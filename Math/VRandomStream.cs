using System;

/// <summary>由VMath创建的独立SplitMix64随机流，状态不与默认战斗流共享。</summary>
public sealed class VRandomStream
{
    // 按固定算法回绕的独立64位状态。
    private ulong _state;
    /// <summary>初始32位种子，抽样不修改此值。</summary>
    public int Seed { get; private set; }
    /// <summary>从指定种子建立全新状态。</summary>
    /// <param name="seed">32位种子，负值按无符号32位映射。</param>
    internal VRandomStream(int seed) => Reset(seed);

    /// <summary>原位重置本流，保留所属战斗与当前上下文的同一引用。</summary>
    /// <param name="seed">32位种子，负值按无符号32位映射。</param>
    internal void Reset(int seed) { Seed = seed; _state = unchecked((uint)seed); }

    /// <summary>取得闭区间均匀整数，相等端点不消耗状态。</summary>
    /// <param name="min">包含的整数下界。</param>
    /// <param name="max">包含的上界，不小于下界。</param>
    /// <returns>区间内的确定性随机整数。</returns>
    public int GetRandomInt(int min, int max)
    {
        if (min > max) throw new ArgumentOutOfRangeException(nameof(max));
        if (min == max) return min;
        // 沿用默认流的拒绝采样及区间映射。
        ulong span = (ulong)((long)max - min) + 1, domain = 1UL << 32;
        ulong limit = domain - domain % span, sample;
        do { sample = NextUInt64() >> 32; } while (sample >= limit);
        return (int)((long)min + (long)(sample % span));
    }
    /// <summary>取得包含端点的53位随机小数，相等端点不消耗状态。</summary>
    /// <param name="min">有限下界。</param>
    /// <param name="max">有限上界，不小于下界。</param>
    /// <returns>区间内的有限双精度数。</returns>
    public double GetRandomDouble(double min, double max)
    {
        if (!double.IsFinite(min)) throw new ArgumentOutOfRangeException(nameof(min));
        if (!double.IsFinite(max) || max < min) throw new ArgumentOutOfRangeException(nameof(max));
        if (min == max) return min;
        // 与默认流相同的53位闭区间映射及防溢出插值。
        double fraction = (NextUInt64() >> 11) / 9007199254740991.0;
        return Math.Clamp(min * (1 - fraction) + max * fraction, min, max);
    }
    /// <summary>取得指定总宽度内的随机偏移。</summary>
    /// <param name="diff">非负有限总宽度，零不消耗状态。</param>
    /// <param name="mode">Forward从零起算，Center以零为中心；默认Forward。</param>
    /// <returns>指定偏移范围内的随机数。</returns>
    public double GetRandomDiff(double diff, RandomDiffMode mode = RandomDiffMode.Forward)
    {
        if (!double.IsFinite(diff) || diff < 0) throw new ArgumentOutOfRangeException(nameof(diff));
        return mode switch
        {
            RandomDiffMode.Forward => GetRandomDouble(0, diff),
            RandomDiffMode.Center => GetRandomDouble(-diff / 2, diff / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    /// <summary>按默认流相同的固定常量推进独立状态。</summary>
    /// <returns>下一份64位样本。</returns>
    private ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            // 仅混合本对象状态，不读取默认流。
            ulong value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}