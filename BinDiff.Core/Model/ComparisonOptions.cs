namespace BinDiff.Core.Model;

/// <summary>
/// All tunable parameters for a comparison run. Signature/pattern and block sizes are
/// configurable per the tool's requirements. Sensible defaults are provided.
/// </summary>
public sealed class ComparisonOptions
{
    /// <summary>Which modules to execute. Defaults to all.</summary>
    public HashSet<AnalyzerModule> EnabledModules { get; set; } = new()
    {
        AnalyzerModule.ByteDiff,
        AnalyzerModule.FuzzyHash,
        AnalyzerModule.Format,
        AnalyzerModule.Entropy,
        AnalyzerModule.Patterns
    };

    // --- ByteDiff: content-defined chunking parameters ---
    public int ChunkMinSize { get; set; } = 512;
    public int ChunkAvgSize { get; set; } = 2048;   // target average chunk length (power-of-two mask derived from this)
    public int ChunkMaxSize { get; set; } = 8192;

    // --- FuzzyHash ---
    public int ShingleK { get; set; } = 8;           // k-gram (bytes) size for MinHash shingling
    public int MinHashPermutations { get; set; } = 128;

    // --- Patterns: configurable signature / pattern size ---
    public int PatternLength { get; set; } = 16;     // length in bytes of extracted signatures
    public int MinPatternOccurrences { get; set; } = 2;
    public int MaxPatternsPerCategory { get; set; } = 50;

    // --- Entropy ---
    public int EntropyBlockSize { get; set; } = 256;
    public int MaxEntropyProfilePoints { get; set; } = 512; // downsample cap for GUI/report curves
    public double HighEntropyThreshold { get; set; } = 7.0; // bits/byte above which a block counts as "high entropy"

    // --- Aggregation weights (module -> weight). Missing entries default to 1.0. ---
    public Dictionary<AnalyzerModule, double> Weights { get; set; } = new()
    {
        [AnalyzerModule.ByteDiff] = 1.0,
        [AnalyzerModule.FuzzyHash] = 1.0,
        [AnalyzerModule.Format] = 1.0,
        [AnalyzerModule.Entropy] = 0.5,
        [AnalyzerModule.Patterns] = 0.5
    };

    /// <summary>Warn (not fail) when an input exceeds this size, in bytes. 0 disables the warning.</summary>
    public long LargeFileWarningBytes { get; set; } = 256L * 1024 * 1024;

    public double WeightFor(AnalyzerModule module) =>
        Weights.TryGetValue(module, out var w) ? w : 1.0;
}
