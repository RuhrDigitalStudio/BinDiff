using System.Globalization;
using System.Text;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Reporting;

try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected/limited console */ }

var parsed = CliArgs.Parse(args);
if (parsed.ShowHelp)
{
    CliArgs.PrintUsage();
    return 0;
}
if (parsed.Error is not null)
{
    Console.Error.WriteLine("Error: " + parsed.Error);
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}

try
{
    var engine = new AnalyzerEngine();
    var result = engine.Compare(parsed.FileA!, parsed.FileB!, parsed.Options);

    Report.PrintText(result);

    if (parsed.JsonPath is not null)
    {
        JsonReportWriter.Write(result, parsed.JsonPath);
        Console.WriteLine($"JSON report written: {parsed.JsonPath}");
    }
    if (parsed.HtmlPath is not null)
    {
        HtmlReportWriter.Write(result, parsed.HtmlPath);
        Console.WriteLine($"HTML report written: {parsed.HtmlPath}");
    }
    return 0;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"Input file not found: {ex.FileName ?? ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Unexpected error: " + ex.Message);
    return 1;
}

/// <summary>Parsed command-line invocation.</summary>
sealed class ParsedArgs
{
    public string? FileA, FileB, JsonPath, HtmlPath, Error;
    public bool ShowHelp;
    public ComparisonOptions Options = new();
}

static class CliArgs
{
    public static ParsedArgs Parse(string[] args)
    {
        var p = new ParsedArgs();
        var positionals = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "--help":
                    p.ShowHelp = true;
                    return p;
                case "--json": p.JsonPath = Next(args, ref i, a, p); break;
                case "--html": p.HtmlPath = Next(args, ref i, a, p); break;
                case "--pattern-len": p.Options.PatternLength = Int(Next(args, ref i, a, p), p, a, 1); break;
                case "--min-occurrences": p.Options.MinPatternOccurrences = Int(Next(args, ref i, a, p), p, a, 1); break;
                case "--block-size": p.Options.ChunkAvgSize = Int(Next(args, ref i, a, p), p, a, 8); break;
                case "--shingle-k": p.Options.ShingleK = Int(Next(args, ref i, a, p), p, a, 1); break;
                case "--entropy-block": p.Options.EntropyBlockSize = Int(Next(args, ref i, a, p), p, a, 1); break;
                case "--string-min": p.Options.MinStringLength = Int(Next(args, ref i, a, p), p, a, 1); break;
                case "--max-strings": p.Options.MaxReportedStrings = Int(Next(args, ref i, a, p), p, a, 0); break;
                case "--modules": ParseModules(Next(args, ref i, a, p), p); break;
                default:
                    if (a.StartsWith('-')) p.Error ??= $"Unknown option: {a}";
                    else positionals.Add(a);
                    break;
            }
            if (p.Error is not null) return p;
        }

        if (positionals.Count != 2)
        {
            p.Error = $"Expected exactly two input files; received {positionals.Count}.";
            return p;
        }
        p.FileA = positionals[0];
        p.FileB = positionals[1];
        return p;
    }

    private static string? Next(string[] args, ref int i, string opt, ParsedArgs p)
    {
        if (i + 1 >= args.Length) { p.Error = $"Option {opt} requires a value."; return null; }
        return args[++i];
    }

    private static int Int(string? s, ParsedArgs p, string opt, int min)
    {
        if (p.Error is not null) return min;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < min)
        {
            p.Error = $"Option {opt} requires an integer >= {min}.";
            return min;
        }
        return v;
    }

    private static void ParseModules(string? csv, ParsedArgs p)
    {
        if (csv is null) return;
        var set = new HashSet<AnalyzerModule>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "bytediff" or "byte" or "diff": set.Add(AnalyzerModule.ByteDiff); break;
                case "fuzzy" or "fuzzyhash" or "hash": set.Add(AnalyzerModule.FuzzyHash); break;
                case "format" or "pe" or "elf": set.Add(AnalyzerModule.Format); break;
                case "entropy": set.Add(AnalyzerModule.Entropy); break;
                case "patterns" or "pattern" or "sig": set.Add(AnalyzerModule.Patterns); break;
                case "strings" or "string" or "text": set.Add(AnalyzerModule.Strings); break;
                case "dotnet" or "managed" or "clr": set.Add(AnalyzerModule.DotNet); break;
                default: p.Error = $"Unknown module: {raw}"; return;
            }
        }
        if (set.Count > 0) p.Options.EnabledModules = set;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
BinDiff — defensive binary similarity analysis tool

Usage:
  bindiff <fileA> <fileB> [options]

Options:
  --json <path>          write a JSON report
  --html <path>          write a self-contained HTML report
  --pattern-len <n>      signature/pattern length in bytes (default 16)
  --min-occurrences <n>  minimum pattern occurrences (default 2)
  --block-size <n>       target CDC chunk size in bytes (default 2048)
  --shingle-k <n>        MinHash k-gram size (default 8)
  --entropy-block <n>    entropy block size in bytes (default 256)
  --string-min <n>       minimum ASCII/UTF-16 string length (default 5)
  --max-strings <n>      maximum displayed strings per category (default 100)
  --modules <a,b,...>    run only: bytediff,fuzzy,format,entropy,patterns,strings,dotnet
  -h, --help             show this help

Example:
  bindiff a.exe b.exe --pattern-len 32 --html report.html
""");
    }
}

static class Report
{
    public static void PrintText(ComparisonResult r)
    {
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine();
        Console.WriteLine("=== BinDiff — similarity analysis ===");
        Console.WriteLine($"A: {r.FileA.Name}  ({r.FileA.Size.ToString("N0", inv)} B)  SHA256 {Short(r.FileA.Sha256)}");
        Console.WriteLine($"B: {r.FileB.Name}  ({r.FileB.Size.ToString("N0", inv)} B)  SHA256 {Short(r.FileB.Sha256)}");
        Console.WriteLine();
        Console.WriteLine($"OVERALL SIMILARITY (weighted): {r.OverallSimilarityPercent.ToString("0.00", inv)} %");
        Console.WriteLine(Bar(r.OverallSimilarityPercent));
        Console.WriteLine();

        foreach (var s in r.Sections)
        {
            string score = s.SimilarityPercent is null ? "n/a" : s.SimilarityPercent.Value.ToString("0.00", inv) + " %";
            Console.WriteLine($"-- {s.Title}  [{score}]");
            if (s.Error is not null)
            {
                Console.WriteLine($"   Error: {s.Error}");
                continue;
            }
            foreach (var m in s.Metrics)
                Console.WriteLine($"   {m.Key,-28} {m.Value}");
            Console.WriteLine();
        }

        if (r.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var w in r.Warnings) Console.WriteLine("  ! " + w);
        }
    }

    private static string Short(string sha) => sha.Length <= 16 ? sha : sha[..16] + "…";

    private static string Bar(double pct)
    {
        int width = 40;
        int fill = (int)Math.Round(Math.Clamp(pct, 0, 100) / 100.0 * width);
        return "[" + new string('#', fill) + new string('.', width - fill) + "]";
    }
}
