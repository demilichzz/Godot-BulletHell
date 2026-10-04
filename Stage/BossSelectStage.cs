using Godot;
using System.Linq;

/// <summary>Boss 选择场景，负责目录展示及发出战斗切换请求。</summary>
public partial class BossSelectStage : Stage
{
    /// <summary>选择场景注册标识。</summary>
    public override string StageId => GameManager.SelectStageId;
    /// <summary>当前选择界面，进入后创建。</summary>
    public BossSelectUI UI { get; private set; } = null!;
    // 控制器只在当前场景内订阅视图，退出时解除。
    private BossSelectionController? _selection;
    /// <summary>创建界面并恢复上次选择。</summary>
    /// <param name="context">包含管理器和共享 Boss 目录的参数。</param>
    protected override void OnEnter(StageContext context)
    {
        UI = new BossSelectUI { Name = "BossSelectUI" };
        AddChild(UI);
        UI.Initialize(context.Game.Catalog, context.Game.AIEnabled);
        _selection = new BossSelectionController(context.Game.Catalog.Entries.Select(data => data.Id), BossSelectUI.Columns, UI);
        _selection.SelectionChanged += context.Game.RememberSelection;
        _selection.Confirmed += StartBattle;
        _selection.AISelectionChanged += context.Game.RememberAISelection;
        AddChild(new MenuInputRouter { Target = _selection });
        _selection.Restore(context.Game.SelectedBossId);
    }
    /// <summary>提交选中的 Boss，实际切换由管理器延迟执行。</summary>
    /// <param name="bossId">目录中的有效 Boss 标识。</param>
    private void StartBattle(string bossId) => Context.Game.RequestStage(GameManager.BattleStageId, bossId);
    /// <summary>解除跨场景事件订阅。</summary>
    protected override void OnExit()
    {
        if (_selection is null) return;
        _selection.SelectionChanged -= Context.Game.RememberSelection;
        _selection.Confirmed -= StartBattle;
        _selection.AISelectionChanged -= Context.Game.RememberAISelection;
        _selection.Dispose();
    }
}
