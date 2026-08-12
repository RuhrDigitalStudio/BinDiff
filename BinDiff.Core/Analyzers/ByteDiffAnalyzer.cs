using BinDiff.Core.Model;
using BinDiff.Core.Util;

namespace BinDiff.Core.Analyzers;

/// <summary>
/// ByteDiff: shift-robust content-defined chunking (CDC) plus a byte-histogram
/// cosine similarity. Splits each file into variable-length chunks whose boundaries
/// are determined by content (a Gear/rolling hash), not by fixed offsets, so that an
/// insertion or deletion near the start does not shift every downstream chunk — the
/// classic weakness of fixed-block diffing. Chunk identity is a strong FNV-1a-64 hash;
/// set-Jaccard over those hashes is the headline similarity.
/// </summary>
public sealed class ByteDiffAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.ByteDiff;

    // FNV-1a constants used to identify chunks.
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    // A fixed seed keeps chunk boundaries reproducible across machines.
    private const ulong GearSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>256 fixed 64-bit gear values derived from a seeded splitmix64 PRNG.</summary>
    private static readonly ulong[] GearTable = BuildGearTable();

    private static ulong[] BuildGearTable()
    {
        var table = new ulong[256];
        ulong state = GearSeed;
        for (int i = 0; i < 256; i++)
        {
            // splitmix64
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            table[i] = z;
        }
        return table;
    }

    private readonly struct Chunk
    {
        public readonly long Offset;
        public readonly int Length;
        public readonly ulong Hash;
        public Chunk(long offset, int length, ulong hash)
        {
            Offset = offset;
            Length = length;
            Hash = hash;
        }
    }

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        var section = new ByteDiffSection();
        try
        {
            byte[] da = a.Data;
            byte[] db = b.Data;

            // Empty-input scores are explicit so zero-length vectors never reach the main path.
            bool aEmpty = da.LongLength == 0;
            bool bEmpty = db.LongLength == 0;
            if (aEmpty && bEmpty)
            {
                section.JaccardPercent = 100.0;
                section.HistogramCosinePercent = 100.0;
                section.SimilarityPercent = 100.0;
                return section;
            }

            // Clamp user-provided chunk settings before deriving the boundary mask.
            int minSize = Math.Max(1, options.ChunkMinSize);
            int maxSize = Math.Max(minSize, options.ChunkMaxSize);
            int avgSize = Math.Max(1, options.ChunkAvgSize);
            ulong mask = (ulong)(NextPow2(avgSize) - 1);

            var chunksA = Chunkify(da, minSize, maxSize, mask);
            var chunksB = Chunkify(db, minSize, maxSize, mask);

            section.TotalChunksA = chunksA.Count;
            section.TotalChunksB = chunksB.Count;

            // Jaccard and the shared/unique counts use distinct chunk hashes.
            var setA = new HashSet<ulong>();
            foreach (var c in chunksA) setA.Add(c.Hash);
            var setB = new HashSet<ulong>();
            foreach (var c in chunksB) setB.Add(c.Hash);

            // Counting distinct hashes prevents repeated chunks from changing these summary counts.
            int common = 0;
            foreach (var h in setA)
                if (setB.Contains(h)) common++;
            section.CommonChunks = common;
            section.OnlyAChunks = setA.Count - common;
            section.OnlyBChunks = setB.Count - common;

            // One empty hash set has no overlap; the both-empty case returned above.
            int unionCount = setA.Count + setB.Count - common;
            double jaccard = unionCount > 0 ? (double)common / unionCount : 0.0;
            section.JaccardPercent = Clamp01Pct(jaccard * 100.0);

            // Each chunk becomes a GUI span, marked shared when its hash occurs in the other input.
            section.MapA = BuildMap(chunksA, setB);
            section.MapB = BuildMap(chunksB, setA);

            // Byte-histogram cosine similarity (256-bin frequency vectors).
            section.HistogramCosinePercent = Clamp01Pct(HistogramCosine(da, db) * 100.0);

            // Headline score = Jaccard.
            section.SimilarityPercent = section.JaccardPercent;
            return section;
        }
        catch (Exception ex)
        {
            return new ByteDiffSection { Error = ex.Message };
        }
    }

    /// <summary>Content-defined chunking via a Gear rolling hash.</summary>
    private static List<Chunk> Chunkify(byte[] data, int minSize, int maxSize, ulong mask)
    {
        var chunks = new List<Chunk>();
        long n = data.LongLength;
        if (n == 0) return chunks;

        long start = 0;
        ulong h = 0;
        int len = 0;

        for (long i = 0; i < n; i++)
        {
            h = (h << 1) + GearTable[data[i]];
            len++;

            bool boundary = (len >= minSize && (h & mask) == 0) || len >= maxSize;
            if (boundary)
            {
                chunks.Add(new Chunk(start, len, Fnv1a(data, start, len)));
                start = i + 1;
                h = 0;
                len = 0;
            }
        }

        // Trailing bytes form a final chunk even if no boundary condition fired.
        if (len > 0)
            chunks.Add(new Chunk(start, len, Fnv1a(data, start, len)));

        return chunks;
    }

    /// <summary>FNV-1a 64 over data[offset .. offset+length).</summary>
    private static ulong Fnv1a(byte[] data, long offset, int length)
    {
        ulong hash = FnvOffset;
        long end = offset + length;
        for (long i = offset; i < end; i++)
        {
            hash ^= data[i];
            hash *= FnvPrime;
        }
        return hash;
    }

    private static List<ByteSpan> BuildMap(List<Chunk> chunks, HashSet<ulong> otherSet)
    {
        var map = new List<ByteSpan>(chunks.Count);
        foreach (var c in chunks)
            map.Add(new ByteSpan { Offset = c.Offset, Length = c.Length, Shared = otherSet.Contains(c.Hash) });
        return map;
    }

    /// <summary>Cosine similarity of the two 256-bin byte-frequency vectors, in [0,1].</summary>
    private static double HistogramCosine(byte[] da, byte[] db)
    {
        var ha = new long[256];
        var hb = new long[256];
        for (long i = 0; i < da.LongLength; i++) ha[da[i]]++;
        for (long i = 0; i < db.LongLength; i++) hb[db[i]]++;

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < 256; i++)
        {
            double x = ha[i];
            double y = hb[i];
            dot += x * y;
            magA += x * x;
            magB += y * y;
        }
        if (magA <= 0 || magB <= 0) return 0.0; // guard zero vectors (e.g. an empty file)
        double cos = dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        if (double.IsNaN(cos) || double.IsInfinity(cos)) return 0.0;
        return cos < 0 ? 0.0 : (cos > 1 ? 1.0 : cos);
    }

    /// <summary>Smallest power of two >= v (>=1). Used to build the boundary mask.</summary>
    private static int NextPow2(int v)
    {
        if (v <= 1) return 1;
        int p = 1;
        while (p < v && p < (1 << 30)) p <<= 1;
        return p;
    }

    private static double Clamp01Pct(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
        if (v < 0) return 0.0;
        if (v > 100) return 100.0;
        return v;
    }
}
