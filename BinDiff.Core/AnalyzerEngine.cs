using System.Globalization;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Model;

namespace BinDiff.Core;

/// <summary>
/// Orchestrates the configured analyzers over two binaries and aggregates their
/// per-module similarity scores into a single weighted overall percentage.
/// This is the only entry point both the CLI and GUI use.
/// </summary>
public sealed class AnalyzerEngine
{
    private readonly IReadOnlyList<IAnalyzer> _analyzers;

    public AnalyzerEngine() : this(DefaultAnalyzers()) { }

    public AnalyzerEngine(IEnumerable<IAnalyzer> analyzers)
    {
        _analyzers = analyzers.ToList();
    }

    /// <summary>The built-in analyzer set, one per <see cref="AnalyzerModule"/>.</summary>
    public static IReadOnlyList<IAnalyzer> DefaultAnalyzers() => new IAnalyzer[]
    {
        new ByteDiffAnalyzer(),
        new FuzzyHashAnalyzer(),
        new FormatAnalyzer(),
        new EntropyAnalyzer(),
        new PatternAnalyzer()
    };

    /// <summary>Loads both files from disk (read-only) and compares them.</summary>
    public ComparisonResult Compare(string pathA, string pathB, ComparisonOptions? options = null)
    {
        var a = BinaryImage.Load(pathA);
        var b = BinaryImage.Load(pathB);
        return Compare(a, b, options);
    }

    /// <summary>Compares two already-loaded images.</summary>
    public ComparisonResult Compare(BinaryImage a, BinaryImage b, ComparisonOptions? options = null)
    {
        options ??= new ComparisonOptions();

        var result = new ComparisonResult
        {
            FileA = a.ToInfo(),
            FileB = b.ToInfo(),
            GeneratedAt = DateTimeOffset.Now
        };

        if (options.LargeFileWarningBytes > 0)
        {
            if (a.Size > options.LargeFileWarningBytes)
                result.Warnings.Add($"File A is large ({a.Size.ToString("N0", CultureInfo.InvariantCulture)} bytes); analysis may take time.");
            if (b.Size > options.LargeFileWarningBytes)
                result.Warnings.Add($"File B is large ({b.Size.ToString("N0", CultureInfo.InvariantCulture)} bytes); analysis may take time.");
        }

        foreach (var analyzer in _analyzers)
        {
            if (!options.EnabledModules.Contains(analyzer.Module))
                continue;

            IAnalysisSection section;
            try
            {
                section = analyzer.Analyze(a, b, options)
                          ?? new ErrorSection(analyzer.Module, "Analyzer returned null.");
            }
            catch (Exception ex)
            {
                section = new ErrorSection(analyzer.Module, ex.Message);
            }
            result.Sections.Add(section);
        }

        result.OverallSimilarityPercent = Aggregate(result.Sections, options);
        return result;
    }

    /// <summary>Weighted mean of the modules that produced a comparable score.</summary>
    private static double Aggregate(IEnumerable<IAnalysisSection> sections, ComparisonOptions options)
    {
        double weightedSum = 0, weightTotal = 0;
        foreach (var s in sections)
        {
            if (s.Error != null || s.SimilarityPercent is null)
                continue;
            double w = options.WeightFor(s.Module);
            // Reject non-positive, NaN and non-finite (Infinity / overflow-prone) weights so an
            // adversarial ComparisonOptions.Weights value cannot poison the aggregate into NaN/Infinity.
            if (!(w > 0) || !double.IsFinite(w)) continue;
            weightedSum += w * s.SimilarityPercent.Value;
            weightTotal += w;
        }
        double result = weightTotal > 0 ? weightedSum / weightTotal : 0.0;
        // Final safety net: guarantee a finite, clamped percentage regardless of input weights.
        return double.IsFinite(result) ? Math.Clamp(result, 0.0, 100.0) : 0.0;
    }
}
