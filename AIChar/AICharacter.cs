using Godot;
using System;
using System.Collections.Generic;

/// <summary>复用玩家能力的独立陪练角色；输入由策略提供，受击不消耗敌弹。</summary>
public partial class AICharacter : PlayerController
{
    // 感知矩形半边长，单位为逻辑像素。
    private const float SenseHalfSize = 100;
    // 复用的附近弹幕缓存和只读包装，避免每步分配。
    private readonly List<VBullet> _nearby = new(BattleConfig.MaxBullets);
    private readonly IReadOnlyList<VBullet> _nearbyView;
    // 本场专属策略、随机流和攻击开关。
    private IAIStrategy _strategy = null!;
    private VRandomStream _random = null!;
    private bool _attacking;
    /// <summary>本场有效受伤次数，单次伤害数值不影响计数。</summary>
    public long HitCount { get; private set; }
    /// <summary>最近一个逻辑步采用的操作，用于显示或确定性验证。</summary>
    public AIIntent LastIntent { get; private set; }

    /// <summary>建立一次性只读缓存包装。</summary>
    public AICharacter() => _nearbyView = _nearby.AsReadOnly();

    /// <summary>添加角色上方标识，不注册独立物理更新。</summary>
    public override void _Ready()
    {
        // 标识跟随角色位置及受击闪烁，忽略鼠标。
        var label = new Label
        {
            Name = "AILabel", Text = "AI", Position = new Vector2(-20, -36), Size = new Vector2(40, 24),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", Colors.LimeGreen);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        AddChild(label);
    }

    /// <summary>创建本场策略、独立随机流和玩家能力时间线，默认不启动攻击。</summary>
    /// <param name="config">已启用的角色配置，种子为32位整数。</param>
    public void InitializeAI(AICharConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _strategy = config.StrategyFactory?.Invoke() ?? throw new ArgumentException("AI策略工厂必须返回独立有效实例。", nameof(config));
        _random = VMath.CreateRandomStream(config.Seed);
        StartTimeline();
        Dodge.Initialize(this);
        Health.Initialize(this);
    }

    /// <summary>采集局部信息并应用策略输入，移动由战斗管理器随后统一推进。</summary>
    /// <param name="seconds">当前固定逻辑步秒数，正式战斗为1/60。</param>
    public void Decide(double seconds)
    {
        GlobalEvent.CollectBulletsInRect(new Rect2(GlobalPosition - Vector2.One * SenseHalfSize,
            Vector2.One * (SenseHalfSize * 2)), _nearby, VBulletTeam.Enemy);
        // 只传入当前状态，不允许策略读取未来时间线或随机结果。
        var state = new AICharState
        {
            Position = GlobalPosition, LastMovement = LastIntent.Movement, StepSeconds = seconds,
            MoveSpeed = BattleConfig.MoveSpeed, Radius = BattleConfig.PlayerRadius
        };
        LastIntent = _strategy.Decide(in state, _nearbyView, _random);
        if (!LastIntent.Movement.IsFinite()) throw new InvalidOperationException("AI策略返回了非有限移动方向。");
        LastIntent = LastIntent with { Movement = LastIntent.Movement.LimitLength() };
        Movement.ReadDirection(LastIntent.Movement);
        if (LastIntent.DodgePressed) Dodge.TryStart(Movement.LastDirection);
        if (LastIntent.AttackEnabled != _attacking)
        {
            if (LastIntent.AttackEnabled) Attack.Initialize(this, GlobalEvent.GetBulletManager());
            else Attack.Stop();
            _attacking = LastIntent.AttackEnabled;
        }
    }

    /// <summary>沿用玩家伤害和无敌规则，仅在实际受伤时增加计数。</summary>
    /// <param name="damage">正整数伤害；非正数不产生受击。</param>
    public void TakeHit(int damage)
    {
        if (Health.TakeDamage(damage, Dodge.IsActive) && HitCount < long.MaxValue) HitCount++;
    }

    /// <summary>结束或离场时关闭攻击并释放查询缓存中的实体引用。</summary>
    public void StopAI()
    {
        Attack.Stop();
        _attacking = false;
        _nearby.Clear();
        LastIntent = default;
    }

    /// <summary>显示绿色角色及与玩家相同大小的白色判定点。</summary>
    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, 10, Dodge.IsActive ? Colors.Gold : Colors.LimeGreen);
        DrawCircle(Vector2.Zero, BattleConfig.PlayerRadius, Colors.White);
    }
}