using System;
using System.Linq;
using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Util;

namespace BinDiff.Tests;

public sealed class FuzzyHashAnalyzerTests
{
    private static ComparisonOptions Opts() => new();

    private static FuzzyHashSection Run(byte[] a, byte[] b)
    {
        var analyzer = new FuzzyHashAnalyzer();
        var section = analyzer.Analyze(new BinaryImage("A.bin", a), new BinaryImage("B.bin", b), Opts());
        return Assert.IsType<FuzzyHashSection>(section);
    }

    private static byte[] Seeded(int n, int seed)
    {
        // Deterministic pseudo-random bytes (no external state).
        var data = new byte[n];
        uint state = (uint)seed | 1u;
        for (int i = 0; i < n; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            data[i] = (byte)(state & 0xFF);
        }
        return data;
    }

    [Fact]
    public void IdenticalInputs_MinHashFull_AndCtphHigh()
    {
        var data = Seeded(4096, 42);
        var s = Run(data, (byte[])data.Clone());

        Assert.Null(s.Error);
        Assert.Equal(AnalyzerModule.FuzzyHash, s.Module);
        Assert.Equal(100.0, s.MinHashSimilarityPercent, 6);
        Assert.Equal(s.MinHashSimilarityPercent, s.SimilarityPercent!.Value, 6);
        Assert.True(s.CtphSimilarityPercent >= 99.0, $"CTPH was {s.CtphSimilarityPercent}");
        Assert.Equal(s.CtphDigestA, s.CtphDigestB);
    }

    [Fact]
    public void DisjointInputs_BothLow()
    {
        var a = Seeded(4096, 1);
        var b = Seeded(4096, 999);
        var s = Run(a, b);

        Assert.Null(s.Error);
        Assert.True(s.MinHashSimilarityPercent < 20.0, $"MinHash was {s.MinHashSimilarityPercent}");
        Assert.True(s.CtphSimilarityPercent < 50.0, $"CTPH was {s.CtphSimilarityPercent}");
    }

    [Fact]
    public void BothEmpty_Returns100()
    {
        var s = Run(Array.Empty<byte>(), Array.Empty<byte>());
        Assert.Null(s.Error);
        Assert.Equal(100.0, s.SimilarityPercent!.Value, 6);
        Assert.Equal(100.0, s.MinHashSimilarityPercent, 6);
    }

    [Fact]
    public void OneEmpty_Returns0()
    {
        var s = Run(Seeded(1024, 7), Array.Empty<byte>());
        Assert.Null(s.Error);
        Assert.Equal(0.0, s.SimilarityPercent!.Value, 6);
        Assert.Equal(0.0, s.MinHashSimilarityPercent, 6);
    }

    [Fact]
    public void TinyInput_ShorterThanK_DoesNotThrow_AndScores100OnIdentical()
    {
        // ShingleK default is 8; use a 3-byte file (whole file is one shingle).
        var tiny = new byte[] { 0x4D, 0x5A, 0x90 };
        var s = Run(tiny, (byte[])tiny.Clone());
        Assert.Null(s.Error);
        Assert.Equal(100.0, s.MinHashSimilarityPercent, 6);
    }

    [Fact]
    public void SmallAppendedBlock_KeepsMinHashHigh()
    {
        // Module-specific property: appending a small block leaves most shingles intact.
        var baseData = Seeded(8192, 123);
        var appended = baseData.Concat(Seeded(256, 55)).ToArray();
        var s = Run(baseData, appended);

        Assert.Null(s.Error);
        Assert.True(s.MinHashSimilarityPercent >= 90.0,
            $"MinHash dropped too far after small append: {s.MinHashSimilarityPercent}");
    }

    [Fact]
    public void AllOutputs_AreFinite_AndClamped()
    {
        var s = Run(Seeded(2000, 3), Seeded(2000, 4));
        foreach (var v in new[] { s.MinHashSimilarityPercent, s.CtphSimilarityPercent, s.SimilarityPercent ?? 0 })
        {
            Assert.False(double.IsNaN(v));
            Assert.False(double.IsInfinity(v));
            Assert.InRange(v, 0.0, 100.0);
        }
        Assert.Equal(8, s.ShingleK); // default
    }

    [Fact]
    public void Deterministic_SameInputsSameResult()
    {
        var a = Seeded(3000, 11);
        var b = Seeded(3000, 22);
        var s1 = Run(a, b);
        var s2 = Run((byte[])a.Clone(), (byte[])b.Clone());
        Assert.Equal(s1.MinHashSimilarityPercent, s2.MinHashSimilarityPercent, 9);
        Assert.Equal(s1.CtphSimilarityPercent, s2.CtphSimilarityPercent, 9);
        Assert.Equal(s1.CtphDigestA, s2.CtphDigestA);
        Assert.Equal(s1.CtphDigestB, s2.CtphDigestB);
    }

    [Fact]
    public void ViaEngine_ProducesSection()
    {
        var engine = new AnalyzerEngine(new IAnalyzer[] { new FuzzyHashAnalyzer() });
        var a = new BinaryImage("A.bin", Seeded(1500, 8));
        var b = new BinaryImage("B.bin", Seeded(1500, 8));
        var result = engine.Compare(a, b, Opts());
        Assert.Contains(result.Sections, sec => sec.Module == AnalyzerModule.FuzzyHash);
    }
}
