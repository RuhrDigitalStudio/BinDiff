namespace BinDiff.Core.Util;

/// <summary>Shannon entropy helpers, shared so every module measures entropy identically.</summary>
public static class EntropyUtil
{
    /// <summary>Shannon entropy of a byte span in bits/byte, range 0..8. Empty span returns 0.</summary>
    public static double ShannonEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return 0.0;

        Span<int> counts = stackalloc int[256];
        foreach (var b in data) counts[b]++;

        double entropy = 0.0;
        double len = data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0) continue;
            double p = counts[i] / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    /// <summary>
    /// Entropy for each fixed-size block of <paramref name="data"/> (last block may be shorter).
    /// Returns bits/byte per block. blockSize is clamped to at least 1.
    /// </summary>
    public static double[] BlockProfile(byte[] data, int blockSize)
    {
        if (data.Length == 0) return Array.Empty<double>();
        blockSize = Math.Max(1, blockSize);

        int blocks = (data.Length + blockSize - 1) / blockSize;
        var profile = new double[blocks];
        for (int i = 0; i < blocks; i++)
        {
            int start = i * blockSize;
            int len = Math.Min(blockSize, data.Length - start);
            profile[i] = ShannonEntropy(data.AsSpan(start, len));
        }
        return profile;
    }

    /// <summary>
    /// Down-samples a profile to at most <paramref name="maxPoints"/> points by averaging buckets,
    /// preserving the overall shape for display. Returns the input unchanged if already small enough.
    /// </summary>
    public static double[] Downsample(double[] profile, int maxPoints)
    {
        if (maxPoints <= 0 || profile.Length <= maxPoints) return profile;
        var result = new double[maxPoints];
        double bucket = (double)profile.Length / maxPoints;
        for (int i = 0; i < maxPoints; i++)
        {
            int start = (int)(i * bucket);
            int end = (int)((i + 1) * bucket);
            if (end <= start) end = start + 1;
            if (end > profile.Length) end = profile.Length;
            double sum = 0;
            for (int j = start; j < end; j++) sum += profile[j];
            result[i] = sum / (end - start);
        }
        return result;
    }
}
