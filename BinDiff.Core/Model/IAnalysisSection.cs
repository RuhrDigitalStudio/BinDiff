using System.Text.Json.Serialization;

namespace BinDiff.Core.Model;

/// <summary>
/// Common contract every analyzer result exposes. Concrete sections carry additional
/// strongly-typed data the GUI renders in dedicated tabs; the generic <see cref="Metrics"/>
/// list is a flat key/value view for simple display and CLI/HTML rendering.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ByteDiffSection), "byteDiff")]
[JsonDerivedType(typeof(FuzzyHashSection), "fuzzyHash")]
[JsonDerivedType(typeof(FormatSection), "format")]
[JsonDerivedType(typeof(EntropySection), "entropy")]
[JsonDerivedType(typeof(PatternSection), "patterns")]
[JsonDerivedType(typeof(StringSection), "strings")]
[JsonDerivedType(typeof(DotNetSection), "dotNet")]
[JsonDerivedType(typeof(ErrorSection), "error")]
public interface IAnalysisSection
{
    AnalyzerModule Module { get; }

    /// <summary>Human-readable title for this section (used as GUI tab header / CLI heading).</summary>
    string Title { get; }

    /// <summary>Similarity 0..100, or null when this module produced no comparable score.</summary>
    double? SimilarityPercent { get; }

    /// <summary>Non-null when the module failed; the run continues and aggregation skips it.</summary>
    string? Error { get; }

    /// <summary>Flat key/value metrics for generic display.</summary>
    IReadOnlyList<KeyValuePair<string, string>> Metrics { get; }
}
