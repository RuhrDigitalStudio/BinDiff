using System.Text;
using BinDiff.Core.Model;
using BinDiff.Core.Util;

namespace BinDiff.Core.Analyzers;

/// <summary>
/// Fuzzy-hash similarity via two dependency-free methods:
///   (A) MinHash over k-gram shingles  -> robust Jaccard estimate (headline score)
///   (B) CTPH (ssdeep-style context-triggered piecewise hashing) -> locality digest + edit-distance score
/// Deterministic: all "random" coefficients derive from a fixed seed. Analyze never throws.
/// </summary>
public sealed class FuzzyHashAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.FuzzyHash;

    // FNV-1a 64-bit constants.
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    // Alphabet for CTPH digests: 64 chars => 6 bits per emitted piece.
    private const string B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    // Cap on CTPH digest length per block size (ssdeep uses ~64). Keeps edit distance bounded.
    private const int MaxDigestLen = 64;

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        var section = new FuzzyHashSection();
        try
        {
            int k = Math.Max(1, options.ShingleK);
            section.ShingleK = k;

            byte[] da = a.Data;
            byte[] db = b.Data;

            // Global empty-file rules (hard rule 4).
            if (da.Length == 0 && db.Length == 0)
            {
                section.MinHashSimilarityPercent = 100.0;
                section.CtphSimilarityPercent = 100.0;
                section.CtphDigestA = "0::";
                section.CtphDigestB = "0::";
                section.SimilarityPercent = 100.0;
                return section;
            }
            if (da.Length == 0 || db.Length == 0)
            {
                section.MinHashSimilarityPercent = 0.0;
                section.CtphSimilarityPercent = 0.0;
                section.CtphDigestA = CtphDigest(da, k);   // one of these is the empty digest
                section.CtphDigestB = CtphDigest(db, k);
                section.SimilarityPercent = 0.0;
                return section;
            }

            // ---- (A) MinHash via bottom-k (KMV) sketch ------------------------
            // One 64-bit hash per shingle (not one-per-permutation): O(len·log k) instead of
            // O(len·permutations) with a 128-bit modular multiply each — same Jaccard estimate,
            // ~100x less work, so large files finish in milliseconds.
            int sketchSize = Math.Max(1, options.MinHashPermutations);
            var sketchA = BottomKSketch(da, k, sketchSize);
            var sketchB = BottomKSketch(db, k, sketchSize);
            section.MinHashSimilarityPercent = Clamp(BottomKJaccard(sketchA, sketchB, sketchSize) * 100.0);

            // ---- (B) CTPH -----------------------------------------------------
            string digestA = CtphDigest(da, k);
            string digestB = CtphDigest(db, k);
            section.CtphDigestA = digestA;
            section.CtphDigestB = digestB;
            section.CtphSimilarityPercent = Clamp(CtphCompare(digestA, digestB));

            // Headline = MinHash (more robust).
            section.SimilarityPercent = section.MinHashSimilarityPercent;
            return section;
        }
        catch (Exception ex)
        {
            return new FuzzyHashSection
            {
                Error = ex.Message,
                MinHashSimilarityPercent = 0,
                CtphSimilarityPercent = 0,
                ShingleK = Math.Max(1, options.ShingleK)
            };
        }
    }

    // ------------------------------------------------------------------ MinHash (bottom-k)

    /// <summary>
    /// Bottom-k (KMV) MinHash sketch: the k smallest DISTINCT 64-bit shingle hashes of a file.
    /// Estimating Jaccard from two such sketches needs only ONE hash per shingle, so the cost is
    /// O(len·log k) instead of the classic O(len·permutations) with a 128-bit modular multiply
    /// per permutation — the change that turns multi-second runs into milliseconds on large files.
    /// For files shorter than k (shingle length) the whole file is one shingle. Fully deterministic.
    /// </summary>
    private static SortedSet<ulong> BottomKSketch(byte[] data, int k, int sketchSize)
    {
        var sketch = new SortedSet<ulong>();
        int len = data.Length;
        int count = len < k ? 1 : len - k + 1;
        int window = len < k ? len : k;

        for (int s = 0; s < count; s++)
        {
            ulong h = ShingleHash(data, s, window);
            if (sketch.Contains(h)) continue;               // distinct values only
            if (sketch.Count < sketchSize) sketch.Add(h);
            else if (h < sketch.Max)                         // keep the k smallest
            {
                sketch.Remove(sketch.Max);
                sketch.Add(h);
            }
        }
        return sketch;
    }

    /// <summary>
    /// Bottom-k Jaccard estimate: over the k smallest distinct hashes of the union of both
    /// sketches, the fraction that appear in BOTH sketches. Identical files => 1.0 exactly;
    /// disjoint => ~0. Range [0,1].
    /// </summary>
    private static double BottomKJaccard(SortedSet<ulong> a, SortedSet<ulong> b, int k)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;

        var union = new SortedSet<ulong>(a);
        union.UnionWith(b);
        int m = Math.Min(k, union.Count);
        if (m <= 0) return 1.0;

        int inBoth = 0, seen = 0;
        foreach (var x in union)                            // ascending order
        {
            if (seen >= m) break;
            seen++;
            if (a.Contains(x) && b.Contains(x)) inBoth++;
        }
        return (double)inBoth / m;
    }

    private static ulong ShingleHash(byte[] data, int start, int window)
    {
        ulong h = FnvOffset;
        int end = start + window;
        for (int i = start; i < end; i++)
        {
            h ^= data[i];
            h *= FnvPrime;
        }
        return h;
    }

    // --------------------------------------------------------------------- CTPH

    /// <summary>
    /// Build an ssdeep-style CTPH digest "blocksize:hashB:hash2B".
    /// blocksize b = 3 * 2^floor(log2(len/64 + 1)), clamped to >= 3.
    /// A 7-byte rolling hash triggers a piece boundary when (roll % bs) == bs-1;
    /// at each boundary one B64 char = (pieceFnv &amp; 63) is emitted and the piece hash resets.
    /// Two parallel digests are produced at block sizes b and 2b (ssdeep does the same),
    /// so a compatible neighbour can be found when file sizes drift.
    /// </summary>
    private static string CtphDigest(byte[] data, int k)
    {
        int len = data.Length;
        if (len == 0) return "0::";

        long bs = BlockSize(len);
        var hashB = PiecewiseHash(data, bs);
        var hash2B = PiecewiseHash(data, bs * 2);
        return bs.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ":" + hashB + ":" + hash2B;
    }

    private static long BlockSize(int len)
    {
        // b = 3 * 2^floor(log2(len/64 + 1)), >= 3.
        long ratio = len / 64 + 1;
        int exp = 0;
        while ((1L << (exp + 1)) <= ratio) exp++;
        long bs = 3L * (1L << exp);
        return bs < 3 ? 3 : bs;
    }

    /// <summary>
    /// Emit one B64 char per triggered block. Rolling hash is a fixed 7-byte window sum-of-shifts;
    /// piece hash is FNV-1a. A trailing piece is flushed so tiny inputs still get a char.
    /// Output capped at MaxDigestLen chars to bound the later edit-distance cost.
    /// </summary>
    private static string PiecewiseHash(byte[] data, long bs)
    {
        if (bs < 1) bs = 1;
        const int WindowSize = 7;
        var sb = new StringBuilder();

        // Rolling window state.
        var win = new byte[WindowSize];
        int wi = 0;
        uint x = 0, y = 0, z = 0; // rollhash accumulators (spamsum-style)
        int filled = 0;

        ulong piece = FnvOffset;
        bool anyByteInPiece = false;

        int len = data.Length;
        for (int i = 0; i < len; i++)
        {
            byte c = data[i];

            // Update piece (FNV-1a).
            piece ^= c;
            piece *= FnvPrime;
            anyByteInPiece = true;

            // Update rolling hash (spamsum roll_hash).
            y = y - x + (uint)(WindowSize * c);
            x = x - win[wi] + c;
            win[wi] = c;
            wi = (wi + 1) % WindowSize;
            z <<= 5;
            z ^= c;
            if (filled < WindowSize) filled++;
            uint roll = x + y + z;

            if (filled >= WindowSize && (roll % (uint)bs) == (uint)bs - 1)
            {
                sb.Append(B64[(int)(piece & 63UL)]);
                piece = FnvOffset;
                anyByteInPiece = false;
                if (sb.Length >= MaxDigestLen) return sb.ToString();
            }
        }

        // Flush trailing partial piece so short inputs are not empty.
        if (anyByteInPiece && sb.Length < MaxDigestLen)
            sb.Append(B64[(int)(piece & 63UL)]);

        return sb.ToString();
    }

    /// <summary>
    /// Compare two CTPH digests. Block sizes are compatible when equal or off-by-one power
    /// (b vs 2b), matching ssdeep's rule; we then compare the appropriate hash strings and
    /// score 100 * (1 - normalizedEditDistance). Incompatible sizes -> 0.
    /// </summary>
    private static double CtphCompare(string digestA, string digestB)
    {
        if (!TryParse(digestA, out long ba, out string aB, out string a2B)) return 0;
        if (!TryParse(digestB, out long bb, out string bB, out string b2B)) return 0;
        if (ba == 0 || bb == 0) return 0;

        // Choose the overlapping block size and its hash strings.
        string sa, sb2;
        if (ba == bb) { sa = aB; sb2 = bB; }
        else if (ba == bb * 2) { sa = aB; sb2 = b2B; }   // A's b aligns with B's 2b
        else if (ba * 2 == bb) { sa = a2B; sb2 = bB; }   // A's 2b aligns with B's b
        else return 0;                                    // incompatible

        if (sa.Length == 0 && sb2.Length == 0) return 100.0;

        int dist = EditDistance(sa, sb2);
        int maxLen = Math.Max(sa.Length, sb2.Length);
        if (maxLen == 0) return 0;
        double sim = (1.0 - (double)dist / maxLen) * 100.0;
        return sim;
    }

    private static bool TryParse(string digest, out long bs, out string hB, out string h2B)
    {
        bs = 0; hB = ""; h2B = "";
        if (string.IsNullOrEmpty(digest)) return false;
        string[] parts = digest.Split(':');
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[0], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out bs)) return false;
        hB = parts[1];
        h2B = parts[2];
        return true;
    }

    /// <summary>Standard Levenshtein edit distance, O(len_a * len_b) with rolling rows.
    /// Inputs are bounded to MaxDigestLen so this stays cheap.</summary>
    private static int EditDistance(string s, string t)
    {
        int m = s.Length, n = t.Length;
        if (m == 0) return n;
        if (n == 0) return m;

        var prev = new int[n + 1];
        var cur = new int[n + 1];
        for (int j = 0; j <= n; j++) prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            cur[0] = i;
            char sc = s[i - 1];
            for (int j = 1; j <= n; j++)
            {
                int cost = sc == t[j - 1] ? 0 : 1;
                int del = prev[j] + 1;
                int ins = cur[j - 1] + 1;
                int sub = prev[j - 1] + cost;
                cur[j] = Math.Min(Math.Min(del, ins), sub);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[n];
    }

    private static double Clamp(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        if (v < 0) return 0;
        if (v > 100) return 100;
        return v;
    }
}
