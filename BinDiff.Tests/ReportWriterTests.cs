using BinDiff.Core.Model;
using BinDiff.Core.Reporting;

namespace BinDiff.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public void JsonIncludesConcreteFieldsForNewSections()
    {
        var result = Result(
            new StringSection
            {
                SimilarityPercent = 50,
                CommonStrings = [new StringHit { Value = "shared", Encoding = "ASCII" }]
            },
            new DotNetSection
            {
                Applicable = true,
                SimilarityPercent = 75,
                A = new DotNetProfile { AssemblyName = "A" },
                B = new DotNetProfile { AssemblyName = "B" },
                TypesOnlyA = ["Example.OldType"]
            });

        var json = JsonReportWriter.ToJson(result);

        Assert.Contains("\"commonStrings\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"typesOnlyA\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Example.OldType", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlRendersDetailsAndEncodesExtractedValues()
    {
        var result = Result(
            new StringSection
            {
                SimilarityPercent = 0,
                UniqueToA = [new StringHit { Value = "<script>alert(1)</script>", Encoding = "ASCII", FirstOffsetA = 42 }]
            },
            new DotNetSection
            {
                Applicable = true,
                SimilarityPercent = 0,
                A = new DotNetProfile { AssemblyName = "Before" },
                B = new DotNetProfile { AssemblyName = "After" },
                PInvokesOnlyB = ["kernel32.dll!CreateFileW"]
            });

        var html = HtmlReportWriter.ToHtml(result);

        Assert.Contains("Strings only in A", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("P/Invoke only in B", html, StringComparison.Ordinal);
        Assert.Contains("CreateFileW", html, StringComparison.Ordinal);
    }

    private static ComparisonResult Result(params IAnalysisSection[] sections) => new()
    {
        FileA = new BinaryImageInfo { Name = "a.bin", Sha256 = new string('A', 64) },
        FileB = new BinaryImageInfo { Name = "b.bin", Sha256 = new string('B', 64) },
        Sections = [.. sections],
        GeneratedAt = DateTimeOffset.UnixEpoch
    };
}
