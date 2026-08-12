using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Util;

namespace BinDiff.Tests;

public sealed class PatternAnalyzerSignatureTests
{
    private static ComparisonOptions Opts(int patternLength = 16, int minOcc = 2, int maxPerCat = 50) => new()
    {
        PatternLength = patternLength,
        MinPatternOccurrences = minOcc,
        MaxPatternsPerCategory = maxPerCat
    };

    private static PatternSection Run(byte[] a, byte[] b, ComparisonOptions? o = null)
    {
        var section = new PatternAnalyzer().Analyze(new BinaryImage("A.bin", a), new BinaryImage("B.bin", b), o ?? Opts());
        return Assert.IsType<PatternSection>(section);
    }

    [Fact]
    public void IdenticalInputs_YieldFullOverlap_NoUniques()
    {
        var buf = new byte[1024];
        for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(i * 31 + 7);

        var s = Run(buf, (byte[])buf.Clone());

        Assert.Null(s.Error);
        Assert.NotNull(s.SimilarityPercent);
        Assert.Equal(100.0, s.SimilarityPercent!.Value, 6);
        Assert.Empty(s.UniqueToA);
        Assert.Empty(s.UniqueToB);
        Assert.NotEmpty(s.CommonPatterns);
    }

    [Fact]
    public void DisjointInputs_YieldZeroOverlap()
    {
        var a = new byte[512];
        var b = new byte[512];
        for (int i = 0; i < a.Length; i++) { a[i] = 0x00; b[i] = 0xFF; }

        var s = Run(a, b);

        Assert.Null(s.Error);
        Assert.Equal(0.0, s.SimilarityPercent!.Value, 6);
        Assert.Empty(s.CommonPatterns);
    }

    [Fact]
    public void BothEmpty_Is100_OneEmpty_Is0()
    {
        var both = Run(Array.Empty<byte>(), Array.Empty<byte>());
        Assert.Null(both.Error);
        Assert.Equal(100.0, both.SimilarityPercent!.Value, 6);

        var one = Run(new byte[64], Array.Empty<byte>());
        Assert.Null(one.Error);
        Assert.Equal(0.0, one.SimilarityPercent!.Value, 6);
    }

    [Fact]
    public void TinyInputs_ShorterThanL_CompareRawBytes()
    {
        var o = Opts(patternLength: 16);
        var eq = Run(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, o);
        Assert.Equal(100.0, eq.SimilarityPercent!.Value, 6);

        var ne = Run(new byte[] { 1, 2, 3 }, new byte[] { 9, 9, 9 }, o);
        Assert.Equal(0.0, ne.SimilarityPercent!.Value, 6);
    }

    [Fact]
    public void SharedRun_AppearsInCommon_TailsAppearInUniques()
    {
        // Distinctive shared 16-byte run, repeated so it clears MinPatternOccurrences,
        // plus different repeated tails per file.
        var shared = new byte[16];
        for (int i = 0; i < 16; i++) shared[i] = (byte)(0xA0 + i);

        var tailA = new byte[16];
        for (int i = 0; i < 16; i++) tailA[i] = (byte)(0x10 + i);

        var tailB = new byte[16];
        for (int i = 0; i < 16; i++) tailB[i] = (byte)(0xE0 + i);

        // Repeat blocks so each 16-gram at block boundaries recurs.
        var a = Concat(shared, shared, shared, tailA, tailA, tailA);
        var b = Concat(shared, shared, shared, tailB, tailB, tailB);

        var s = Run(a, b, Opts(patternLength: 16, minOcc: 2));

        Assert.Null(s.Error);

        string sharedHex = HexUtil.ToHex(shared);
        Assert.Contains(s.CommonPatterns, p => p.Hex == sharedHex);

        string tailAHex = HexUtil.ToHex(tailA);
        string tailBHex = HexUtil.ToHex(tailB);
        Assert.Contains(s.UniqueToA, p => p.Hex == tailAHex);
        Assert.Contains(s.UniqueToB, p => p.Hex == tailBHex);

        // Unique-to-A hits must carry only A-side data.
        foreach (var hit in s.UniqueToA)
        {
            Assert.Equal(0, hit.CountB);
            Assert.Equal(-1, hit.FirstOffsetB);
            Assert.True(hit.FirstOffsetA >= 0);
        }
    }

    [Fact]
    public void Determinism_SameInputsSameOutput()
    {
        var a = new byte[300];
        var b = new byte[300];
        for (int i = 0; i < 300; i++) { a[i] = (byte)(i % 17); b[i] = (byte)((i * 3) % 23); }

        var s1 = Run(a, b);
        var s2 = Run(a, b);

        Assert.Equal(s1.SimilarityPercent!.Value, s2.SimilarityPercent!.Value, 10);
        Assert.Equal(s1.CommonPatterns.Count, s2.CommonPatterns.Count);
        Assert.Equal(s1.UniqueToA.Count, s2.UniqueToA.Count);
    }

    [Fact]
    public void RunsViaEngine_ProducesFiniteScore()
    {
        var engine = new AnalyzerEngine(new IAnalyzer[] { new PatternAnalyzer() });
        var opts = new ComparisonOptions
        {
            EnabledModules = new HashSet<AnalyzerModule> { AnalyzerModule.Patterns }
        };
        var a = new byte[500];
        var b = new byte[500];
        for (int i = 0; i < 500; i++) { a[i] = (byte)i; b[i] = (byte)(i + 5); }

        var result = engine.Compare(new BinaryImage("A.bin", a), new BinaryImage("B.bin", b), opts);
        var section = Assert.Single(result.Sections);
        Assert.Null(section.Error);
        Assert.NotNull(section.SimilarityPercent);
        Assert.False(double.IsNaN(section.SimilarityPercent!.Value));
        Assert.InRange(section.SimilarityPercent!.Value, 0.0, 100.0);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        var buf = new byte[total];
        int off = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, buf, off, p.Length); off += p.Length; }
        return buf;
    }
}
