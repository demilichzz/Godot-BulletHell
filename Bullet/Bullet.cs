using Godot;
using System;

/// <summary>保存弹幕状态与外观，由管理器统一推进和释放。</summary>
public partial class Bullet : Node2D
{
    /// <summary>初始位置，登记前为全局坐标，登记后为管理器局部逻辑像素。</summary>
    public Vector2 SpawnPosition { get; internal set; }
    /// <summary>当前运动角度，单位为弧度，0向右、π/2向下。</summary>
    public float AngleRadians { get; private set; }
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
            if (!value.IsZeroApprox()) AngleRadians = VMath.StandardizationAngleFloat(VMath.GetAngleBetween2Points(Vector2.Zero, value));
            _sprite.Rotation = AngleRadians;
        }
    }
    /// <summary>已经存活的秒数。</summary>
    public double Age { get; private set; }
    /// <summary>是否到达寿命上限。</summary>
    public bool Expired => Age + 1e-9 >= LifetimeSeconds;
    /// <summary>所属阵营，默认敌方。</summary>
    public BulletTeam Team { get; private set; }
    /// <summary>伤害点数。</summary>
    public int Damage { get; private set; }
    /// <summary>碰撞半径，单位为逻辑像素。</summary>
    public float Radius { get; private set; }
    /// <summary>当前运动策略，默认直线。</summary>
    public BulletBehavior Behavior { get; private set; } = new StraightBehavior();
    // 所属发射批次，由管理器登记和注销。
    internal BulletEmitter? Emitter;
    // 按初始化数据配置的居中贴图，仅修改外观变换。
    private readonly Sprite2D _sprite = new() { Name = "Sprite" };
    /// <summary>初始化时复制的圆点颜色，不跟随外部数据修改。</summary>
    public Color CircleColor { get; private set; }
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
        _sprite.Texture = data.UseSprite ? GD.Load<Texture2D>(data.TexturePath!) : null;
        _sprite.Hframes = data.Hframes;
        _sprite.Vframes = data.Vframes;
        _sprite.Frame = data.ColorIndex;
        _sprite.Scale = Vector2.One * data.VisualScale;
        _sprite.Visible = data.UseSprite;
        CircleColor = data.CircleColor;
        AngleRadians = data.AngleRadians;
        SetSpeed(data.Speed);
    }
    /// <summary>改变运动方向并保留速度，同步贴图朝向。</summary>
    /// <param name="angleRadians">有限角度，单位为弧度，0向右、π/2向下。</param>
    public void SetDirection(float angleRadians)
    {
        if (!float.IsFinite(angleRadians)) throw new ArgumentOutOfRangeException(nameof(angleRadians));
        AngleRadians = VMath.StandardizationAngleFloat(angleRadians);
        _velocity = VMath.PolarMove(Vector2.Zero, AngleRadians, Speed);
        _sprite.Rotation = AngleRadians;
    }
    /// <summary>改变速度并保留方向，同步速度向量。</summary>
    /// <param name="speed">非负有限速度，单位为逻辑像素/秒，0表示停止。</param>
    public void SetSpeed(float speed)
    {
        if (!float.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
        SetDirection(AngleRadians);
    }
    /// <summary>由管理器推进运动与年龄，销毁由管理器负责。</summary>
    /// <param name="delta">经过的非负秒数。</param>
    internal void Advance(double delta)
    {
        Behavior.Advance(this, delta);
        Age += delta;
    }
    /// <summary>装配已配置的居中贴图并禁止子弹自行物理更新。</summary>
    public override void _Ready()
    {
        _sprite.Centered = true;
        _sprite.TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_sprite);
        SetPhysicsProcess(false);
    }
    /// <summary>绘制未采用图集的子弹圆点。</summary>
    public override void _Draw()
    {
        if (!_sprite.Visible) DrawCircle(Vector2.Zero, Radius, CircleColor);
    }
}
