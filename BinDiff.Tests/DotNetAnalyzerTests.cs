using BinDiff.Core.Analyzers;
using BinDiff.Core.Model;
using BinDiff.Core;
using Xunit;

namespace BinDiff.Tests;

public sealed class DotNetAnalyzerTests
{
    [Fact]
    public void IdenticalManagedAssemblyHasCompleteMetadataMatch()
    {
        var image = BinaryImage.Load(typeof(DotNetAnalyzerTests).Assembly.Location);

        var result = Analyze(image, image);

        Assert.True(result.Applicable);
        Assert.NotNull(result.A);
        Assert.Equal(result.A!.AssemblyName, result.B!.AssemblyName);
        Assert.NotEmpty(result.A.TargetFramework);
        Assert.NotEmpty(result.TypesCommon);
        Assert.NotEmpty(result.MethodsCommon);
        Assert.Equal(100, result.SimilarityPercent);
    }

    [Fact]
    public void DifferentManagedAssembliesExposeSpecificTypesAndReferences()
    {
        var tests = BinaryImage.Load(typeof(DotNetAnalyzerTests).Assembly.Location);
        var core = BinaryImage.Load(typeof(AnalyzerEngine).Assembly.Location);

        var result = Analyze(tests, core);

        Assert.True(result.Applicable);
        Assert.NotNull(result.A);
        Assert.NotNull(result.B);
        Assert.NotEqual(result.A!.AssemblyName, result.B!.AssemblyName);
        Assert.NotEmpty(result.TypesOnlyA);
        Assert.NotEmpty(result.TypesOnlyB);
        Assert.InRange(result.SimilarityPercent!.Value, 0, 99.99);
    }

    [Fact]
    public void NativeInputsAreInformationalAndDoNotContributeAScore()
    {
        var result = Analyze(new BinaryImage("a.bin", [1, 2, 3]), new BinaryImage("b.bin", [4, 5, 6]));

        Assert.False(result.Applicable);
        Assert.Null(result.A);
        Assert.Null(result.B);
        Assert.Null(result.SimilarityPercent);
    }

    [Fact]
    public void MetadataCollectionsAreBoundedAndReportTruncation()
    {
        var image = BinaryImage.Load(typeof(DotNetAnalyzerTests).Assembly.Location);
        var options = new ComparisonOptions { MaxMetadataItems = 1, MaxReportedMetadataItems = 1 };

        var result = (DotNetSection)new DotNetAnalyzer().Analyze(image, image, options);

        Assert.True(result.A!.Truncated);
        Assert.True(result.A.Types.Count <= 1);
        Assert.True(result.A.Methods.Count <= 1);
        Assert.True(result.TypesCommon.Count <= 1);
    }

    private static DotNetSection Analyze(BinaryImage a, BinaryImage b) =>
        (DotNetSection)new DotNetAnalyzer().Analyze(a, b, new ComparisonOptions());
}
