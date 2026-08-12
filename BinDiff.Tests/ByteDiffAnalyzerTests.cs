using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Util;

namespace BinDiff.Tests;

public sealed class ByteDiffAnalyzerTests
{
    private static ByteDiffSection Run(byte[] a, byte[] b, ComparisonOptions? opts = null)
    {
        var analyzer = new ByteDiffAnalyzer();
        var section = analyzer.Analyze(
            new BinaryImage("A.bin", a),
            new BinaryImage("B.bin", b),
            opts ?? new ComparisonOptions());
        Assert.IsType<ByteDiffSection>(section);
        return (ByteDiffSection)section;
    }

    /// <summary>Deterministic pseudo-random byte buffer (splitmix64) — no unseeded randomness.</summary>
    private static byte[] Pseudo(int length, ulong seed)
    {
        var buf = new byte[length];
        ulong state = seed;
        for (int i = 0; i < length; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            buf[i] = (byte)z;
        }
        return buf;
    }

    [Fact]
    public void IdenticalInputs_ReturnFullSimilarity()
    {
        var data = Pseudo(50_000, 1);
        var s = Run(data, (byte[])data.Clone());

        Assert.Null(s.Error);
        Assert.NotNull(s.SimilarityPercent);
        Assert.Equal(100.0, s.JaccardPercent, 6);
        Assert.Equal(100.0, s.HistogramCosinePercent, 6);
        Assert.Equal(s.JaccardPercent, s.SimilarityPercent!.Value, 6);
        Assert.Equal(0, s.OnlyAChunks);
        Assert.Equal(0, s.OnlyBChunks);
        Assert.True(s.TotalChunksA > 1); // buffer big enough to chunk
        Assert.Equal(s.TotalChunksA, s.MapA.Count);
        Assert.All(s.MapA, span => Assert.True(span.Shared));
    }

    [Fact]
    public void DisjointInputs_ReturnLowSimilarity()
    {
        var a = Pseudo(50_000, 100);
        var b = Pseudo(50_000, 999);
        var s = Run(a, b);

        Assert.Null(s.Error);
        Assert.True(s.JaccardPercent < 5.0, $"expected low Jaccard, got {s.JaccardPercent}");
        Assert.Equal(0, s.CommonChunks);
        // Every numeric output finite and in range.
        Assert.InRange(s.JaccardPercent, 0.0, 100.0);
        Assert.InRange(s.HistogramCosinePercent, 0.0, 100.0);
    }

    [Fact]
    public void BothEmpty_Is100_OneEmpty_Is0()
    {
        var both = Run(Array.Empty<byte>(), Array.Empty<byte>());
        Assert.Null(both.Error);
        Assert.Equal(100.0, both.SimilarityPercent!.Value, 6);

        var oneEmpty = Run(Pseudo(10_000, 7), Array.Empty<byte>());
        Assert.Null(oneEmpty.Error);
        Assert.Equal(0.0, oneEmpty.JaccardPercent, 6);
        Assert.Equal(0.0, oneEmpty.HistogramCosinePercent, 6);
        Assert.Equal(0, oneEmpty.CommonChunks);
    }

    [Fact]
    public void TinyInput_ProducesSingleChunk_NoThrow()
    {
        var s = Run(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 });
        Assert.Null(s.Error);
        Assert.Equal(1, s.TotalChunksA);
        Assert.Equal(1, s.TotalChunksB);
        Assert.Equal(100.0, s.JaccardPercent, 6);
    }

    /// <summary>
    /// Module-specific property: shift robustness. Prepending a handful of bytes must
    /// keep Jaccard high because content-defined chunk boundaries re-align rather than
    /// shifting every downstream chunk.
    /// </summary>
    [Fact]
    public void PrependingFewBytes_KeepsJaccardHigh()
    {
        var baseData = Pseudo(80_000, 42);
        var shifted = new byte[baseData.Length + 5];
        shifted[0] = 0xDE; shifted[1] = 0xAD; shifted[2] = 0xBE; shifted[3] = 0xEF; shifted[4] = 0x00;
        Array.Copy(baseData, 0, shifted, 5, baseData.Length);

        var s = Run(baseData, shifted);
        Assert.Null(s.Error);
        Assert.True(s.JaccardPercent > 80.0,
            $"CDC should be shift-robust; got Jaccard {s.JaccardPercent}");
    }

    [Fact]
    public void RunsThroughEngine()
    {
        var engine = new AnalyzerEngine(new IAnalyzer[] { new ByteDiffAnalyzer() });
        var data = Pseudo(20_000, 5);
        var result = engine.Compare(new BinaryImage("A.bin", data), new BinaryImage("B.bin", (byte[])data.Clone()));
        var section = Assert.Single(result.Sections);
        Assert.Equal(AnalyzerModule.ByteDiff, section.Module);
        Assert.Null(section.Error);
        Assert.Equal(100.0, section.SimilarityPercent!.Value, 6);
    }
}
