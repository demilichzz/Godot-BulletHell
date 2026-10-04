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
        if (context.BossData is null) throw new ArgumentException("战斗场景需要 Boss 配置。");
        View = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        View.BossData = context.BossData;
        View.AIConfig = context.AIConfig;
        AddChild(View);
        if (!View.Battle.IsInitialized) throw new InvalidOperationException("战斗初始化未完成。");
        View.Battle.WaitForConfirmRelease();
        AddChild(new MenuInputRouter
        {
            Target = new BattleMenuController(() => IsActive, () => Context.Game.RequestStage(GameManager.SelectStageId))
        });
    }
    /// <summary>停止模拟并清理弹幕，节点随 Stage 整体销毁。</summary>
    protected override void OnExit() => View?.Battle.StopBattle();
}
