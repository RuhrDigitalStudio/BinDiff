using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;

namespace BinDiff.Tests;

public class EngineAggregateTests
{
    // Regression: adversarial weights (Infinity / MaxValue / NaN) must not poison the
    // overall score into a non-finite value. See AnalyzerEngine.Aggregate.
    [Fact]
    public void OverallSimilarity_StaysFinite_WithAdversarialWeights()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var a = new BinaryImage("a.bin", data);
        var b = new BinaryImage("b.bin", (byte[])data.Clone());

        var opts = new ComparisonOptions();
        opts.Weights[AnalyzerModule.ByteDiff] = double.PositiveInfinity;
        opts.Weights[AnalyzerModule.FuzzyHash] = double.MaxValue;
        opts.Weights[AnalyzerModule.Entropy] = double.NaN;

        var result = new AnalyzerEngine().Compare(a, b, opts);

        Assert.True(double.IsFinite(result.OverallSimilarityPercent),
            "Overall similarity must remain finite regardless of weights.");
        Assert.InRange(result.OverallSimilarityPercent, 0.0, 100.0);
    }

    [Fact]
    public void DisabledModules_AreSkipped()
    {
        var a = new BinaryImage("a.bin", new byte[] { 1, 2, 3, 4 });
        var b = new BinaryImage("b.bin", new byte[] { 5, 6, 7, 8 });

        var opts = new ComparisonOptions
        {
            EnabledModules = new HashSet<AnalyzerModule> { AnalyzerModule.Entropy }
        };

        var result = new AnalyzerEngine().Compare(a, b, opts);

        Assert.Single(result.Sections);
        Assert.Equal(AnalyzerModule.Entropy, result.Sections[0].Module);
    }
}
