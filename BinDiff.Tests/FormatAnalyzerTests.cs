using Xunit;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Analyzers;
using BinDiff.Core.Util;

namespace BinDiff.Tests;

/// <summary>
/// Deterministic, disk-free tests for <see cref="FormatAnalyzer"/>. A minimal but structurally
/// valid PE (MZ + e_lfanew + PE\0\0 + COFF + optional header + one ".text" section) is built
/// in memory so the parser has real fields to read.
/// </summary>
public sealed class FormatAnalyzerTests
{
    private static readonly ComparisonOptions Opts = new();

    // ------------------------------------------------------------------
    // Minimal PE builder
    // ------------------------------------------------------------------

    private const int PeOff = 0x80;     // where "PE\0\0" begins
    private const int OptOff = PeOff + 24;
    private const int SizeOfOptional = 112; // PE32; covers subsystem@+68 and 2 data dirs (dirBase=+96)
    private const int SectOff = OptOff + SizeOfOptional;

    private static void WriteU16(byte[] buf, int off, ushort v)
    {
        buf[off] = (byte)(v & 0xFF);
        buf[off + 1] = (byte)((v >> 8) & 0xFF);
    }

    private static void WriteU32(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v & 0xFF);
        buf[off + 1] = (byte)((v >> 8) & 0xFF);
        buf[off + 2] = (byte)((v >> 16) & 0xFF);
        buf[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    /// <summary>
    /// Builds a tiny valid PE32 with one section named <paramref name="sectionName"/> whose raw
    /// bytes are <paramref name="sectionBytes"/>. No imports (import dir RVA = 0).
    /// </summary>
    private static byte[] BuildMinimalPe(string sectionName, byte[] sectionBytes, ushort machine = 0x8664, ushort subsystem = 3)
    {
        // Section raw data placed right after the section table.
        int rawPtr = SectOff + 40; // one section header = 40 bytes
        int total = rawPtr + sectionBytes.Length;
        var buf = new byte[total];

        // DOS header
        buf[0] = 0x4D; // 'M'
        buf[1] = 0x5A; // 'Z'
        WriteU32(buf, 0x3C, PeOff); // e_lfanew

        // PE signature "PE\0\0"
        buf[PeOff] = (byte)'P';
        buf[PeOff + 1] = (byte)'E';
        buf[PeOff + 2] = 0;
        buf[PeOff + 3] = 0;

        // COFF header at PeOff+4
        int coff = PeOff + 4;
        WriteU16(buf, coff + 0, machine);              // Machine
        WriteU16(buf, coff + 2, 1);                     // NumberOfSections = 1
        WriteU32(buf, coff + 4, 1_600_000_000);         // TimeDateStamp (fixed, deterministic)
        WriteU16(buf, coff + 16, SizeOfOptional);       // SizeOfOptionalHeader
        WriteU16(buf, coff + 18, 0x0102);               // Characteristics (arbitrary)

        // Optional header
        WriteU16(buf, OptOff, 0x10B);                   // Magic = PE32
        WriteU32(buf, OptOff + 16, 0x1000);             // AddressOfEntryPoint
        WriteU16(buf, OptOff + 68, subsystem);          // Subsystem
        // Data directories start at OptOff+96 (PE32). Import dir (index 1) RVA/Size left 0 -> no imports.

        // Section header (40 bytes)
        int nameLen = Math.Min(8, sectionName.Length);
        for (int i = 0; i < nameLen; i++)
            buf[SectOff + i] = (byte)sectionName[i];
        WriteU32(buf, SectOff + 8, (uint)sectionBytes.Length);   // VirtualSize
        WriteU32(buf, SectOff + 12, 0x1000);                     // VirtualAddress
        WriteU32(buf, SectOff + 16, (uint)sectionBytes.Length);  // SizeOfRawData
        WriteU32(buf, SectOff + 20, (uint)rawPtr);               // PointerToRawData
        WriteU32(buf, SectOff + 36, 0x60000020);                 // Characteristics: R+X + CODE

        // Section raw content
        Array.Copy(sectionBytes, 0, buf, rawPtr, sectionBytes.Length);
        return buf;
    }

    private static byte[] Filled(int len, byte value)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = value;
        return b;
    }

    private static FormatSection Run(byte[] a, byte[] b)
    {
        var analyzer = new FormatAnalyzer();
        var section = analyzer.Analyze(new BinaryImage("A.bin", a), new BinaryImage("B.bin", b), Opts);
        return Assert.IsType<FormatSection>(section);
    }

    // ------------------------------------------------------------------
    // Module-specific property: a hand-crafted PE parses as "PE" with one section
    // ------------------------------------------------------------------

    [Fact]
    public void MinimalPe_IsRecognizedAndSectionParsed()
    {
        var pe = BuildMinimalPe(".text", Filled(64, 0x90)); // 64x NOP
        var section = Run(pe, pe);

        Assert.Null(section.Error);
        Assert.True(section.Applicable);
        Assert.NotNull(section.A);
        Assert.Equal("PE", section.A!.Format);
        Assert.Equal("x64", section.A.Machine);
        Assert.Equal("Console", section.A.Subsystem);
        Assert.Single(section.A.Sections);
        Assert.Equal(".text", section.A.Sections[0].Name);
        Assert.Equal(64, section.A.Sections[0].RawSize);
        Assert.Equal("R-X", section.A.Sections[0].Flags);
        Assert.NotEqual("", section.A.Sections[0].Sha256);
        // Deterministic timestamp render.
        Assert.False(string.IsNullOrEmpty(section.A.TimeStamp));
    }

    // ------------------------------------------------------------------
    // Identical inputs => 100 % headline and every shared section == 100 %
    // ------------------------------------------------------------------

    [Fact]
    public void IdenticalPes_AreFullySimilar()
    {
        var pe = BuildMinimalPe(".text", Filled(128, 0x41));
        var section = Run(pe, pe);

        Assert.Null(section.Error);
        Assert.True(section.Applicable);
        Assert.NotNull(section.SimilarityPercent);
        Assert.Equal(100.0, section.SimilarityPercent!.Value, 6);

        var cmp = Assert.Single(section.SectionComparisons);
        Assert.True(cmp.InA);
        Assert.True(cmp.InB);
        Assert.Equal(100.0, cmp.SimilarityPercent!.Value, 6); // SHA match short-circuits to 100
    }

    // ------------------------------------------------------------------
    // Disjoint section names => low structural similarity, no shared sections
    // ------------------------------------------------------------------

    [Fact]
    public void DisjointSectionNames_HaveNoCommonSectionsAndLowScore()
    {
        var a = BuildMinimalPe(".text", Filled(64, 0x11));
        var b = BuildMinimalPe(".data", Filled(64, 0x22));
        var section = Run(a, b);

        Assert.Null(section.Error);
        Assert.True(section.Applicable);
        // No import sets on either side -> headline is purely section-name Jaccard = 0.
        Assert.NotNull(section.SimilarityPercent);
        Assert.Equal(0.0, section.SimilarityPercent!.Value, 6);

        // Union of two distinct names, neither shared.
        Assert.Equal(2, section.SectionComparisons.Count);
        Assert.All(section.SectionComparisons, c => Assert.False(c.InA && c.InB));
        Assert.All(section.SectionComparisons, c => Assert.Null(c.SimilarityPercent));
    }

    // ------------------------------------------------------------------
    // Same section name, different content => histogram cosine in (0,100), not 100
    // ------------------------------------------------------------------

    [Fact]
    public void SameSectionNameDifferentBytes_UsesHistogramCosine()
    {
        var a = BuildMinimalPe(".text", Filled(256, 0x00)); // all zero bytes
        var b = BuildMinimalPe(".text", Filled(256, 0xFF)); // all 0xFF bytes
        var section = Run(a, b);

        var cmp = Assert.Single(section.SectionComparisons);
        Assert.True(cmp.InA && cmp.InB);
        Assert.NotNull(cmp.SimilarityPercent);
        // Disjoint histograms (only bin 0 vs only bin 255) => cosine 0.
        Assert.Equal(0.0, cmp.SimilarityPercent!.Value, 6);
        Assert.InRange(cmp.SimilarityPercent.Value, 0.0, 100.0);
    }

    // ------------------------------------------------------------------
    // Edge cases: empty and random inputs never throw; Applicable handled
    // ------------------------------------------------------------------

    [Fact]
    public void BothEmpty_NotApplicable_NoScore_NoThrow()
    {
        var section = Run(Array.Empty<byte>(), Array.Empty<byte>());
        Assert.Null(section.Error);
        Assert.False(section.Applicable);
        Assert.Null(section.SimilarityPercent);
        Assert.NotNull(section.A);
        Assert.Equal("Unknown", section.A!.Format);
    }

    [Fact]
    public void RandomBytes_AreUnknownAndNotApplicable()
    {
        // Deterministic pseudo-random bytes (fixed seed, no wall-clock).
        var rnd = new Random(1234);
        var a = new byte[500];
        var b = new byte[733];
        rnd.NextBytes(a);
        rnd.NextBytes(b);
        // Force the MZ magic off so this is definitely not mistaken for a PE.
        a[0] = 0x00; b[0] = 0x00;

        var section = Run(a, b);
        Assert.Null(section.Error);
        Assert.False(section.Applicable);
        Assert.Null(section.SimilarityPercent);
    }

    [Fact]
    public void TruncatedPeHeader_DoesNotThrow_TreatedAsUnknown()
    {
        // Starts with MZ and a plausible e_lfanew, but the buffer is too short for a PE header.
        var buf = new byte[0x40];
        buf[0] = 0x4D; buf[1] = 0x5A;
        WriteU32(buf, 0x3C, 0x1000); // e_lfanew points past the buffer
        var section = Run(buf, buf);
        Assert.Null(section.Error);
        Assert.False(section.Applicable);
        Assert.Equal("Unknown", section.A!.Format);
    }

    [Fact]
    public void OneEmptyOnePe_IsApplicableWithZeroOverlap()
    {
        var pe = BuildMinimalPe(".text", Filled(32, 0x90));
        var section = Run(pe, Array.Empty<byte>());

        Assert.Null(section.Error);
        Assert.True(section.Applicable); // one side is a recognised PE
        Assert.NotNull(section.SimilarityPercent);
        // B has no sections/imports -> Jaccard of section names = 0.
        Assert.Equal(0.0, section.SimilarityPercent!.Value, 6);
        Assert.Equal("Unknown", section.B!.Format);
    }

    // ------------------------------------------------------------------
    // Runs through the engine to prove wiring + non-throwing contract
    // ------------------------------------------------------------------

    [Fact]
    public void ViaEngine_ProducesFormatSection()
    {
        var pe = BuildMinimalPe(".text", Filled(48, 0x90));
        var engine = new AnalyzerEngine(new IAnalyzer[] { new FormatAnalyzer() });
        var opts = new ComparisonOptions { EnabledModules = new HashSet<AnalyzerModule> { AnalyzerModule.Format } };
        var result = engine.Compare(new BinaryImage("A.bin", pe), new BinaryImage("B.bin", pe), opts);

        var fmt = Assert.IsType<FormatSection>(Assert.Single(result.Sections));
        Assert.Null(fmt.Error);
        Assert.True(fmt.Applicable);
        Assert.Equal(100.0, fmt.SimilarityPercent!.Value, 6);
    }
}
