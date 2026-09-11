using Godot;
using System;

/// <summary>保存弹幕状态与外观，由管理器统一推进和释放。</summary>
public partial class Bullet : Node2D
{
    /// <summary>初始位置，登记前为全局坐标，登记后为管理器局部逻辑像素。</summary>
    public Vector2 SpawnPosition { get; internal set; }
    /// <summary>当前运动角度，单位为度，0向右、90向下。</summary>
    public float AngleDegrees { get; private set; }
    /// <summary>当前非负速度，单位为逻辑像素/秒。</summary>
    public float Speed { get; private set; }
    /// <summary>存活上限，单位为秒。</summary>
    public float LifetimeSeconds { get; private set; }
    // 当前速度向量，单位为管理器局部逻辑像素/秒。
    private Vector2 _velocity;
    /// <summary>当前局部速度，逻辑像素/秒，修改时同步速度、方向及贴图朝向。</summary>
    public Vector2 Velocity
    {
        get => _velocity;
        set
        {
            if (!value.IsFinite()) throw new ArgumentOutOfRangeException(nameof(value));
            _velocity = value;
            Speed = value.Length();
            if (!value.IsZeroApprox()) AngleDegrees = Mathf.RadToDeg(value.Angle());
            _sprite.RotationDegrees = AngleDegrees;
        }
    }
    /// <summary>已经存活的秒数。</summary>
    public double Age { get; private set; }
    /// <summary>是否到达寿命上限。</summary>
    public bool Expired => Age + 1e-9 >= LifetimeSeconds;
    /// <summary>所属阵营，默认敌方。</summary>
    public BulletTeam Team { get; private set; }
    /// <summary>伤害点数。</summary>
    public int Damage { get; private set; } = BattleConfig.Damage;
    /// <summary>碰撞半径，单位为逻辑像素。</summary>
    public float Radius { get; private set; } = BattleConfig.EnemyBulletRadius;
    /// <summary>当前运动策略，默认直线。</summary>
    public BulletBehavior Behavior { get; private set; } = new StraightBehavior();
    // 所属发射批次，由管理器登记和注销。
    internal BulletEmitter? Emitter;
    // 横向十格图集的居中贴图，仅修改外观变换。
    private readonly Sprite2D _sprite = new() { Name = "Sprite", Hframes = 10 };
    /// <summary>入树前按完整参数初始化，初始位置稍后由管理器转换。</summary>
    /// <param name="data">完整配置，位置为全局逻辑像素。</param>
    public void Configure(BulletSpawnData data)
    {
        if (IsInsideTree()) throw new InvalidOperationException("子弹须在入树前初始化。");
        data.Validate();
        SpawnPosition = Position = data.Position;
        LifetimeSeconds = data.LifetimeSeconds;
        Team = data.Team;
        Damage = data.Damage;
        Radius = data.Radius;
        Behavior = data.Behavior;
        Age = 0;
        _sprite.Frame = data.ColorIndex;
        _sprite.Scale = Vector2.One * data.VisualScale;
        _sprite.Visible = data.UseSprite;
        AngleDegrees = data.AngleDegrees;
        SetSpeed(data.Speed);
    }
    /// <summary>保留入树前的直线敌弹初始化入口，不启用独立物理更新。</summary>
    /// <param name="position">全局发射位置，单位为逻辑像素。</param>
    /// <param name="angleDegrees">角度，单位为度，0向右、90向下。</param>
    /// <param name="speed">非负速度，逻辑像素/秒。</param>
    /// <param name="lifetimeSeconds">正数寿命，单位为秒。</param>
    /// <param name="colorIndex">从左至右图集索引0～9，默认0。</param>
    /// <param name="visualScale">正数有限倍率，默认2。</param>
    public void Initialize(Vector2 position, float angleDegrees, float speed, float lifetimeSeconds, int colorIndex = 0, float visualScale = 2f)
        => Configure(new BulletSpawnData { Position = position, AngleDegrees = angleDegrees, Speed = speed,
            LifetimeSeconds = lifetimeSeconds, ColorIndex = colorIndex, VisualScale = visualScale });
    /// <summary>使用当前阵营默认参数初始化，须在入树前调用。</summary>
    /// <param name="position">全局发射位置，逻辑像素。</param>
    /// <param name="angle">角度，单位为度，0向右、90向下。</param>
    /// <param name="team">所属阵营。</param>
    public void ConfigureShot(Vector2 position, float angle, BulletTeam team)
        => Configure(BulletSpawnData.ForTeam(team, angle) with { Position = position });
    /// <summary>改变运动方向并保留速度，同步贴图朝向。</summary>
    /// <param name="angleDegrees">有限角度，单位为度，0向右、90向下。</param>
    public void SetDirection(float angleDegrees)
    {
        if (!float.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        AngleDegrees = angleDegrees;
        _velocity = Vector2.Right.Rotated(Mathf.DegToRad(angleDegrees)) * Speed;
        _sprite.RotationDegrees = angleDegrees;
    }
    /// <summary>改变速度并保留方向，同步速度向量。</summary>
    /// <param name="speed">非负有限速度，单位为逻辑像素/秒，0表示停止。</param>
    public void SetSpeed(float speed)
    {
        if (!float.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
        SetDirection(AngleDegrees);
    }
    /// <summary>由管理器推进运动与年龄，销毁由管理器负责。</summary>
    /// <param name="delta">经过的非负秒数。</param>
    internal void Advance(double delta)
    {
        Behavior.Advance(this, delta);
        Age += delta;
    }
    /// <summary>加载居中贴图并禁止子弹自行物理更新。</summary>
    public override void _Ready()
    {
        _sprite.Texture = GD.Load<Texture2D>("res://Assets/Sprite_02.png");
        _sprite.Centered = true;
        _sprite.TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_sprite);
        SetPhysicsProcess(false);
    }
    /// <summary>绘制未采用图集的子弹圆点。</summary>
    public override void _Draw()
    {
        if (!_sprite.Visible) DrawCircle(Vector2.Zero, Radius, Colors.Cyan);
    }
}
