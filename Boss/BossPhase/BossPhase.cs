using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>执行独立JSON阶段，复用阶段时间线、Emitter生命周期和VPath几何。</summary>
public sealed class BossPhase : IVTimelineOwner
{
    // 当前移动路段的起点和累计秒数；不另建运动容器。
    private Vector2 _travelStart;
    private double _travelSeconds;
    // 仅保存本阶段绑定的Emitter，停止时由其自身决定遗留子弹。
    private readonly List<VBulletEmitter> _emitters = new();
    /// <summary>由本阶段推进年龄的独占时间线。</summary>
    public VTimeline? Timeline { get; private set; }
    /// <summary>按声明顺序绑定的Emitter只读视图。</summary>
    public IReadOnlyList<VBulletEmitter> Emitters { get; }
    /// <summary>当前目标，战场局部逻辑像素，右下为正。</summary>
    public Vector2 MoveTarget { get; private set; } = new(640, 240);
    /// <summary>是否正在前往当前目标。</summary>
    public bool IsMoving { get; private set; }
    /// <summary>阶段静态定义，不包含本场运行状态。</summary>
    public BossPhaseDefinition Definition { get; }
    /// <summary>阶段在Boss队列中的零基序号。</summary>
    public int Index { get; }
    /// <summary>阶段显示名称。</summary>
    public string Name => Definition.Name;
    // 阶段独占移动配置、固定目标索引及路径采样状态。
    private readonly BossMovement _movement;
    // 每次进入阶段创建独立树；预览来源只保存冻结文本。
    private readonly Func<string, VBulletEmitter> _loadEmitter;
    private int _nextTarget;
    private Vector2[] _path = Array.Empty<Vector2>();
    private double[] _lengths = Array.Empty<double>();

    /// <summary>建立尚未启动的独立阶段。</summary>
    /// <param name="definition">已校验的阶段定义。</param>
    /// <param name="index">队列零基序号。</param>
    /// <param name="loadEmitter">可选Emitter创建入口，默认读取正式文件。</param>
    public BossPhase(BossPhaseDefinition definition, int index, Func<string, VBulletEmitter>? loadEmitter = null)
    {
        Emitters = _emitters.AsReadOnly();
        Definition = definition;
        Index = index;
        _loadEmitter = loadEmitter ?? VBulletEmitter.Load;
        _movement = definition.Movement;
    }
    /// <summary>进入阶段时的Boss总血量；未来阶段尚未受伤。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>本阶段及剩余阶段的生命点数总和。</returns>
    public int GetInitialHp(BossController boss) => boss.GetRemainingPhaseHp(Index);
    /// <summary>取得中心移动或显式入场目标，随机与路径默认从当前位置开始。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>战场局部逻辑像素目标。</returns>
    private Vector2 GetInitialMoveTarget(BossController boss)
        => _movement.Target is not null ? BossMovement.Point(_movement.Target)
            : _movement.Type == "Center" ? new Vector2(640, 240) : boss.Position;

    /// <summary>建立本阶段时间线，先登记移动，再依次绑定独立发射器。</summary>
    /// <param name="boss">本场Boss，玩家与战斗服务已就绪。</param>
    public void Enter(BossController boss)
    {
        _emitters.Clear();
        Timeline = GlobalEvent.CreateTimeline(this);
        SetMoveTarget(boss, GetInitialMoveTarget(boss));
        _nextTarget = 0;
        if (_movement.Type is "RandomCircle" or "RandomRect" or "Sequence")
            Timeline!.Repeat(_movement.StartMs, _movement.IntervalMs, null, () => ChooseTarget(boss));
        if (_movement.Type == "Path")
        {
            // 在激活时冻结路径世界坐标，后续仅按阶段整数年龄推进距离。
            Vector2 origin = boss.GlobalPosition;
            var geometry = VPathCreator.CreateGeometry(_movement.PathQueue, _movement.PointCount);
            _path = geometry.SampleGeometry(origin).Select(offset => origin + offset).ToArray();
            _lengths = new double[_path.Length];
            for (int index = 1; index < _path.Length; index++)
                _lengths[index] = _lengths[index - 1] + VMath.GetDistanceBetween2Points(_path[index - 1], _path[index]);
            if (_movement.Loop && _path[0].DistanceTo(_path[^1]) > 0.001f)
                throw new InvalidOperationException("循环Boss路径必须首尾闭合。");
            boss.GlobalPosition = _path[0];
            MoveTarget = boss.Position + (_path[^1] - _path[0]);
            IsMoving = true;
        }
        // 每次进入阶段重新加载独立树，手动回退或重开不共享批次。
        foreach (string path in Definition.Emitters) BindEmitter(_loadEmitter(path), boss);
    }
    /// <summary>在阶段时间线上选择下一个目标，不由渲染帧消耗随机。</summary>
    /// <param name="boss">提供移动起点的Boss。</param>
    private void ChooseTarget(BossController boss)
    {
        // 声明顺序固定：圆形先角度再半径，矩形先X再Y。
        Vector2 target;
        if (_movement.Type == "Sequence")
        {
            target = BossMovement.Point(_movement.Targets![_nextTarget]);
            _nextTarget = (_nextTarget + 1) % _movement.Targets.Count;
        }
        else if (_movement.Type == "RandomCircle")
        {
            double angle = VMath.getRandomDouble(0, Math.Tau);
            double radius = Sample(_movement.MinRadius, _movement.MaxRadius);
            target = VMath.PolarMove(BossMovement.Point(_movement.Center), angle, radius);
        }
        else
        {
            Vector2 min = BossMovement.Point(_movement.Min), max = BossMovement.Point(_movement.Max);
            target = new Vector2((float)Sample(min.X, max.X), (float)Sample(min.Y, max.Y));
        }
        SetMoveTarget(boss, target);
    }
    /// <summary>抽样闭区间配置，固定值不消耗业务随机。</summary>
    /// <param name="min">最小值。</param>
    /// <param name="max">不小于最小值的最大值。</param>
    /// <returns>固定值或VMath抽样值。</returns>
    private static double Sample(double min, double max) => min == max ? min : VMath.getRandomDouble(min, max);
    /// <summary>推进阶段年龄与移动，在下一棵Emitter树更新前完成。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <param name="delta">固定步非负秒数。</param>
    public void Advance(BossController boss, double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (Timeline is null || boss.IsDefeated) return;
        Timeline.AdvanceUnits(VTimeline.SecondsToUnits(delta));
        // Boss先完成本步移动，再由管理器沿树推进Emitter及成员。
        if (_movement.Type != "Path")
        {
            if (IsMoving)
            {
                _travelSeconds += delta;
                boss.Position = _travelStart.MoveToward(MoveTarget, (float)(_travelSeconds * _movement.Speed));
                if (boss.Position == MoveTarget) IsMoving = false;
            }
            return;
        }
        // 累计距离由整数年龄求值，速度单位为像素/秒。
        double distance = Timeline.ElapsedUnits / (double)VTimerProcessor.UnitsPerSecond * _movement.Speed;
        if (_movement.Loop) distance %= _lengths[^1];
        if (distance >= _lengths[^1]) { boss.GlobalPosition = _path[^1]; IsMoving = false; return; }
        // 二分查找采样路段，长度平台不参与除法。
        int upper = Array.BinarySearch(_lengths, distance);
        if (upper >= 0) { boss.GlobalPosition = _path[upper]; return; }
        upper = ~upper;
        int lower = Math.Max(0, upper - 1);
        boss.GlobalPosition = _path[lower].Lerp(_path[upper],
            (float)((distance - _lengths[lower]) / (_lengths[upper] - _lengths[lower])));
    }
    /// <summary>直接检查血池与整数年龄，HealthOrTime采用逻辑或。</summary>
    /// <param name="boss">所属Boss，提供当前阶段血量。</param>
    /// <returns>当前阶段是否满足结束条件。</returns>
    public bool ShouldEnd(BossController boss)
        => (Definition.EndCondition != "Time" && boss.PhaseHp == 0)
            || (Definition.EndCondition != "Health" && Timeline is not null
                && Timeline.ElapsedUnits >= (long)Definition.DurationMs!.Value * (VTimerProcessor.UnitsPerSecond / 1000));

    /// <summary>绑定并启动独立Emitter时间线。</summary>
    /// <param name="emitter">本阶段独占的未启动Emitter。</param>
    /// <param name="boss">提供生命周期和空间参考的Boss。</param>
    private void BindEmitter(VBulletEmitter emitter, BossController boss)
    {
        if (_emitters.Contains(emitter)) throw new InvalidOperationException("发射器不能重复绑定。");
        emitter.Start(boss, GlobalEvent.GetBulletManager());
        _emitters.Add(emitter);
    }
    /// <summary>从Boss当前位置开始新的移动路段。</summary>
    /// <param name="boss">提供路段起点的Boss。</param>
    /// <param name="target">战场局部逻辑像素目标。</param>
    private void SetMoveTarget(BossController boss, Vector2 target)
    {
        _travelStart = boss.Position;
        _travelSeconds = 0;
        MoveTarget = target;
        IsMoving = boss.Position != target;
    }
    /// <summary>切换或结束阶段时取消自身动作并停止绑定的Emitter。</summary>
    /// <param name="boss">本阶段所属Boss。</param>
    public void Exit(BossController boss)
    {
        IsMoving = false;
        Timeline?.Cancel();
        Timeline = null;
        foreach (var emitter in _emitters) emitter.Stop();
        _emitters.Clear();
    }
}
