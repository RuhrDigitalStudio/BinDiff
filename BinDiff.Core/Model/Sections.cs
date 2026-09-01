using System.Globalization;

namespace BinDiff.Core.Model;

/// <summary>Formatting helpers shared by section metric views (invariant culture).</summary>
internal static class Fmt
{
    public static string Pct(double? v) => v is null ? "n/a" : v.Value.ToString("0.00", CultureInfo.InvariantCulture) + " %";
    public static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Int(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>One contiguous span of a file, flagged as shared with the other file or unique.</summary>
public sealed class ByteSpan
{
    public long Offset { get; set; }
    public int Length { get; set; }
    public bool Shared { get; set; }
}

public sealed class ByteDiffSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.ByteDiff;
    public string Title => "Byte-Diff";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }

    public double JaccardPercent { get; set; }
    public double HistogramCosinePercent { get; set; }
    public int CommonChunks { get; set; }
    public int OnlyAChunks { get; set; }
    public int OnlyBChunks { get; set; }
    public int TotalChunksA { get; set; }
    public int TotalChunksB { get; set; }

    /// <summary>Colour-map spans over file A (Shared vs unique) for the GUI diff view.</summary>
    public List<ByteSpan> MapA { get; set; } = new();
    /// <summary>Colour-map spans over file B.</summary>
    public List<ByteSpan> MapB { get; set; } = new();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("Similarity (Jaccard)", Fmt.Pct(JaccardPercent)),
        new("Byte-histogram cosine", Fmt.Pct(HistogramCosinePercent)),
        new("Shared chunks", Fmt.Int(CommonChunks)),
        new("Only in A", Fmt.Int(OnlyAChunks)),
        new("Only in B", Fmt.Int(OnlyBChunks)),
        new("Chunks A / B", $"{Fmt.Int(TotalChunksA)} / {Fmt.Int(TotalChunksB)}")
    };
}

public sealed class FuzzyHashSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.FuzzyHash;
    public string Title => "Fuzzy-Hash";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }

    public double MinHashSimilarityPercent { get; set; }
    public double CtphSimilarityPercent { get; set; }
    public int ShingleK { get; set; }
    public string CtphDigestA { get; set; } = "";
    public string CtphDigestB { get; set; } = "";

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("MinHash similarity", Fmt.Pct(MinHashSimilarityPercent)),
        new("CTPH similarity", Fmt.Pct(CtphSimilarityPercent)),
        new("Shingle-k", Fmt.Int(ShingleK)),
        new("CTPH-Digest A", CtphDigestA),
        new("CTPH-Digest B", CtphDigestB)
    };
}

public sealed class SectionEntry
{
    public string Name { get; set; } = "";
    public long VirtualSize { get; set; }
    public long RawSize { get; set; }
    public double Entropy { get; set; }
    public string Sha256 { get; set; } = "";
    public string Flags { get; set; } = "";
}

public sealed class FormatSummary
{
    public string Format { get; set; } = "Unknown";
    public string Machine { get; set; } = "";
    public string TimeStamp { get; set; } = "";
    public string Subsystem { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public List<SectionEntry> Sections { get; set; } = new();
    public List<string> Imports { get; set; } = new();
}

public sealed class SectionComparison
{
    public string Name { get; set; } = "";
    public bool InA { get; set; }
    public bool InB { get; set; }
    public double? SimilarityPercent { get; set; }
}

public sealed class FormatSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.Format;
    public string Title => "Format (PE/ELF)";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }

    /// <summary>False when neither file is a recognised PE/ELF; the module is then informational only.</summary>
    public bool Applicable { get; set; }
    public FormatSummary? A { get; set; }
    public FormatSummary? B { get; set; }

    public List<SectionComparison> SectionComparisons { get; set; } = new();
    public List<string> ImportsCommon { get; set; } = new();
    public List<string> ImportsOnlyA { get; set; } = new();
    public List<string> ImportsOnlyB { get; set; } = new();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics
    {
        get
        {
            var list = new List<KeyValuePair<string, string>>
            {
                new("Format A / B", $"{A?.Format ?? "-"} / {B?.Format ?? "-"}"),
                new("Structural similarity", Fmt.Pct(SimilarityPercent)),
            };
            if (Applicable)
            {
                list.Add(new("Sections A / B", $"{A?.Sections.Count ?? 0} / {B?.Sections.Count ?? 0}"));
                list.Add(new("Shared imports", Fmt.Int(ImportsCommon.Count)));
                list.Add(new("Imports only in A / B", $"{ImportsOnlyA.Count} / {ImportsOnlyB.Count}"));
            }
            return list;
        }
    }
}

public sealed class EntropySection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.Entropy;
    public string Title => "Entropy";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }

    public int BlockSize { get; set; }
    public double MeanEntropyA { get; set; }
    public double MeanEntropyB { get; set; }
    public double MaxEntropyA { get; set; }
    public double MaxEntropyB { get; set; }
    public double HighEntropyFractionA { get; set; }
    public double HighEntropyFractionB { get; set; }

    /// <summary>Down-sampled entropy curve (bits/byte, 0..8) for the GUI graph.</summary>
    public double[] ProfileA { get; set; } = Array.Empty<double>();
    public double[] ProfileB { get; set; } = Array.Empty<double>();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("Profile similarity", Fmt.Pct(SimilarityPercent)),
        new("Block size", Fmt.Int(BlockSize)),
        new("Mean entropy A / B", $"{Fmt.Num(MeanEntropyA)} / {Fmt.Num(MeanEntropyB)} bit"),
        new("Maximum entropy A / B", $"{Fmt.Num(MaxEntropyA)} / {Fmt.Num(MaxEntropyB)} bit"),
        new("High-entropy share A / B",
            $"{Fmt.Num(HighEntropyFractionA * 100)} % / {Fmt.Num(HighEntropyFractionB * 100)} %")
    };
}

public sealed class PatternHit
{
    public string Hex { get; set; } = "";
    public int CountA { get; set; }
    public int CountB { get; set; }
    public long FirstOffsetA { get; set; } = -1;
    public long FirstOffsetB { get; set; } = -1;
}

public sealed class PatternSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.Patterns;
    public string Title => "Patterns / signatures";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }

    public int PatternLength { get; set; }

    /// <summary>Frequent byte sequences present in BOTH files.</summary>
    public List<PatternHit> CommonPatterns { get; set; } = new();
    /// <summary>Sequences occurring only in A — candidate static indicators for defensive documentation.</summary>
    public List<PatternHit> UniqueToA { get; set; } = new();
    /// <summary>Sequences occurring only in B.</summary>
    public List<PatternHit> UniqueToB { get; set; } = new();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("Signature length (bytes)", Fmt.Int(PatternLength)),
        new("Pattern overlap", Fmt.Pct(SimilarityPercent)),
        new("Shared patterns", Fmt.Int(CommonPatterns.Count)),
        new("Only in A (indicators)", Fmt.Int(UniqueToA.Count)),
        new("Only in B (indicators)", Fmt.Int(UniqueToB.Count))
    };
}

public sealed class StringHit
{
    public string Value { get; set; } = "";
    public string Encoding { get; set; } = "";
    public long FirstOffsetA { get; set; } = -1;
    public long FirstOffsetB { get; set; } = -1;
    public int CountA { get; set; }
    public int CountB { get; set; }
}

public sealed class StringSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.Strings;
    public string Title => "Extracted strings";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }
    public int MinimumLength { get; set; }
    public int DistinctStringsA { get; set; }
    public int DistinctStringsB { get; set; }
    public bool TruncatedA { get; set; }
    public bool TruncatedB { get; set; }
    public List<StringHit> CommonStrings { get; set; } = new();
    public List<StringHit> UniqueToA { get; set; } = new();
    public List<StringHit> UniqueToB { get; set; } = new();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("String-set similarity", Fmt.Pct(SimilarityPercent)),
        new("Minimum length", Fmt.Int(MinimumLength)),
        new("Distinct strings A / B", $"{Fmt.Int(DistinctStringsA)} / {Fmt.Int(DistinctStringsB)}"),
        new("Extraction truncated A / B", $"{TruncatedA} / {TruncatedB}"),
        new("Reported shared strings", Fmt.Int(CommonStrings.Count)),
        new("Reported only in A / B", $"{Fmt.Int(UniqueToA.Count)} / {Fmt.Int(UniqueToB.Count)}")
    };
}

public sealed class DotNetProfile
{
    public string AssemblyName { get; set; } = "";
    public string AssemblyVersion { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public bool Truncated { get; set; }
    public List<string> AssemblyReferences { get; set; } = new();
    public List<string> Types { get; set; } = new();
    public List<string> Methods { get; set; } = new();
    public List<string> PInvokes { get; set; } = new();
}

public sealed class DotNetSection : IAnalysisSection
{
    public AnalyzerModule Module => AnalyzerModule.DotNet;
    public string Title => "Managed .NET metadata";
    public double? SimilarityPercent { get; set; }
    public string? Error { get; set; }
    public bool Applicable { get; set; }
    public DotNetProfile? A { get; set; }
    public DotNetProfile? B { get; set; }
    public List<string> ReferencesCommon { get; set; } = new();
    public List<string> ReferencesOnlyA { get; set; } = new();
    public List<string> ReferencesOnlyB { get; set; } = new();
    public List<string> TypesCommon { get; set; } = new();
    public List<string> TypesOnlyA { get; set; } = new();
    public List<string> TypesOnlyB { get; set; } = new();
    public List<string> MethodsCommon { get; set; } = new();
    public List<string> MethodsOnlyA { get; set; } = new();
    public List<string> MethodsOnlyB { get; set; } = new();
    public List<string> PInvokesCommon { get; set; } = new();
    public List<string> PInvokesOnlyA { get; set; } = new();
    public List<string> PInvokesOnlyB { get; set; } = new();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics => new List<KeyValuePair<string, string>>
    {
        new("Managed metadata similarity", Fmt.Pct(SimilarityPercent)),
        new("Assembly A / B", $"{A?.AssemblyName ?? "native or unknown"} / {B?.AssemblyName ?? "native or unknown"}"),
        new("Version A / B", $"{A?.AssemblyVersion ?? "-"} / {B?.AssemblyVersion ?? "-"}"),
        new("Target framework A / B", $"{A?.TargetFramework ?? "-"} / {B?.TargetFramework ?? "-"}"),
        new("Shared references / types / methods", $"{ReferencesCommon.Count} / {TypesCommon.Count} / {MethodsCommon.Count}"),
        new("P/Invoke only in A / B", $"{PInvokesOnlyA.Count} / {PInvokesOnlyB.Count}"),
        new("Metadata truncated A / B", $"{A?.Truncated ?? false} / {B?.Truncated ?? false}")
    };
}
