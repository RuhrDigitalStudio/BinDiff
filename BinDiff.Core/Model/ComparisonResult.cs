namespace BinDiff.Core.Model;

/// <summary>Top-level outcome of comparing two binaries. Fully serialisable for reports.</summary>
public sealed class ComparisonResult
{
    public BinaryImageInfo FileA { get; set; } = new();
    public BinaryImageInfo FileB { get; set; } = new();

    /// <summary>Weighted aggregate of the per-module similarity scores, 0..100.</summary>
    public double OverallSimilarityPercent { get; set; }

    public List<IAnalysisSection> Sections { get; set; } = new();

    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Non-fatal warnings raised during the run (e.g. very large input).</summary>
    public List<string> Warnings { get; set; } = new();

    public IAnalysisSection? Section(AnalyzerModule module) =>
        Sections.FirstOrDefault(s => s.Module == module);
}
