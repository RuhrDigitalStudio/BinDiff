using BinDiff.Core.Model;

namespace BinDiff.Core;

/// <summary>
/// One analysis dimension. Implementations are pure with respect to the inputs
/// (they never mutate <see cref="BinaryImage.Data"/>) and must not throw: any failure
/// is reported via <see cref="IAnalysisSection.Error"/> so the engine can continue.
/// </summary>
public interface IAnalyzer
{
    AnalyzerModule Module { get; }

    IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options);
}
