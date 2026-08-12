namespace BinDiff.Core.Model;

/// <summary>
/// Fallback section produced when a module throws unexpectedly. Keeps the run alive and
/// records the failure; aggregation skips it because <see cref="SimilarityPercent"/> is null.
/// </summary>
public sealed class ErrorSection : IAnalysisSection
{
    public AnalyzerModule Module { get; set; }
    public string Title => $"{Module} (Error)";
    public double? SimilarityPercent => null;
    public string? Error { get; set; }

    public IReadOnlyList<KeyValuePair<string, string>> Metrics =>
        new List<KeyValuePair<string, string>> { new("Error", Error ?? "") };

    public ErrorSection() { }

    public ErrorSection(AnalyzerModule module, string error)
    {
        Module = module;
        Error = error;
    }
}
