using Godot;
using System;

/// <summary>只在独立编辑器进程使用的预览战斗，不改变正式场景和业务源码。</summary>
public partial class EditorPreview : Node2D
{
    // 预览通过正式数据阶段运行，不注册额外代码阶段。
    private BattleManager? _battle;
    /// <summary>当前正式Boss实例，用于阶段和血池预览。</summary>
    public BossController? Boss => _battle?.Boss;
    /// <summary>当前预览是否已完成全部阶段。</summary>
    public bool Victory => _battle?.State == BattleState.Victory;
    /// <summary>当前预览发射器，未启动时为空。</summary>
    public VBulletEmitter? Emitter { get; private set; }
    /// <summary>当前预览运行秒数。</summary>
    public double Elapsed => _battle?.Elapsed ?? 0;
    /// <summary>当前弹幕数量。</summary>
    public int BulletCount => _battle?.Bullets.ActiveBullets.Count ?? 0;
    /// <summary>是否简化普通子弹为统一圆点；激光保留原始几何。</summary>
    public bool Simple { get; set; } = true;
    /// <summary>创建完整初始战斗状态，固定种子由BattleManager设置为0。</summary>
    /// <param name="json">已经校验的Emitter JSON。</param>
    public void Start(string json)
    {
        // 先解析，失败时仍保留上一份有效预览。
        var emitter = VBulletEmitter.FromJson(json, "预览");
        Stop();
        try
        {
            // 使用静止的通用阶段；画布另外绘制锚点，Boss贴图保持隐藏。
            var data = BossData.FromJson("""
                {"Core":{"Id":"EmitterPreview","DisplayName":"预览锚点","TexturePath":"res://Assets/Units/Boss_01.png",
                 "MaxHp":300,"SpawnPosition":{"X":640,"Y":250}},
                 "Phases":[{"Name":"弹幕编辑预览","Hp":300,"DurationMs":null,"EndCondition":"Health","Emitters":[],
                 "Movement":{"Type":"Center","Target":{"X":640,"Y":250}}}]}
                """);
            _battle = new BattleManager(); AddChild(_battle);
            _battle.Initialize(this, data);
            _battle.SetPhysicsProcess(false);
            _battle.Player.Attack.Stop();
            _battle.Boss.Visible = false; _battle.Player.Visible = false;
            Emitter = emitter;
            emitter.Start(_battle.Boss, _battle.Bullets);
            QueueRedraw();
        }
        catch { Stop(); throw; }
    }
    /// <summary>以完整Boss数据创建正式战斗预览，停止玩家自动攻击以便检查阶段。</summary>
    /// <param name="json">当前Boss JSON快照。</param>
    /// <param name="loadEmitter">可选冻结的编辑会话资源入口。</param>
    public void StartBoss(string json, Func<string, VBulletEmitter>? loadEmitter = null)
    {
        // 先校验，再替换旧场景；运行实例完全重新建立。
        var data = BossData.FromJson(json, "Boss预览", loadEmitter);
        Stop();
        try
        {
            _battle = new BattleManager();
            AddChild(_battle);
            _battle.Initialize(this, data);
            _battle.SetPhysicsProcess(false);
            _battle.Player.Attack.Stop();
            _battle.Player.Visible = false;
            QueueRedraw();
        }
        catch { Stop(); throw; }
    }
    /// <summary>推进唯一正式60Hz步，不使用界面计时计算战斗时间。</summary>
    public void Advance()
    {
        _battle?.StepFixed(Vector2.Zero, false);
        RefreshDisplay();
    }
    /// <summary>切换普通子弹显示，不改动碰撞、寿命、随机与容量。</summary>
    public void RefreshDisplay()
    {
        if (_battle is null) return;
        // 仅操作显示属性；激光的预警、展开和曲线路径由原实现绘制。
        foreach (var bullet in _battle.Bullets.ActiveBullets) bullet.Visible = bullet is VLaser || !Simple;
        QueueRedraw();
    }
    /// <summary>释放本次预览全部战斗对象及全局绑定。</summary>
    public void Stop()
    {
        if (_battle is not null) { _battle.StopBattle(); _battle = null; }
        // 移除节点后再延迟释放，避免新旧Boss共用同一场景父节点。
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        Emitter = null; QueueRedraw();
    }
    /// <summary>离开编辑器时停止预览。</summary>
    public override void _ExitTree() => Stop();
    /// <summary>绘制轻量普通子弹与角色锚点，全部使用逻辑世界坐标。</summary>
    public override void _Draw()
    {
        DrawRect(BattleConfig.Bounds, new Color("101c2b"));
        DrawCircle(BattleConfig.ArenaCenter, BattleConfig.ArenaRadius, new Color("15283a"));
        DrawArc(BattleConfig.ArenaCenter, BattleConfig.ArenaRadius, 0, Mathf.Tau, 96, new Color("42637b"), 2);
        if (_battle is null) return;
        DrawCircle(_battle.Boss.GlobalPosition, 13, new Color("ffba71"));
        DrawCircle(_battle.Player.GlobalPosition, 7, new Color("74e9d4"));
        if (!Simple) return;
        // 当前活动弹幕，仅读取状态或调整显示。
        foreach (var bullet in _battle.Bullets.ActiveBullets)
            if (bullet is not VLaser && bullet.IsAlive) DrawCircle(bullet.WorldPosition, Math.Max(2, (float)bullet.Radius), new Color("91d7ff"));
    }
}
