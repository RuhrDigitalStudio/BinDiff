using BinDiff.Core.Analyzers;
using BinDiff.Core.Model;
using Xunit;

namespace BinDiff.Tests;

public sealed class StringAnalyzerTests
{
    [Fact]
    public void ExtractsAsciiAndUtf16WithOffsetsAndCounts()
    {
        var a = Image("a.bin", [0, .. Ascii("hello"), 0, .. Utf16("world"), 0, .. Ascii("hello")]);
        var b = Image("b.bin", [.. Ascii("hello"), 0, .. Utf16("other")]);

        var result = Analyze(a, b);

        var shared = Assert.Single(result.CommonStrings);
        Assert.Equal("hello", shared.Value);
        Assert.Equal("ASCII", shared.Encoding);
        Assert.Equal(1, shared.FirstOffsetA);
        Assert.Equal(0, shared.FirstOffsetB);
        Assert.Equal(2, shared.CountA);
        Assert.Equal(1, shared.CountB);
        Assert.Contains(result.UniqueToA, item => item.Value == "world" && item.Encoding == "UTF-16LE");
        Assert.Contains(result.UniqueToB, item => item.Value == "other" && item.Encoding == "UTF-16LE");
    }

    [Fact]
    public void IgnoresShortRunsAndBoundsReportedValues()
    {
        var options = new ComparisonOptions
        {
            MinStringLength = 4,
            MaxStringLength = 6,
            MaxExtractedStrings = 2,
            MaxReportedStrings = 1
        };
        var a = Image("a", [.. Ascii("abc"), 0, .. Ascii("abcdefgh"), 0, .. Ascii("second"), 0, .. Ascii("third")]);

        var result = (StringSection)new StringAnalyzer().Analyze(a, Image("b", []), options);

        Assert.Equal(2, result.DistinctStringsA);
        Assert.Single(result.UniqueToA);
        Assert.Equal(6, result.UniqueToA[0].Value.Length);
    }

    [Fact]
    public void EmptyInputsAreIdenticalAndOneEmptyInputHasNoOverlap()
    {
        Assert.Equal(100, Analyze(Image("a", []), Image("b", [])).SimilarityPercent);
        Assert.Equal(0, Analyze(Image("a", Ascii("visible")), Image("b", [])).SimilarityPercent);
    }

    [Fact]
    public void EncodingIsPartOfStringIdentity()
    {
        var result = Analyze(Image("a", Ascii("same!")), Image("b", Utf16("same!")));

        Assert.Empty(result.CommonStrings);
        Assert.Single(result.UniqueToA);
        Assert.Single(result.UniqueToB);
        Assert.Equal(0, result.SimilarityPercent);
    }

    private static StringSection Analyze(BinaryImage a, BinaryImage b) =>
        (StringSection)new StringAnalyzer().Analyze(a, b, new ComparisonOptions());

    private static BinaryImage Image(string name, byte[] bytes) => new(name, bytes);

    private static byte[] Ascii(string value) => System.Text.Encoding.ASCII.GetBytes(value);

    private static byte[] Utf16(string value) => System.Text.Encoding.Unicode.GetBytes(value);
}
