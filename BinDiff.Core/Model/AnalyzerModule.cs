namespace BinDiff.Core.Model;

/// <summary>
/// The distinct analysis dimensions the engine can run. Each maps to exactly one
/// <see cref="IAnalyzer"/> implementation and one <see cref="IAnalysisSection"/> in the result.
/// </summary>
public enum AnalyzerModule
{
    ByteDiff,
    FuzzyHash,
    Format,
    Entropy,
    Patterns
}
