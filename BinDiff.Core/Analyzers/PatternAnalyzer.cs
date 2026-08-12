using BinDiff.Core.Model;
using BinDiff.Core.Util;

namespace BinDiff.Core.Analyzers;

/// <summary>
/// Extracts fixed-length byte signatures (L = <see cref="ComparisonOptions.PatternLength"/>) from each
/// file by sliding a window of length L (step 1). Each L-gram is keyed by a 64-bit hash to bound memory;
/// the first offset of each key is retained so the actual bytes can be recovered for hex display.
///
/// Headline score ("pattern overlap") = Jaccard of the two distinct L-gram hash sets * 100.
///
/// Notes / trade-offs:
///  * Hashing: a polynomial rolling hash (O(1) per window, O(n) overall) is passed through a
///    splitmix64-style avalanche finalizer with FIXED constants — fully deterministic, no randomness.
///  * Hash collisions are accepted for this heuristic: two distinct L-grams mapping to the same 64-bit
///    key are treated as the same signature. With a 64-bit key over multi-MB inputs this is negligible
///    and only marginally affects the Jaccard estimate / hit lists.
///  * To bound memory the number of distinct entries per file is capped
///    (<see cref="MaxDistinctPerFile"/>); once reached, new distinct L-grams are ignored but counts for
///    already-tracked keys keep accumulating. Sufficient for the defensive/heuristic use case.
/// </summary>
public sealed class PatternAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.Patterns;

    // Polynomial rolling-hash base (fixed) — reuse the FNV prime as a well-mixing odd base.
    private const ulong Base = 1099511628211UL;

    // Memory cap: at most this many distinct L-grams tracked per file.
    private const int MaxDistinctPerFile = 2_000_000;

    private sealed class Entry
    {
        public int Count;
        public long FirstOffset;
    }

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        int L = options.PatternLength;
        var section = new PatternSection { PatternLength = L };

        try
        {
            byte[] da = a.Data ?? Array.Empty<byte>();
            byte[] db = b.Data ?? Array.Empty<byte>();

            // Guard invalid L: fall back to raw whole-buffer equality.
            if (L <= 0)
            {
                section.SimilarityPercent = BuffersEqual(da, db) ? 100.0 : 0.0;
                return section;
            }

            bool aHasGrams = da.Length >= L;
            bool bHasGrams = db.Length >= L;

            // Neither file long enough to contain an L-gram: compare raw bytes (both empty => equal => 100).
            if (!aHasGrams && !bHasGrams)
            {
                section.SimilarityPercent = BuffersEqual(da, db) ? 100.0 : 0.0;
                return section;
            }

            // Exactly one file has L-grams -> no shared signatures -> Jaccard = 0.
            if (!aHasGrams || !bHasGrams)
            {
                section.SimilarityPercent = 0.0;
                if (aHasGrams)
                {
                    var mapOnlyA = BuildMap(da, L);
                    section.UniqueToA = BuildUniqueHits(mapOnlyA, da, L, options, forA: true, other: null);
                }
                else
                {
                    var mapOnlyB = BuildMap(db, L);
                    section.UniqueToB = BuildUniqueHits(mapOnlyB, db, L, options, forA: false, other: null);
                }
                return section;
            }

            var mapA = BuildMap(da, L);
            var mapB = BuildMap(db, L);

            // --- Jaccard of distinct hash sets (iterate the smaller map) ---
            int inter = 0;
            var (small, large) = mapA.Count <= mapB.Count ? (mapA, mapB) : (mapB, mapA);
            foreach (var key in small.Keys)
                if (large.ContainsKey(key)) inter++;

            long union = (long)mapA.Count + mapB.Count - inter;
            double jaccard = union > 0 ? (double)inter / union : 1.0;
            section.SimilarityPercent = Clamp01(jaccard) * 100.0;

            // --- Common patterns: keys present in both files ---
            var common = new List<PatternHit>(inter);
            foreach (var kv in mapA)
            {
                if (mapB.TryGetValue(kv.Key, out var eb))
                {
                    var ea = kv.Value;
                    common.Add(new PatternHit
                    {
                        Hex = SafeHex(da, ea.FirstOffset, L),
                        CountA = ea.Count,
                        CountB = eb.Count,
                        FirstOffsetA = ea.FirstOffset,
                        FirstOffsetB = eb.FirstOffset
                    });
                }
            }
            common.Sort((x, y) =>
            {
                int mx = Math.Min(x.CountA, x.CountB);
                int my = Math.Min(y.CountA, y.CountB);
                int c = my.CompareTo(mx);                 // desc by min(count)
                return c != 0 ? c : string.CompareOrdinal(x.Hex, y.Hex); // then asc by hex
            });
            TrimTo(common, options.MaxPatternsPerCategory);
            section.CommonPatterns = common;

            // --- Unique to A / B (count >= MinPatternOccurrences, key absent from the other file) ---
            section.UniqueToA = BuildUniqueHits(mapA, da, L, options, forA: true, other: mapB);
            section.UniqueToB = BuildUniqueHits(mapB, db, L, options, forA: false, other: mapA);

            return section;
        }
        catch (Exception ex)
        {
            return new PatternSection
            {
                PatternLength = L,
                Error = ex.Message
            };
        }
    }

    private static List<PatternHit> BuildUniqueHits(
        Dictionary<ulong, Entry> map, byte[] data, int L, ComparisonOptions options,
        bool forA, Dictionary<ulong, Entry>? other)
    {
        var hits = new List<PatternHit>();
        foreach (var kv in map)
        {
            if (kv.Value.Count < options.MinPatternOccurrences) continue;
            if (other != null && other.ContainsKey(kv.Key)) continue;

            var e = kv.Value;
            hits.Add(new PatternHit
            {
                Hex = SafeHex(data, e.FirstOffset, L),
                CountA = forA ? e.Count : 0,
                CountB = forA ? 0 : e.Count,
                FirstOffsetA = forA ? e.FirstOffset : -1,
                FirstOffsetB = forA ? -1 : e.FirstOffset
            });
        }

        hits.Sort((x, y) =>
        {
            int cx = forA ? x.CountA : x.CountB;
            int cy = forA ? y.CountA : y.CountB;
            int c = cy.CompareTo(cx);                     // desc by count
            return c != 0 ? c : string.CompareOrdinal(x.Hex, y.Hex);
        });
        TrimTo(hits, options.MaxPatternsPerCategory);
        return hits;
    }

    /// <summary>
    /// Builds the map of distinct L-gram hash keys -> (count, first offset) via an O(n) polynomial
    /// rolling hash. Recomputing each window from scratch would be O(n*L); the rolling form removes the
    /// outgoing byte and adds the incoming one in O(1) per step.
    /// </summary>
    private static Dictionary<ulong, Entry> BuildMap(byte[] data, int L)
    {
        var map = new Dictionary<ulong, Entry>();
        int n = data.Length;
        if (n < L) return map;

        int windows = n - L + 1;

        // Base^(L-1) for the rolling removal step.
        ulong basePowL = 1UL;
        for (int i = 0; i < L - 1; i++) basePowL *= Base;

        // Hash of the first window [0, L).
        ulong roll = 0UL;
        for (int i = 0; i < L; i++)
            roll = roll * Base + data[i];
        AddOrUpdate(map, MixSeed(roll), 0);

        for (int start = 1; start < windows; start++)
        {
            // remove outgoing byte data[start-1], add incoming data[start+L-1]
            roll -= (ulong)data[start - 1] * basePowL;
            roll = roll * Base + data[start + L - 1];
            AddOrUpdate(map, MixSeed(roll), start);
        }

        return map;
    }

    // splitmix64 finalizer over the polynomial hash to avalanche bits — fixed constants, deterministic.
    private static ulong MixSeed(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return x;
    }

    private static void AddOrUpdate(Dictionary<ulong, Entry> map, ulong key, long offset)
    {
        if (map.TryGetValue(key, out var e))
        {
            e.Count++;
        }
        else
        {
            if (map.Count >= MaxDistinctPerFile) return; // cap distinct entries (documented)
            map[key] = new Entry { Count = 1, FirstOffset = offset };
        }
    }

    private static void TrimTo<T>(List<T> list, int max)
    {
        if (max < 0) max = 0;
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }

    private static string SafeHex(byte[] data, long offset, int len)
    {
        if (offset < 0 || len <= 0 || offset >= data.Length) return "";
        if (offset + len > data.Length) len = (int)(data.Length - offset);
        if (len <= 0) return "";
        return HexUtil.ToHex(data.AsSpan((int)offset, len));
    }

    private static bool BuffersEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    private static double Clamp01(double v)
    {
        if (double.IsNaN(v)) return 0.0;
        if (v < 0.0) return 0.0;
        if (v > 1.0) return 1.0;
        return v;
    }
}
