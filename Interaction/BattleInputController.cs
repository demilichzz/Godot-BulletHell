using Godot;

/// <summary>一物理步的语义输入，不包含设备编号或按键。</summary>
/// <param name="Movement">右下为正的移动向量，保留小幅模拟输入。</param>
/// <param name="DodgeHeld">闪避动作当前是否保持，用于场景进入隔离。</param>
/// <param name="DodgePressed">本步闪避动作的新按下沿。</param>
/// <param name="RestartPressed">本步重开动作的新按下沿。</param>
/// <param name="PreviousPhasePressed">本步上一阶段动作的新按下沿。</param>
/// <param name="NextPhasePressed">本步下一阶段动作的新按下沿。</param>
public readonly record struct BattleInputFrame(Vector2 Movement, bool DodgeHeld, bool DodgePressed,
    bool RestartPressed, bool PreviousPhasePressed, bool NextPhasePressed);

/// <summary>操作层提供的固定步输入源，替换设备无需改变战斗行为。</summary>
public interface IBattleInputSource
{
    /// <summary>采集本物理步输入，不推进逻辑时钟。</summary>
    /// <returns>独立的语义输入快照。</returns>
    BattleInputFrame Read();
}

/// <summary>行为层可调用的战斗入口，不暴露玩家、弹幕或UI对象。</summary>
public interface IBattleActions
{
    /// <summary>是否允许继续接受操作。</summary>
    bool IsStopped { get; }
    /// <summary>当前战斗状态。</summary>
    BattleState State { get; }
    /// <summary>重建战斗初态。</summary>
    void Restart();
    /// <summary>按固定60Hz执行移动、闪避与阶段切换。</summary>
    /// <param name="movement">右下为正的移动向量。</param>
    /// <param name="dodgePressed">本步是否请求闪避。</param>
    /// <param name="previousPhasePressed">本步是否请求上一阶段。</param>
    /// <param name="nextPhasePressed">本步是否请求下一阶段。</param>
    void StepFixed(Vector2 movement, bool dodgePressed, bool previousPhasePressed = false, bool nextPhasePressed = false);
}

/// <summary>场景装配的战斗行为分发接口。</summary>
public interface IBattleControl
{
    /// <summary>处理一次物理步；不使用渲染时间。</summary>
    void Tick();
    /// <summary>进入战斗后等待原确认操作释放，避免连带闪避。</summary>
    void WaitForConfirmRelease();
}

/// <summary>分发语义操作，集中处理状态许可与确认释放，不认识设备或UI。</summary>
public sealed class BattleInputController : IBattleControl
{
    // 输入源和行为目标由场景装配；释放门闩仅属于当前控制器。
    private readonly IBattleInputSource _source;
    private readonly IBattleActions _target;
    private bool _waitForRelease;
    /// <summary>连接输入源与本场战斗。</summary>
    /// <param name="source">当前操作来源。</param>
    /// <param name="target">本场行为入口。</param>
    public BattleInputController(IBattleInputSource source, IBattleActions target) { _source = source; _target = target; }
    /// <summary>开启确认释放隔离，不改变战斗状态。</summary>
    public void WaitForConfirmRelease() => _waitForRelease = true;
    /// <summary>先处理释放，再按当前战斗状态分发一个输入快照。</summary>
    public void Tick()
    {
        if (_target.IsStopped) return;
        // 一个物理步只读取一次，重开步不继续推进新战斗。
        var frame = _source.Read();
        if (_waitForRelease && !frame.DodgeHeld) _waitForRelease = false;
        if (_target.State != BattleState.Running)
        {
            if (frame.RestartPressed) _target.Restart();
            return;
        }
        _target.StepFixed(frame.Movement, !_waitForRelease && frame.DodgePressed,
            frame.PreviousPhasePressed, frame.NextPhasePressed);
    }
}
