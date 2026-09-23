using Godot;
using System;
using System.Collections.Generic;

/// <summary>常驻游戏根管理器，通过注册表延迟替换唯一活动 Stage。</summary>
public partial class GameManager : Node
{
    // 内置场景注册标识；新增场景可使用自己的非空字符串。
    public const string SelectStageId = "boss_select", BattleStageId = "battle";
    /// <summary>当前活动场景，启动及切换失败时可为空。</summary>
    public Stage? CurrentStage { get; private set; }
    /// <summary>当前 Boss 目录，可在入树前注入测试目录。</summary>
    [Export] public BossCatalog Catalog { get; set; } = null!;
    /// <summary>返回选择界面时恢复的 Boss 标识。</summary>
    public string SelectedBossId { get; private set; } = "";
    /// <summary>是否已有一个等待执行的切换请求。</summary>
    public bool IsTransitioning { get; private set; }
    /// <summary>当前唯一 Stage 的宿主节点。</summary>
    public Node StageHost { get; } = new() { Name = "StageHost" };
    /// <summary>切换失败的最近说明，成功后清空。</summary>
    public string LastError { get; private set; } = "";
    // 场景标识到实例工厂的注册表，避免不断扩大的类型判断。
    private readonly Dictionary<string, Func<Stage>> _factories = new(StringComparer.Ordinal);
    /// <summary>初始化目录与内置场景，启动时进入选择界面。</summary>
    public override void _Ready()
    {
        GameInput.EnsureBindings();
        AddChild(StageHost);
        Catalog ??= GD.Load<BossCatalog>("res://Data/BossCatalog.tres");
        Catalog.Validate();
        if (Catalog.Entries.Count > 0) SelectedBossId = Catalog.Entries[0].Id;
        RegisterStage(SelectStageId, () => GD.Load<PackedScene>("res://Stage/BossSelectStage.tscn").Instantiate<BossSelectStage>());
        RegisterStage(BattleStageId, () => GD.Load<PackedScene>("res://Stage/BattleStage.tscn").Instantiate<BattleStage>());
        RequestStage(SelectStageId);
    }
    /// <summary>注册扩展场景，只需提供唯一标识及新实例工厂。</summary>
    /// <param name="id">非空且唯一的场景标识。</param>
    /// <param name="create">每次返回尚未入树的全新 Stage。</param>
    public void RegisterStage(string id, Func<Stage> create)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("场景标识不可为空。", nameof(id));
        _factories.Add(id, create ?? throw new ArgumentNullException(nameof(create)));
    }
    /// <summary>记录选择，不切换场景。</summary>
    /// <param name="bossId">目录中存在的 Boss 标识。</param>
    public void RememberSelection(string bossId)
    {
        if (Catalog.Find(bossId) is null) throw new ArgumentException("Boss 不存在。", nameof(bossId));
        SelectedBossId = bossId;
    }
    /// <summary>检查目标后排队切换，重复请求在当前切换完成前被拒绝。</summary>
    /// <param name="id">已注册的目标场景标识。</param>
    /// <param name="bossId">战斗使用的 Boss 标识；默认空时使用上次选择。</param>
    /// <returns>是否成功接受切换请求。</returns>
    public bool RequestStage(string id, string? bossId = null)
    {
        if (IsTransitioning || !IsInsideTree()) return false;
        if (!_factories.TryGetValue(id, out var create)) { LastError = $"未注册的场景：{id}"; return false; }
        // 战斗参数在退出旧场景之前验证。
        var boss = id == BattleStageId ? Catalog.Find(bossId ?? SelectedBossId) : null;
        if (id == BattleStageId && boss is null) { LastError = "请选择有效 Boss。"; return false; }
        IsTransitioning = true;
        Callable.From(() => ChangeStage(id, create, new StageContext(this, boss))).CallDeferred();
        return true;
    }
    /// <summary>安全时机准备新节点，再替换旧场景；准备失败时保留旧场景。</summary>
    /// <param name="id">目标注册标识。</param>
    /// <param name="create">生成目标的新实例工厂。</param>
    /// <param name="context">已经验证的进入参数。</param>
    private void ChangeStage(string id, Func<Stage> create, StageContext context)
    {
        // 先保留旧实例以便目标进入失败时恢复可用界面。
        Stage? next = null;
        // 只释放本次工厂创建且尚未挂载的节点，拒绝误删已有场景。
        var ownsNext = false;
        var previous = CurrentStage;
        var previousBattle = GlobalEvent.CaptureCurrent();
        try
        {
            if (!IsInsideTree()) return;
            next = create();
            ownsNext = next is not null && next.GetParent() is null;
            if (next is null || next.StageId != id || next.GetParent() is not null) throw new InvalidOperationException("场景工厂返回了不匹配或已挂载的实例。");
            if (previous is not null) previous.ProcessMode = ProcessModeEnum.Disabled;
            StageHost.AddChild(next);
            next.Enter(context);
            previous?.Exit();
            if (previous is not null) { StageHost.RemoveChild(previous); previous.QueueFree(); }
            CurrentStage = next;
            if (context.BossData is not null) SelectedBossId = context.BossData.Id;
            LastError = "";
        }
        catch (Exception error)
        {
            LastError = error.Message;
            if (ownsNext && next is not null && GodotObject.IsInstanceValid(next))
            {
                next.Exit();
                if (next.GetParent() == StageHost) StageHost.RemoveChild(next);
                next.QueueFree();
            }
            if (GlobalEvent.IsValidBattle(previousBattle)) GlobalEvent.BindCurrent(previousBattle!);
            else if (previousBattle is not null) GlobalEvent.ClearCurrent(previousBattle);
            if (previous is not null) previous.ProcessMode = ProcessModeEnum.Inherit;
            GD.PushError($"场景切换失败：{error.Message}");
        }
        finally { IsTransitioning = false; }
    }
    /// <summary>应用退出时停止当前场景。</summary>
    public override void _ExitTree() => CurrentStage?.Exit();
}


