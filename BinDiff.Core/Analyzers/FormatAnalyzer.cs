using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BinDiff.Core.Model;
using BinDiff.Core.Util;

namespace BinDiff.Core.Analyzers;

/// <summary>
/// Self-contained, defensive PE and ELF parser used for structural similarity.
/// No NuGet, no reflection metadata APIs: every read is bounds-checked and any
/// malformed structure degrades the file to an "Unknown" summary rather than throwing.
/// The headline score blends section-name and import-name Jaccard overlap; per-section
/// comparison uses SHA equality first, then a 256-bin byte-histogram cosine of the raw bytes.
/// </summary>
public sealed class FormatAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.Format;

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        var section = new FormatSection();
        try
        {
            var parsedA = ParseImage(a?.Data ?? Array.Empty<byte>());
            var parsedB = ParseImage(b?.Data ?? Array.Empty<byte>());
            section.A = parsedA.Summary;
            section.B = parsedB.Summary;

            bool recognizedA = IsRecognized(parsedA.Summary.Format);
            bool recognizedB = IsRecognized(parsedB.Summary.Format);
            section.Applicable = recognizedA || recognizedB;

            // Section-level comparison over the union of section names (case-sensitive).
            section.SectionComparisons = CompareSections(parsedA, parsedB);

            // Import set comparison (names already upper-cased at parse time for stability).
            var setA = new HashSet<string>(parsedA.Summary.Imports, StringComparer.Ordinal);
            var setB = new HashSet<string>(parsedB.Summary.Imports, StringComparer.Ordinal);
            var common = new List<string>();
            var onlyA = new List<string>();
            var onlyB = new List<string>();
            foreach (var name in setA)
                (setB.Contains(name) ? common : onlyA).Add(name);
            foreach (var name in setB)
                if (!setA.Contains(name)) onlyB.Add(name);
            common.Sort(StringComparer.Ordinal);
            onlyA.Sort(StringComparer.Ordinal);
            onlyB.Sort(StringComparer.Ordinal);
            section.ImportsCommon = common;
            section.ImportsOnlyA = onlyA;
            section.ImportsOnlyB = onlyB;

            if (!section.Applicable)
            {
                // Neither file is a recognised executable: informational only, no score.
                section.SimilarityPercent = null;
                return section;
            }

            // Headline: average of section-name Jaccard and (when either has imports) import Jaccard.
            var secNamesA = new HashSet<string>(parsedA.Summary.Sections.Select(s => s.Name), StringComparer.Ordinal);
            var secNamesB = new HashSet<string>(parsedB.Summary.Sections.Select(s => s.Name), StringComparer.Ordinal);
            double sectionScore = Jaccard(secNamesA, secNamesB) * 100.0;

            double sum = sectionScore;
            int parts = 1;
            if (setA.Count > 0 || setB.Count > 0)
            {
                sum += Jaccard(setA, setB) * 100.0;
                parts++;
            }
            double headline = sum / parts;
            section.SimilarityPercent = Clamp(SafeFinite(headline, 0.0), 0.0, 100.0);
            return section;
        }
        catch (Exception ex)
        {
            return new FormatSection { Error = ex.Message };
        }
    }

    private static bool IsRecognized(string format) => format == "PE" || format == "ELF";

    // -----------------------------------------------------------------------
    // Internal parse model: FormatSummary for the GUI + a 256-bin byte histogram
    // per section (keyed by name) so section comparison can compute a true cosine
    // without keeping every raw section slice alive.
    // -----------------------------------------------------------------------

    private sealed class ParsedImage
    {
        public FormatSummary Summary = new();
        // First occurrence of each section name -> its 256-bin byte histogram.
        public Dictionary<string, long[]> Histograms = new(StringComparer.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Comparison helpers
    // -----------------------------------------------------------------------

    private static List<SectionComparison> CompareSections(ParsedImage a, ParsedImage b)
    {
        var result = new List<SectionComparison>();

        var mapA = new Dictionary<string, SectionEntry>(StringComparer.Ordinal);
        foreach (var s in a.Summary.Sections) mapA.TryAdd(s.Name, s);
        var mapB = new Dictionary<string, SectionEntry>(StringComparer.Ordinal);
        foreach (var s in b.Summary.Sections) mapB.TryAdd(s.Name, s);

        // Deterministic union ordering: A's order first, then B-only names sorted.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var orderedNames = new List<string>();
        foreach (var s in a.Summary.Sections)
            if (seen.Add(s.Name)) orderedNames.Add(s.Name);
        var bOnly = new List<string>();
        foreach (var s in b.Summary.Sections)
            if (!mapA.ContainsKey(s.Name) && seen.Add(s.Name)) bOnly.Add(s.Name);
        bOnly.Sort(StringComparer.Ordinal);
        orderedNames.AddRange(bOnly);

        foreach (var name in orderedNames)
        {
            bool inA = mapA.TryGetValue(name, out var ea);
            bool inB = mapB.TryGetValue(name, out var eb);
            double? sim = null;
            if (inA && inB)
            {
                if (!string.IsNullOrEmpty(ea!.Sha256) && ea.Sha256 == eb!.Sha256)
                {
                    sim = 100.0;
                }
                else
                {
                    a.Histograms.TryGetValue(name, out var hA);
                    b.Histograms.TryGetValue(name, out var hB);
                    double cos = HistogramCosine(hA, hB);
                    sim = Clamp(SafeFinite(cos * 100.0, 0.0), 0.0, 100.0);
                }
            }
            result.Add(new SectionComparison { Name = name, InA = inA, InB = inB, SimilarityPercent = sim });
        }
        return result;
    }

    /// <summary>Cosine similarity of two 256-bin byte histograms, range 0..1.</summary>
    private static double HistogramCosine(long[]? a, long[]? b)
    {
        if (a is null || b is null || a.Length != 256 || b.Length != 256) return 0.0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < 256; i++)
        {
            double x = a[i], y = b[i];
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        if (na <= 0 || nb <= 0) return 0.0; // one side is empty -> undefined -> 0 overlap
        double denom = Math.Sqrt(na) * Math.Sqrt(nb);
        if (denom <= 0) return 0.0;
        double v = dot / denom;
        return SafeFinite(v < 0 ? 0 : (v > 1 ? 1 : v), 0.0);
    }

    private static long[] Histogram(ReadOnlySpan<byte> data)
    {
        var h = new long[256];
        foreach (var bb in data) h[bb]++;
        return h;
    }

    private static double Jaccard<T>(HashSet<T> a, HashSet<T> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0; // both empty -> treated as identical
        int inter = 0;
        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        foreach (var x in small) if (large.Contains(x)) inter++;
        int union = a.Count + b.Count - inter;
        return union == 0 ? 1.0 : (double)inter / union;
    }

    // -----------------------------------------------------------------------
    // Top-level format dispatch
    // -----------------------------------------------------------------------

    private static ParsedImage ParseImage(byte[] data)
    {
        // PE: "MZ" + valid e_lfanew -> "PE\0\0".
        if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A)
        {
            if (TryParsePe(data, out var pe)) return pe;
        }
        // ELF: 0x7F 'E' 'L' 'F'.
        if (data.Length >= 4 && data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46)
        {
            if (TryParseElf(data, out var elf)) return elf;
        }
        return new ParsedImage { Summary = new FormatSummary { Format = "Unknown" } };
    }

    // -----------------------------------------------------------------------
    // PE parser
    // -----------------------------------------------------------------------

    private static bool TryParsePe(byte[] data, out ParsedImage parsed)
    {
        parsed = new ParsedImage { Summary = new FormatSummary { Format = "Unknown" } };
        try
        {
            var summary = parsed.Summary;

            // e_lfanew (u32 LE @ 0x3C)
            if (!TryU32(data, 0x3C, out uint peOff)) return false;
            long peL = peOff;
            // Need "PE\0\0" + 20-byte COFF header.
            if (peL < 0 || peL + 24 > data.Length) return false;
            if (data[peL] != (byte)'P' || data[peL + 1] != (byte)'E' || data[peL + 2] != 0 || data[peL + 3] != 0)
                return false;

            int coff = (int)peL + 4;
            if (!TryU16(data, coff + 0, out ushort machine)) return false;
            if (!TryU16(data, coff + 2, out ushort numSections)) return false;
            TryU32(data, coff + 4, out uint timeDateStamp);
            if (!TryU16(data, coff + 16, out ushort sizeOfOptional)) return false;
            // coff + 18 = Characteristics (not surfaced in the summary).

            int optOff = (int)peL + 24;
            bool havePe32Plus = false;
            uint entryRva = 0;
            ushort subsystem = 0;
            uint importDirRva = 0;

            if (sizeOfOptional > 0 && optOff + 2 <= data.Length)
            {
                TryU16(data, optOff, out ushort magic);
                havePe32Plus = magic == 0x20B;
                TryU32(data, optOff + 16, out entryRva);
                TryU16(data, optOff + 68, out subsystem);

                // Data directories: PE32 -> optOff+96, PE32+ -> optOff+112. Import dir = index 1.
                int dirBase = optOff + (havePe32Plus ? 112 : 96);
                int importDirOff = dirBase + 8; // (RVA u32, Size u32) per dir; index 1 RVA.
                TryU32(data, importDirOff, out importDirRva);
            }

            summary.Format = "PE";
            summary.Machine = MachineNamePe(machine);
            summary.Subsystem = SubsystemName(subsystem);
            summary.EntryPoint = "0x" + entryRva.ToString("X", CultureInfo.InvariantCulture);
            summary.TimeStamp = FormatTimestamp(timeDateStamp);

            // Section table starts at optOff + SizeOfOptionalHeader; 40 bytes/entry.
            int sectOff = optOff + sizeOfOptional;
            int count = numSections;
            if (count < 0) count = 0;
            long maxByBuffer = ((long)data.Length - sectOff) / 40;
            if (maxByBuffer < 0) maxByBuffer = 0;
            if (count > maxByBuffer) count = (int)maxByBuffer;
            if (count > 4096) count = 4096; // sanity ceiling

            var sections = new List<SectionEntry>(count);
            var secTable = new List<PeSection>(count);
            for (int i = 0; i < count; i++)
            {
                int e = sectOff + i * 40;
                if (e + 40 > data.Length) break;

                string name = ReadFixedAscii(data, e, 8);
                TryU32(data, e + 8, out uint virtualSize);
                TryU32(data, e + 12, out uint virtualAddress);
                TryU32(data, e + 16, out uint rawSize);
                TryU32(data, e + 20, out uint ptrRaw);
                TryU32(data, e + 36, out uint characteristics);

                var (slice, sliceLen) = SafeSlice(data, ptrRaw, rawSize);
                double entropy = 0.0;
                string sha = "";
                long[]? hist = null;
                if (sliceLen > 0)
                {
                    var span = slice.Span;
                    entropy = SafeFinite(EntropyUtil.ShannonEntropy(span), 0.0);
                    sha = Convert.ToHexString(SHA256.HashData(span));
                    hist = Histogram(span);
                }

                var entry = new SectionEntry
                {
                    Name = name,
                    VirtualSize = virtualSize,
                    RawSize = rawSize,
                    Entropy = entropy,
                    Sha256 = sha,
                    Flags = PeSectionFlags(characteristics)
                };
                sections.Add(entry);
                if (hist != null) parsed.Histograms.TryAdd(name, hist);
                secTable.Add(new PeSection(virtualAddress, virtualSize, ptrRaw, rawSize));
            }
            summary.Sections = sections;

            // Imports: map import-directory RVA to a file offset, walk descriptors.
            if (importDirRva != 0)
                summary.Imports = ParsePeImports(data, importDirRva, secTable);

            return true;
        }
        catch
        {
            parsed = new ParsedImage { Summary = new FormatSummary { Format = "Unknown" } };
            return false;
        }
    }

    private readonly record struct PeSection(uint VirtualAddress, uint VirtualSize, uint PointerToRawData, uint SizeOfRawData);

    private static long RvaToOffset(uint rva, List<PeSection> sections)
    {
        foreach (var s in sections)
        {
            uint vsize = s.VirtualSize != 0 ? s.VirtualSize : s.SizeOfRawData;
            long lo = s.VirtualAddress;
            long hi = lo + Math.Max(vsize, s.SizeOfRawData);
            if (rva >= lo && rva < hi)
                return (long)rva - s.VirtualAddress + s.PointerToRawData;
        }
        return -1;
    }

    private static List<string> ParsePeImports(byte[] data, uint importDirRva, List<PeSection> sections)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            long descOff = RvaToOffset(importDirRva, sections);
            if (descOff < 0) return result;

            // IMAGE_IMPORT_DESCRIPTOR = 20 bytes; stop at all-zero descriptor.
            const int maxDescriptors = 4096;
            for (int i = 0; i < maxDescriptors; i++)
            {
                long e = descOff + (long)i * 20;
                if (e < 0 || e + 20 > data.Length) break;

                bool allZero = true;
                for (int k = 0; k < 20; k++)
                {
                    if (data[e + k] != 0) { allZero = false; break; }
                }
                if (allZero) break;

                if (!TryU32(data, (int)e + 12, out uint nameRva)) break; // DLL Name RVA @ +12
                if (nameRva == 0) continue;
                long nameOff = RvaToOffset(nameRva, sections);
                if (nameOff < 0 || nameOff >= data.Length) continue;

                string dll = ReadAsciiZ(data, (int)nameOff, 256);
                if (dll.Length == 0) continue;
                string up = dll.ToUpperInvariant();
                if (seen.Add(up)) result.Add(up);
            }
        }
        catch
        {
            // Best-effort: return whatever was gathered before the fault.
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // ELF parser
    // -----------------------------------------------------------------------

    private static bool TryParseElf(byte[] data, out ParsedImage parsed)
    {
        parsed = new ParsedImage { Summary = new FormatSummary { Format = "Unknown" } };
        try
        {
            if (data.Length < 24) return false;
            byte eiClass = data[4]; // 1=32, 2=64
            byte eiData = data[5];  // 1=LE, 2=BE
            bool is64 = eiClass == 2;
            bool le = eiData != 2;  // default LE unless explicitly BE

            if (eiClass != 1 && eiClass != 2) return false;

            var summary = parsed.Summary;

            ushort eType = U16(data, 16, le);
            ushort eMachine = U16(data, 18, le);

            long eShoff;
            ushort eShentsize, eShnum, eShstrndx;
            if (is64)
            {
                if (data.Length < 0x40) return false;
                eShoff = (long)U64(data, 0x28, le);
                eShentsize = U16(data, 0x3A, le);
                eShnum = U16(data, 0x3C, le);
                eShstrndx = U16(data, 0x3E, le);
            }
            else
            {
                if (data.Length < 0x34) return false;
                eShoff = U32(data, 0x20, le);
                eShentsize = U16(data, 0x2E, le);
                eShnum = U16(data, 0x30, le);
                eShstrndx = U16(data, 0x32, le);
            }

            summary.Format = "ELF";
            summary.Machine = MachineNameElf(eMachine);
            summary.Subsystem = ElfTypeName(eType);
            ulong entry = is64 ? U64(data, 0x18, le) : U32(data, 0x18, le);
            summary.EntryPoint = "0x" + entry.ToString("X", CultureInfo.InvariantCulture);
            summary.TimeStamp = ""; // ELF has no header timestamp.

            if (eShoff <= 0 || eShnum == 0 || eShentsize == 0) return true; // valid header, no sections

            // Guard section-header table bounds; truncate to what fits.
            long tableEnd = eShoff + (long)eShnum * eShentsize;
            if (eShoff >= data.Length || tableEnd > data.Length)
            {
                if (eShoff >= data.Length) return true;
                long fit = ((long)data.Length - eShoff) / eShentsize;
                if (fit <= 0) return true;
                eShnum = (ushort)Math.Min(eShnum, (int)Math.Min(fit, ushort.MaxValue));
            }

            int shnum = eShnum;

            // First pass: read raw header fields.
            var raw = new List<ElfSh>(shnum);
            for (int i = 0; i < shnum; i++)
            {
                long h = eShoff + (long)i * eShentsize;
                if (h < 0 || h + eShentsize > data.Length) break;
                int hi = (int)h;
                uint shName = U32(data, hi + 0, le);
                uint shType = U32(data, hi + 4, le);
                long shOffset, shSize;
                if (is64)
                {
                    shOffset = (long)U64(data, hi + 0x18, le); // sh_offset @ 0x18
                    shSize = (long)U64(data, hi + 0x20, le);   // sh_size @ 0x20
                }
                else
                {
                    shOffset = U32(data, hi + 0x10, le); // sh_offset @ 0x10
                    shSize = U32(data, hi + 0x14, le);   // sh_size @ 0x14
                }
                raw.Add(new ElfSh(shName, shType, shOffset, shSize));
            }

            // Resolve string table (section names) via e_shstrndx.
            long strOff = -1, strSize = 0;
            if (eShstrndx < raw.Count)
            {
                strOff = raw[eShstrndx].Offset;
                strSize = raw[eShstrndx].Size;
            }

            var sections = new List<SectionEntry>(raw.Count);
            foreach (var sh in raw)
            {
                string name = ResolveElfName(data, strOff, strSize, sh.NameOff);

                double entropy = 0.0;
                string sha = "";
                long[]? hist = null;
                // NOBITS (sh_type == 8) occupies no file space.
                if (sh.Type != 8 && sh.Size > 0)
                {
                    var (slice, sliceLen) = SafeSlice(data, sh.Offset, sh.Size);
                    if (sliceLen > 0)
                    {
                        var span = slice.Span;
                        entropy = SafeFinite(EntropyUtil.ShannonEntropy(span), 0.0);
                        sha = Convert.ToHexString(SHA256.HashData(span));
                        hist = Histogram(span);
                    }
                }

                var sectionEntry = new SectionEntry
                {
                    Name = name,
                    VirtualSize = sh.Size,
                    RawSize = sh.Type == 8 ? 0 : sh.Size,
                    Entropy = entropy,
                    Sha256 = sha,
                    Flags = ElfSectionType(sh.Type)
                };
                sections.Add(sectionEntry);
                if (hist != null) parsed.Histograms.TryAdd(name, hist);
            }
            summary.Sections = sections;
            // ELF import (dynamic symbol) parsing is out of scope; leave Imports empty per spec.
            return true;
        }
        catch
        {
            parsed = new ParsedImage { Summary = new FormatSummary { Format = "Unknown" } };
            return false;
        }
    }

    private readonly record struct ElfSh(uint NameOff, uint Type, long Offset, long Size);

    private static string ResolveElfName(byte[] data, long strOff, long strSize, uint nameOff)
    {
        if (strOff < 0 || strSize <= 0) return "";
        long pos = strOff + nameOff;
        if (nameOff >= strSize || pos < 0 || pos >= data.Length) return "";
        long limit = Math.Min(strOff + strSize, data.Length);
        return ReadAsciiZUpTo(data, pos, limit);
    }

    // -----------------------------------------------------------------------
    // Safe primitive readers (all bounds-checked)
    // -----------------------------------------------------------------------

    private static bool TryU16(byte[] d, int off, out ushort v)
    {
        if (off < 0 || off + 2 > d.Length) { v = 0; return false; }
        v = (ushort)(d[off] | (d[off + 1] << 8));
        return true;
    }

    private static bool TryU32(byte[] d, int off, out uint v)
    {
        if (off < 0 || off + 4 > d.Length) { v = 0; return false; }
        v = (uint)(d[off] | (d[off + 1] << 8) | (d[off + 2] << 16) | (d[off + 3] << 24));
        return true;
    }

    private static ushort U16(byte[] d, int off, bool le)
    {
        if (off < 0 || off + 2 > d.Length) return 0;
        return le
            ? (ushort)(d[off] | (d[off + 1] << 8))
            : (ushort)((d[off] << 8) | d[off + 1]);
    }

    private static uint U32(byte[] d, int off, bool le)
    {
        if (off < 0 || off + 4 > d.Length) return 0;
        return le
            ? (uint)(d[off] | (d[off + 1] << 8) | (d[off + 2] << 16) | (d[off + 3] << 24))
            : (uint)((d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3]);
    }

    private static ulong U64(byte[] d, int off, bool le)
    {
        if (off < 0 || off + 8 > d.Length) return 0;
        ulong v = 0;
        if (le)
            for (int i = 7; i >= 0; i--) v = (v << 8) | d[off + i];
        else
            for (int i = 0; i < 8; i++) v = (v << 8) | d[off + i];
        return v;
    }

    /// <summary>Reads up to <paramref name="len"/> bytes as ASCII, stopping at the first NUL.</summary>
    private static string ReadFixedAscii(byte[] d, int off, int len)
    {
        if (off < 0 || off >= d.Length) return "";
        int end = Math.Min(off + len, d.Length);
        var sb = new StringBuilder(end - off);
        for (int i = off; i < end; i++)
        {
            byte c = d[i];
            if (c == 0) break;
            sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
        }
        return sb.ToString();
    }

    /// <summary>Reads a NUL-terminated ASCII string up to <paramref name="maxLen"/> bytes.</summary>
    private static string ReadAsciiZ(byte[] d, int off, int maxLen)
    {
        if (off < 0 || off >= d.Length) return "";
        long limit = Math.Min((long)off + maxLen, d.Length);
        return ReadAsciiZUpTo(d, off, limit);
    }

    private static string ReadAsciiZUpTo(byte[] d, long off, long limit)
    {
        if (off < 0 || off >= d.Length) return "";
        if (limit > d.Length) limit = d.Length;
        var sb = new StringBuilder();
        for (long i = off; i < limit; i++)
        {
            byte c = d[i];
            if (c == 0) break;
            sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
        }
        return sb.ToString();
    }

    /// <summary>Bounds-safe slice starting at <paramref name="ptr"/> for <paramref name="len"/> bytes.</summary>
    private static (ReadOnlyMemory<byte> mem, int len) SafeSlice(byte[] d, long ptr, long len)
    {
        if (ptr < 0 || ptr >= d.Length || len <= 0) return (ReadOnlyMemory<byte>.Empty, 0);
        long avail = d.Length - ptr;
        int take = (int)Math.Min(len, avail);
        if (take <= 0) return (ReadOnlyMemory<byte>.Empty, 0);
        return (new ReadOnlyMemory<byte>(d, (int)ptr, take), take);
    }

    // -----------------------------------------------------------------------
    // Friendly-name maps
    // -----------------------------------------------------------------------

    private static string MachineNamePe(ushort m) => m switch
    {
        0x8664 => "x64",
        0x14C => "x86",
        0xAA64 => "ARM64",
        0x1C0 => "ARM",
        0x200 => "IA64",
        0 => "Unknown",
        _ => "0x" + m.ToString("X", CultureInfo.InvariantCulture)
    };

    private static string SubsystemName(ushort s) => s switch
    {
        1 => "Native",
        2 => "GUI",
        3 => "Console",
        9 => "WinCE-GUI",
        10 => "EFI-Application",
        0 => "Unknown",
        _ => s.ToString(CultureInfo.InvariantCulture)
    };

    private static string MachineNameElf(ushort m) => m switch
    {
        0x3E => "x86-64",
        0x03 => "x86",
        0xB7 => "AArch64",
        0x28 => "ARM",
        0x08 => "MIPS",
        0x14 => "PowerPC",
        0 => "Unknown",
        _ => "0x" + m.ToString("X", CultureInfo.InvariantCulture)
    };

    private static string ElfTypeName(ushort t) => t switch
    {
        1 => "REL",
        2 => "EXEC",
        3 => "DYN",
        4 => "CORE",
        0 => "NONE",
        _ => "0x" + t.ToString("X", CultureInfo.InvariantCulture)
    };

    private static string ElfSectionType(uint t) => t switch
    {
        0 => "NULL",
        1 => "PROGBITS",
        2 => "SYMTAB",
        3 => "STRTAB",
        4 => "RELA",
        8 => "NOBITS",
        9 => "REL",
        11 => "DYNSYM",
        _ => "0x" + t.ToString("X", CultureInfo.InvariantCulture)
    };

    private static string PeSectionFlags(uint c)
    {
        // IMAGE_SCN_MEM_EXECUTE=0x20000000, READ=0x40000000, WRITE=0x80000000.
        var sb = new StringBuilder(3);
        sb.Append((c & 0x40000000) != 0 ? 'R' : '-');
        sb.Append((c & 0x80000000) != 0 ? 'W' : '-');
        sb.Append((c & 0x20000000) != 0 ? 'X' : '-');
        return sb.ToString();
    }

    private static string FormatTimestamp(uint unix)
    {
        if (unix == 0) return "";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    // -----------------------------------------------------------------------
    // Numeric guards
    // -----------------------------------------------------------------------

    private static double SafeFinite(double v, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

    private static double Clamp(double v, double lo, double hi)
        => v < lo ? lo : (v > hi ? hi : v);
}
