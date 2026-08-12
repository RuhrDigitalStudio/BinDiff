using System.Security.Cryptography;

namespace BinDiff.Core.Model;

/// <summary>
/// A binary file loaded fully into memory once and shared read-only across all analyzers.
/// The tool never writes back to disk; this type exposes bytes for analysis only.
/// </summary>
public sealed class BinaryImage
{
    public string Path { get; }
    public string Name { get; }
    public byte[] Data { get; }
    public long Size => Data.LongLength;
    public string Sha256 { get; }

    public BinaryImage(string path, byte[] data)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Name = System.IO.Path.GetFileName(path);
        Sha256 = Convert.ToHexString(SHA256.HashData(data));
    }

    /// <summary>Loads a file from disk into a <see cref="BinaryImage"/>. Read-only.</summary>
    public static BinaryImage Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Input file not found.", path);
        var bytes = File.ReadAllBytes(path);
        return new BinaryImage(path, bytes);
    }

    /// <summary>Compact, serialisable description of an image (no raw bytes) for reports.</summary>
    public BinaryImageInfo ToInfo() => new()
    {
        Path = Path,
        Name = Name,
        Size = Size,
        Sha256 = Sha256
    };
}

/// <summary>Serialisable metadata about an input file, without the byte payload.</summary>
public sealed class BinaryImageInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
