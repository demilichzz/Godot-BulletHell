using System;
using System.Collections.Generic;
using Godot;

/// <summary>普通弹幕的连续出界计时，使用固定逻辑步的整数时间单位。</summary>
public partial class VBullet
{
    /// <summary>中心连续出界后的强制消失阈值，非负整数毫秒，默认2000；0表示出界即消失。</summary>
    public long OutsideTimeoutMs { get; private set; } = 2000;
    /// <summary>世界出界判定区域，默认游戏区域；与反射区域独立，按弹幕中心判断。</summary>
    public IRegionShape OutsideRegion { get; private set; } = BattleConfig.GameRegion;
    /// <summary>当前连续位于指定区域外的逻辑秒数，回到区域内时清零。</summary>
    public double OutsideAge => _outsideUnits / (double)VTimerProcessor.UnitsPerSecond;
    // 固定步整数计时；单独保存出界标记，区分阈值为0与位于区域内。
    private long _outsideUnits;
    private bool _isOutside;
    /// <summary>是否已经满足出界强制消失条件。</summary>
    internal bool OutsideExpired => _isOutside && _outsideUnits >= OutsideTimeoutMs * VTimerProcessor.UnitsPerMillisecond;

    /// <summary>出生时保存并重置本颗子弹的出界策略。</summary>
    /// <param name="milliseconds">非负整数毫秒，默认策略为2000。</param>
    private void ConfigureOutside(long milliseconds)
    {
        ValidateOutsideTimeout(milliseconds);
        OutsideTimeoutMs = milliseconds;
        _outsideUnits = 0;
        _isOutside = false;
    }

    /// <summary>在每次普通弹幕运动完成后检查中心，区域边界算作区域内。</summary>
    /// <param name="delta">本次逻辑秒数，正式战斗为1/60；0不增加计时。</param>
    internal void UpdateOutsideTime(double delta)
    {
        // 回到区域内立即结束上一段计时；不占用时间线动作。
        _isOutside = !OutsideRegion.Contains(WorldPosition);
        if (!_isOutside) _outsideUnits = 0;
        else
        {
            long units = VTimeline.SecondsToUnits(delta);
            long limit = OutsideTimeoutMs * VTimerProcessor.UnitsPerMillisecond;
            _outsideUnits += Math.Min(units, limit - _outsideUnits);
        }
    }

    /// <summary>验证毫秒阈值可转换为固定步内部时间单位。</summary>
    /// <param name="milliseconds">非负整数毫秒。</param>
    internal static void ValidateOutsideTimeout(long milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        _ = checked(milliseconds * VTimerProcessor.UnitsPerMillisecond);
    }

    /// <summary>是否启用边界反射，默认false，仅用于普通子弹。</summary>
    public bool Reflectable { get; private set; }
    /// <summary>不可变世界反射区域，不改变OutsideRegion的出界判断。</summary>
    public VReflectionRegion? ReflectionRegion { get; private set; }
    // 仅反射弹幕创建并复用本步折线路径，供连续碰撞判断使用。
    private List<ReflectionMotionSegment>? _reflectionMotion;

    /// <summary>本步运动的一段直线及其时间范围，避免把反射折线当成首尾连线。</summary>
    private readonly record struct ReflectionMotionSegment
    {
        /// <summary>世界起点，逻辑像素。</summary>
        public Vector2 Start { get; init; }
        /// <summary>世界终点，逻辑像素。</summary>
        public Vector2 End { get; init; }
        /// <summary>本步起始时间比例，0至1。</summary>
        public double From { get; init; }
        /// <summary>本步结束时间比例，0至1。</summary>
        public double To { get; init; }
    }

    /// <summary>预测本步出界并在命中边界后反射剩余位移，保留实际速率和其他运动参数。</summary>
    /// <param name="delta">本步非负逻辑秒数，正式战斗固定1/60。</param>
    protected override void AdvancePosition(double delta)
    {
        _reflectionMotion?.Clear();
        if (!Reflectable || delta == 0 || Velocity == Vector2.Zero)
        {
            base.AdvancePosition(delta);
            return;
        }
        // 只记录本步几何，不依赖随机或反射后重新累计加速度。
        _reflectionMotion ??= new();
        Vector2 position = WorldPosition, velocity = Velocity;
        double elapsed = 0;
        bool reflected = false;
        for (int bounce = 0; ; bounce++)
        {
            Vector2 end = position + velocity * (float)(delta * (1 - elapsed));
            if (!end.IsFinite()) throw new OverflowException("反射预测位置溢出。");
            Vector2 nextVelocity = velocity;
            bool hits = ReflectionRegion!.Shape.TryGetExit(position, end, out var exit)
                && ReflectionRegion.TryReflect(exit, velocity, out nextVelocity);
            if (!hits)
            {
                _reflectionMotion.Add(new ReflectionMotionSegment { Start = position, End = end, From = elapsed, To = 1 });
                position = end;
                break;
            }
            // 极端速度或退化接触不得令单步无限循环；报错不静默吞掉剩余位移。
            if (bounce >= 128) throw new InvalidOperationException("单逻辑步反射超过128次，请降低弹速或增大反射区域。");
            double arrival = elapsed + (1 - elapsed) * exit.Fraction;
            _reflectionMotion.Add(new ReflectionMotionSegment { Start = position, End = exit.Point, From = elapsed, To = arrival });
            velocity = nextVelocity;
            position = exit.Point;
            elapsed = arrival;
            reflected = true;
            if (elapsed >= 1) break;
        }
        GlobalPosition = position;
        if (reflected) ApplyReflectedVelocity(velocity);
    }

    /// <summary>按本步反射折线和目标运动计算连续命中，普通弹幕沿用原直线判定。</summary>
    /// <param name="previous">本步开始前显示的子弹世界位置，包含父平移前的位置。</param>
    /// <param name="targetStart">目标上步世界位置。</param>
    /// <param name="targetEnd">目标本步世界位置。</param>
    /// <param name="radius">双方碰撞半径和，非负像素。</param>
    /// <returns>任一实际运动段接触目标判定圆时为真。</returns>
    internal bool SweptRegionHit(Vector2 previous, Vector2 targetStart, Vector2 targetEnd, double radius)
    {
        if (_reflectionMotion is null || _reflectionMotion.Count == 0)
            return VMath.SweptHit(previous - targetStart, GlobalPosition - targetEnd, radius);
        // 父平移在本节点运动前完成；保留原位置到当前参考位置之间的扫掠。
        if (previous != _reflectionMotion[0].Start
            && VMath.SweptHit(previous - targetStart, _reflectionMotion[0].Start - targetStart, radius)) return true;
        foreach (var segment in _reflectionMotion)
        {
            Vector2 start = targetStart.Lerp(targetEnd, (float)segment.From);
            Vector2 end = targetStart.Lerp(targetEnd, (float)segment.To);
            if (VMath.SweptHit(segment.Start - start, segment.End - end, radius)) return true;
        }
        return false;
    }
}
