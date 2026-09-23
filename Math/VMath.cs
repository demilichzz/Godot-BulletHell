using System;
using Godot;

/// <summary>指定随机偏移区间从零起算或以零为中心。</summary>
public enum RandomDiffMode
{
    /// <summary>偏移位于零至总偏差值之间。</summary>
    Forward,
    /// <summary>偏移位于总偏差值一半的正负范围内。</summary>
    Center
}

/// <summary>提供统一弧度的坐标数学工具及固定算法的可重现随机序列。</summary>
public static class VMath
{
    /// <summary>取得当前 Boss 指向玩家的标准弧度。</summary>
    /// <returns>[0,2π)内的弧度，使用双方全局逻辑像素坐标。</returns>
    public static double getB2PAngle()
        => GetAngleBetween2Points(GlobalEvent.GetBoss().GlobalPosition, GlobalEvent.GetPlayer().GlobalPosition);
    /// <summary>计算同一坐标系中两点的欧氏距离。</summary>
    /// <param name="x">起点横坐标，有限逻辑像素。</param>
    /// <param name="y">起点纵坐标，有限逻辑像素。</param>
    /// <param name="x_tar">目标横坐标，有限逻辑像素。</param>
    /// <param name="y_tar">目标纵坐标，有限逻辑像素。</param>
    /// <returns>非负距离，逻辑像素；重合点返回0，超出double范围抛错。</returns>
    public static double GetDistanceBetween2Points(double x, double y, double x_tar, double y_tar)
    {
        ValidateFinite(x, nameof(x)); ValidateFinite(y, nameof(y));
        ValidateFinite(x_tar, nameof(x_tar)); ValidateFinite(y_tar, nameof(y_tar));
        // 按最大分量缩放，避免直接平方溢出或下溢。
        double dx = Math.Abs(x_tar - x), dy = Math.Abs(y_tar - y);
        double largest = Math.Max(dx, dy);
        if (largest == 0) return 0;
        if (!double.IsFinite(largest)) throw new OverflowException("两点距离超出double范围。");
        double ratio = Math.Min(dx, dy) / largest;
        return FiniteResult(largest * Math.Sqrt(1 + ratio * ratio));
    }
    /// <summary>计算两个同一坐标系的坐标值之间的距离，不转换坐标空间。</summary>
    /// <param name="source">起点，逻辑像素。</param>
    /// <param name="target">目标，逻辑像素。</param>
    /// <returns>非负距离，单位为逻辑像素。</returns>
    public static double GetDistanceBetween2Points(Vector2 source, Vector2 target)
        => GetDistanceBetween2Points(source.X, source.Y, target.X, target.Y);
    /// <summary>计算起点指向目标的标准弧度，0向右、π/2向下，顺时针为正。</summary>
    /// <param name="x">起点横坐标，有限逻辑像素。</param>
    /// <param name="y">起点纵坐标，有限逻辑像素。</param>
    /// <param name="x_tar">目标横坐标，有限逻辑像素。</param>
    /// <param name="y_tar">目标纵坐标，有限逻辑像素。</param>
    /// <returns>[0,2π)内的弧度；重合点返回0。</returns>
    public static double GetAngleBetween2Points(double x, double y, double x_tar, double y_tar)
    {
        ValidateFinite(x, nameof(x)); ValidateFinite(y, nameof(y));
        ValidateFinite(x_tar, nameof(x_tar)); ValidateFinite(y_tar, nameof(y_tar));
        // 极大坐标相减溢出时，两个轴同时减半以保留方向比例。
        double dx = x_tar - x, dy = y_tar - y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
        {
            dx = x_tar / 2 - x / 2;
            dy = y_tar / 2 - y / 2;
        }
        if (dx == 0 && dy == 0) return 0;
        return StandardizationAngle(Math.Atan2(dy, dx));
    }
    /// <summary>计算同一坐标系中两个坐标值之间的标准方向弧度。</summary>
    /// <param name="source">起点，逻辑像素。</param>
    /// <param name="target">目标，逻辑像素。</param>
    /// <returns>[0,2π)内弧度，0向右、π/2向下；重合点返回0。</returns>
    public static double GetAngleBetween2Points(Vector2 source, Vector2 target)
        => GetAngleBetween2Points(source.X, source.Y, target.X, target.Y);
    /// <summary>将有限弧度归一到[0,2π)，整圈与负零返回0。</summary>
    /// <param name="angle">有限弧度，允许负值及多圈。</param>
    /// <returns>标准方向弧度，不适用于需要保留圈数的旋转量。</returns>
    public static double StandardizationAngle(double angle)
    {
        ValidateFinite(angle, nameof(angle));
        // 取模不随圈数增加迭代次数，补偿负余数及上界舍入。
        double result = angle % Math.Tau;
        if (result < 0) result += Math.Tau;
        return result == 0 || result >= Math.Tau ? 0 : result;
    }
    /// <summary>标准化后转换为单精度方向，避免舍入到整圈上界。</summary>
    /// <param name="angle">有限弧度，允许负值及多圈。</param>
    /// <returns>[0,2π)内的单精度弧度。</returns>
    internal static float StandardizationAngleFloat(double angle)
    {
        // 单精度2π略大于真实2π，转换后的上界须重新归零。
        float result = (float)StandardizationAngle(angle);
        return result >= Math.Tau ? 0 : result;
    }
    /// <summary>从起点按弧度与距离计算新坐标，不修改输入或创建节点。</summary>
    /// <param name="source">同一坐标系内的有限起点，逻辑像素。</param>
    /// <param name="angle">有限弧度，0向右、π/2向下，计算前标准化。</param>
    /// <param name="dist">有限位移，逻辑像素；负值表示反向，0保持原位。</param>
    /// <returns>新坐标；超出Vector2单精度有限范围时抛错。</returns>
    public static Vector2 PolarMove(Vector2 source, double angle, double dist)
    {
        ValidateFinite(source.X, nameof(source)); ValidateFinite(source.Y, nameof(source));
        ValidateFinite(dist, nameof(dist));
        // 双精度三角计算后再转换为Godot单精度坐标。
        double normalized = StandardizationAngle(angle);
        double x = source.X + Math.Cos(normalized) * dist;
        double y = source.Y + Math.Sin(normalized) * dist;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue)
            throw new OverflowException("极坐标结果超出Vector2范围。");
        return new Vector2((float)x, (float)y);
    }
    /// <summary>仅转换度数单位，保留旋转量的正负与圈数；方向由取角函数标准化。</summary>
    /// <param name="degrees">有限度数。</param>
    /// <returns>对应弧度。</returns>
    public static double DegreesToRadians(double degrees)
    {
        ValidateFinite(degrees, nameof(degrees));
        return FiniteResult(degrees * (Math.PI / 180));
    }
    /// <summary>仅转换弧度单位，保留旋转量的正负与圈数；方向由取角函数标准化。</summary>
    /// <param name="radians">有限弧度。</param>
    /// <returns>对应度数；超出double有限范围时抛错。</returns>
    public static double RadiansToDegrees(double radians)
    {
        ValidateFinite(radians, nameof(radians));
        return FiniteResult(radians * (180 / Math.PI));
    }
    /// <summary>拒绝非有限的数学输入。</summary>
    /// <param name="value">需要校验的数值。</param>
    /// <param name="name">用于异常说明的参数名。</param>
    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
    /// <summary>检查数学结果的double溢出。</summary>
    /// <param name="value">已经计算的结果。</param>
    /// <returns>有限结果，否则抛错。</returns>
    private static double FiniteResult(double value)
        => double.IsFinite(value) ? value : throw new OverflowException("数学结果超出double范围。");
    // SplitMix64内部状态，按无符号64位整数回绕，不受运行库随机实现影响。
    private static ulong _state;
    /// <summary>最近设置的初始种子，默认0；抽样不会修改此值。</summary>
    public static int randomSeed { get; private set; }
    /// <summary>设置初始种子并从头重置随机序列。</summary>
    /// <param name="seed">32位有符号种子，默认0；负数按32位无符号位模式映射后扩展到64位。</param>
    public static void setRandomSeed(int seed = 0)
    {
        randomSeed = seed;
        _state = unchecked((uint)seed);
    }
    /// <summary>获取闭区间内的均匀随机整数；相等端点不消耗序列。</summary>
    /// <param name="min">包含的下界，可为int.MinValue。</param>
    /// <param name="max">包含的上界，须不小于min，可为int.MaxValue。</param>
    /// <returns>位于[min,max]的整数。</returns>
    public static int getRandomInt(int min, int max)
    {
        if (min > max) throw new ArgumentOutOfRangeException(nameof(max));
        if (min == max) return min;
        // 使用64位计算跨度，完整int范围包含2的32次方个值。
        ulong span = (ulong)((long)max - min) + 1;
        ulong domain = 1UL << 32;
        ulong limit = domain - domain % span;
        ulong sample;
        // 拒绝不能均分的尾部样本，避免取模偏差。
        do { sample = NextUInt64() >> 32; } while (sample >= limit);
        return (int)((long)min + (long)(sample % span));
    }
    /// <summary>获取包含两端的随机小数，使用53位离散均匀样本；相等端点不消耗序列。</summary>
    /// <param name="min">包含的有限下界。</param>
    /// <param name="max">包含的有限上界，须不小于min。</param>
    /// <returns>位于[min,max]的有限双精度数。</returns>
    public static double getRandomDouble(double min, double max)
    {
        if (!double.IsFinite(min)) throw new ArgumentOutOfRangeException(nameof(min));
        if (!double.IsFinite(max) || min > max) throw new ArgumentOutOfRangeException(nameof(max));
        if (min == max) return min;
        // 53位整数除以最大53位整数，使0和1均可取到。
        double fraction = (NextUInt64() >> 11) / 9007199254740991.0;
        // 加权插值避免max-min在跨越双精度两极时溢出，夹紧舍入误差。
        return Math.Clamp(min * (1 - fraction) + max * fraction, min, max);
    }
    /// <summary>按给定总宽度获取随机偏移，沿用现有双精度随机序列。</summary>
    /// <param name="diff">非负有限总偏差值；零不消耗随机序列。</param>
    /// <param name="mode">Forward为[0,diff]，Center为[-diff/2,diff/2]；默认Forward。</param>
    /// <returns>指定区间内的随机偏移。</returns>
    public static double getRandomDiff(double diff, RandomDiffMode mode = RandomDiffMode.Forward)
    {
        if (!double.IsFinite(diff) || diff < 0) throw new ArgumentOutOfRangeException(nameof(diff));
        return mode switch
        {
            RandomDiffMode.Forward => getRandomDouble(0, diff),
            RandomDiffMode.Center => getRandomDouble(-diff / 2, diff / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    /// <summary>按固定SplitMix64常量产生下一份64位样本。</summary>
    /// <returns>覆盖64位空间的无符号随机整数。</returns>
    private static ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            // 混合临时状态，固定移位和乘数属于序列版本的一部分。
            ulong value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
