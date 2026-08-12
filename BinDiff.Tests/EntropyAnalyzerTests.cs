using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Util;

namespace BinDiff.Tests;

public sealed class EntropyAnalyzerModuleTests
{
    private static readonly ComparisonOptions Options = new();

    private static EntropySection Run(byte[] a, byte[] b, ComparisonOptions? options = null)
    {
        var analyzer = new EntropyAnalyzer();
        var section = analyzer.Analyze(
            new BinaryImage("A.bin", a),
            new BinaryImage("B.bin", b),
            options ?? Options);
        return Assert.IsType<EntropySection>(section);
    }

    private static byte[] LowEntropy(int len) => new byte[len]; // all zeros

    private static byte[] HighEntropy(int len)
    {
        // 0,1,2,...,255,0,1,... — every 256-byte window is uniformly distributed => entropy ~8.
        var data = new byte[len];
        for (int i = 0; i < len; i++) data[i] = (byte)(i & 0xFF);
        return data;
    }

    [Fact]
    public void IdenticalInputs_YieldNearPerfectSimilarity()
    {
        var data = HighEntropy(4096);
        var s = Run(data, (byte[])data.Clone());

        Assert.Null(s.Error);
        Assert.NotNull(s.SimilarityPercent);
        Assert.True(s.SimilarityPercent!.Value > 99.9,
            $"identical inputs should be ~100%, got {s.SimilarityPercent}");
        Assert.Equal(s.MeanEntropyA, s.MeanEntropyB, 6);
        Assert.Equal(Options.EntropyBlockSize, s.BlockSize);
    }

    [Fact]
    public void DisjointEntropyProfiles_YieldLowSimilarity()
    {
        // All-zero (entropy ~0) vs. uniform (entropy ~8): mean |diff| ~8 => similarity ~0.
        var s = Run(LowEntropy(8192), HighEntropy(8192));

        Assert.Null(s.Error);
        Assert.NotNull(s.SimilarityPercent);
        Assert.True(s.SimilarityPercent!.Value < 5.0,
            $"maximally different profiles should be near 0%, got {s.SimilarityPercent}");
        Assert.True(s.MeanEntropyA < 0.01, $"all-zero mean entropy should be ~0, got {s.MeanEntropyA}");
        Assert.True(s.MeanEntropyB > 7.0, $"uniform mean entropy should be high, got {s.MeanEntropyB}");
    }

    [Fact]
    public void BothEmpty_Yield100_And_OneEmpty_Yields0()
    {
        var bothEmpty = Run(Array.Empty<byte>(), Array.Empty<byte>());
        Assert.Null(bothEmpty.Error);
        Assert.Equal(100.0, bothEmpty.SimilarityPercent);
        Assert.Empty(bothEmpty.ProfileA);
        Assert.Empty(bothEmpty.ProfileB);
        Assert.Equal(0.0, bothEmpty.MeanEntropyA);
        Assert.Equal(0.0, bothEmpty.MaxEntropyB);

        var oneEmpty = Run(Array.Empty<byte>(), HighEntropy(1024));
        Assert.Null(oneEmpty.Error);
        Assert.Equal(0.0, oneEmpty.SimilarityPercent);
    }

    [Fact]
    public void HighEntropyFraction_ReflectsPackedContent()
    {
        // Module-specific property: a uniform buffer should report a high high-entropy fraction
        // and a max entropy near 8, while a zero buffer reports ~0 on both.
        var high = Run(HighEntropy(8192), HighEntropy(8192));
        Assert.True(high.HighEntropyFractionA > 0.5,
            $"uniform data should mostly exceed the high-entropy threshold, got {high.HighEntropyFractionA}");
        Assert.True(high.MaxEntropyA > 7.5 && high.MaxEntropyA <= 8.0);

        var low = Run(LowEntropy(8192), LowEntropy(8192));
        Assert.Equal(0.0, low.HighEntropyFractionA);
        Assert.Equal(0.0, low.HighEntropyFractionB);
        Assert.Equal(100.0, low.SimilarityPercent); // two flat profiles are identical
    }

    [Fact]
    public void OutputsAreFinite_AndPercentageClamped()
    {
        var s = Run(HighEntropy(3000), LowEntropy(5000));

        Assert.Null(s.Error);
        Assert.NotNull(s.SimilarityPercent);
        double pct = s.SimilarityPercent!.Value;
        Assert.InRange(pct, 0.0, 100.0);
        Assert.True(double.IsFinite(s.MeanEntropyA));
        Assert.True(double.IsFinite(s.MeanEntropyB));
        Assert.True(double.IsFinite(s.MaxEntropyA));
        Assert.All(s.ProfileA, v => Assert.True(double.IsFinite(v) && v >= 0.0 && v <= 8.0));
        Assert.All(s.ProfileB, v => Assert.True(double.IsFinite(v) && v >= 0.0 && v <= 8.0));
    }

    [Fact]
    public void Deterministic_RepeatedRunsMatch()
    {
        var a = HighEntropy(4096);
        var b = LowEntropy(4096);
        var s1 = Run(a, b);
        var s2 = Run((byte[])a.Clone(), (byte[])b.Clone());
        Assert.Equal(s1.SimilarityPercent, s2.SimilarityPercent);
        Assert.Equal(s1.MeanEntropyA, s2.MeanEntropyA, 12);
    }

    [Fact]
    public void RunsThroughEngine_WithoutThrowing()
    {
        var engine = new AnalyzerEngine(new IAnalyzer[] { new EntropyAnalyzer() });
        var opts = new ComparisonOptions { EnabledModules = new() { AnalyzerModule.Entropy } };
        var result = engine.Compare(
            new BinaryImage("A.bin", HighEntropy(2048)),
            new BinaryImage("B.bin", HighEntropy(2048)),
            opts);

        var section = Assert.Single(result.Sections);
        var entropy = Assert.IsType<EntropySection>(section);
        Assert.Null(entropy.Error);
        Assert.True(entropy.SimilarityPercent!.Value > 99.9);
    }
}
