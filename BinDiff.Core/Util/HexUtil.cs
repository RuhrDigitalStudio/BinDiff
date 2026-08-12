namespace BinDiff.Core.Util;

/// <summary>Hex formatting helpers for pattern/signature display.</summary>
public static class HexUtil
{
    /// <summary>Uppercase hex without separators, e.g. "4D5A9000".</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    /// <summary>Uppercase hex with a space between each byte, e.g. "4D 5A 90 00".</summary>
    public static string ToHexSpaced(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return "";
        var sb = new System.Text.StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
