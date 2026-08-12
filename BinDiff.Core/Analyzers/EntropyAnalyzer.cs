using BinDiff.Core.Model;
using BinDiff.Core.Util;

namespace BinDiff.Core.Analyzers;

/// <summary>
/// Block-entropy profile analyzer. Splits each file into fixed-size blocks, measures the
/// Shannon entropy (bits/byte) of each block, and compares the two resulting entropy curves.
/// Useful defensively to spot packed / encrypted regions (high, flat entropy) and to tell
/// whether two samples have a similar layout of compressed vs. structured data.
/// </summary>
public sealed class EntropyAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.Entropy;

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        // A failed entropy calculation must remain visible without aborting the comparison.
        try
        {
            int blockSize = Math.Max(1, options.EntropyBlockSize);
            int maxPoints = options.MaxEntropyProfilePoints;
            double highThreshold = options.HighEntropyThreshold;

            // Keep full profiles for scoring; empty inputs produce empty profiles.
            double[] profileA = EntropyUtil.BlockProfile(a.Data, blockSize);
            double[] profileB = EntropyUtil.BlockProfile(b.Data, blockSize);

            var section = new EntropySection
            {
                BlockSize = blockSize,
                MeanEntropyA = Mean(profileA),
                MeanEntropyB = Mean(profileB),
                MaxEntropyA = Max(profileA),
                MaxEntropyB = Max(profileB),
                HighEntropyFractionA = HighFraction(profileA, highThreshold),
                HighEntropyFractionB = HighFraction(profileB, highThreshold),
                // The GUI only needs a bounded number of points.
                ProfileA = EntropyUtil.Downsample(profileA, maxPoints),
                ProfileB = EntropyUtil.Downsample(profileB, maxPoints),
            };

            section.SimilarityPercent = ProfileSimilarity(profileA, profileB, maxPoints);
            return section;
        }
        catch (Exception ex)
        {
            return new EntropySection
            {
                BlockSize = Math.Max(1, options.EntropyBlockSize),
                Error = ex.Message,
            };
        }
    }

    /// <summary>Average of a profile; 0 when empty. Result is always finite.</summary>
    private static double Mean(double[] profile)
    {
        if (profile.Length == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < profile.Length; i++) sum += profile[i];
        double mean = sum / profile.Length;
        return double.IsFinite(mean) ? mean : 0.0;
    }

    /// <summary>Maximum of a profile; 0 when empty.</summary>
    private static double Max(double[] profile)
    {
        if (profile.Length == 0) return 0.0;
        double max = profile[0];
        for (int i = 1; i < profile.Length; i++)
            if (profile[i] > max) max = profile[i];
        return double.IsFinite(max) ? max : 0.0;
    }

    /// <summary>Fraction of blocks whose entropy meets/exceeds the high-entropy threshold; 0 when empty.</summary>
    private static double HighFraction(double[] profile, double threshold)
    {
        if (profile.Length == 0) return 0.0;
        int high = 0;
        for (int i = 0; i < profile.Length; i++)
            if (profile[i] >= threshold) high++;
        return (double)high / profile.Length;
    }

    /// <summary>
    /// Headline score. Both full profiles are resampled to a common length
    /// N = min(len(A), len(B), maxPoints), N >= 1, then similarity =
    /// 100 * (1 - mean(|a_i - b_i|) / 8), clamped to [0,100].
    /// Both empty => 100; exactly one empty => 0.
    /// </summary>
    private static double ProfileSimilarity(double[] profileA, double[] profileB, int maxPoints)
    {
        bool emptyA = profileA.Length == 0;
        bool emptyB = profileB.Length == 0;
        if (emptyA && emptyB) return 100.0;   // both files empty => identical
        if (emptyA || emptyB) return 0.0;     // exactly one empty => disjoint

        // Invalid caps fall back to the shorter profile so at least one sample is compared.
        int n = Math.Min(profileA.Length, profileB.Length);
        if (maxPoints > 0) n = Math.Min(n, maxPoints);
        if (n < 1) n = 1;

        double[] ra = Resample(profileA, n);
        double[] rb = Resample(profileB, n);

        double diffSum = 0.0;
        for (int i = 0; i < n; i++)
            diffSum += Math.Abs(ra[i] - rb[i]);

        double meanDiff = diffSum / n;                 // in bits/byte, 0..8
        double similarity = 100.0 * (1.0 - meanDiff / 8.0);
        if (!double.IsFinite(similarity)) similarity = 0.0;
        return Math.Clamp(similarity, 0.0, 100.0);
    }

    /// <summary>
    /// Resamples a non-empty profile to exactly <paramref name="n"/> points by averaging the
    /// source samples that fall in each output bucket (box down-sampling). When n exceeds the
    /// source length this up-samples by repeating the nearest source bucket, which keeps the
    /// two resampled curves aligned for point-wise comparison. O(len + n).
    /// </summary>
    private static double[] Resample(double[] profile, int n)
    {
        var result = new double[n];
        if (profile.Length == n)
        {
            Array.Copy(profile, result, n);
            return result;
        }

        double bucket = (double)profile.Length / n;
        for (int i = 0; i < n; i++)
        {
            int start = (int)(i * bucket);
            int end = (int)((i + 1) * bucket);
            if (end <= start) end = start + 1;          // ensure at least one sample (up-sampling case)
            if (end > profile.Length) end = profile.Length;
            if (start >= profile.Length) start = profile.Length - 1;
            if (start < 0) start = 0;

            double sum = 0.0;
            int count = 0;
            for (int j = start; j < end; j++) { sum += profile[j]; count++; }
            result[i] = count > 0 ? sum / count : 0.0;
        }
        return result;
    }
}
