using Godot;
using System;

/// <summary>通用 Boss 战斗场景，装配战场并在离场时停止战斗。</summary>
public partial class BattleStage : Stage
{
    /// <summary>战斗场景注册标识。</summary>
    public override string StageId => GameManager.BattleStageId;
    /// <summary>本次进入创建的战场视图。</summary>
    public Main View { get; private set; } = null!;
    /// <summary>按选中的 Boss 配置创建通用战场。</summary>
    /// <param name="context">必须携带有效 Boss 配置的进入参数。</param>
    protected override void OnEnter(StageContext context)
    {
        if (context.Boss is null) throw new ArgumentException("战斗场景需要 Boss 配置。");
        View = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        View.BossData = context.Boss;
        AddChild(View);
        if (!View.Battle.IsInitialized) throw new InvalidOperationException("战斗初始化未完成。");
        View.Battle.WaitForConfirmRelease();
    }
    /// <summary>停止模拟并清理弹幕，节点随 Stage 整体销毁。</summary>
    protected override void OnExit() => View?.Battle.StopBattle();
    /// <summary>Esc 返回选择场景，战斗状态不影响返回操作。</summary>
    /// <param name="inputEvent">当前未处理的键盘事件。</param>
    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (!IsActive || !inputEvent.IsActionPressed("stage_back")) return;
        GetViewport().SetInputAsHandled();
        Context.Game.RequestStage(GameManager.SelectStageId);
    }
}
