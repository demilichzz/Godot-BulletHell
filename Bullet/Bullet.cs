using Godot;
using System;

/// <summary>保存弹幕状态与外观，由管理器统一推进和释放。</summary>
public partial class Bullet : Node2D, IVTimelineOwner
{
    /// <summary>出生时启动、释放时取消的逻辑时间线。</summary>
    public VTimeline? Timeline { get; internal set; }
    /// <summary>所属队列；非队列弹幕为空。</summary>
    internal BulletQueue? Queue;
    /// <summary>初始位置，登记前为全局坐标，登记后为管理器局部逻辑像素。</summary>
    public Vector2 SpawnPosition { get; internal set; }
    /// <summary>当前运动角度，单位为弧度，0向右、π/2向下。</summary>
    public double AngleRadians { get; private set; }
    /// <summary>当前有符号速度，负值沿角度的反方向运动，单位为逻辑像素/秒。</summary>
    public double Speed { get; private set; }
    /// <summary>存活上限，单位为秒。</summary>
    public double LifetimeSeconds { get; private set; }
    // 当前速度向量，单位为管理器局部逻辑像素/秒。
    private Vector2 _velocity;
    /// <summary>当前局部速度向量，逻辑像素/秒；通过方向和有符号速度修改。</summary>
    public Vector2 Velocity => _velocity;
    /// <summary>已经存活的秒数。</summary>
    public double Age { get; private set; }
    /// <summary>是否到达寿命上限。</summary>
    public bool Expired => Age + 1e-9 >= LifetimeSeconds;
    /// <summary>所属阵营，默认敌方。</summary>
    public BulletTeam Team { get; private set; }
    /// <summary>伤害点数。</summary>
    public int Damage { get; private set; }
    /// <summary>碰撞半径，单位为逻辑像素。</summary>
    public double Radius { get; private set; }
    /// <summary>当前运动策略，默认直线。</summary>
    public BulletBehavior Behavior { get; private set; } = new StraightBehavior();
    // 所属发射批次，由管理器登记和注销。
    internal BulletEmitter? Emitter;
    // 按初始化数据配置的居中贴图，仅修改外观变换。
    private readonly Sprite2D _sprite = new() { Name = "Sprite" };
    /// <summary>初始化时复制的圆点颜色，不跟随外部数据修改。</summary>
    public Color CircleColor { get; private set; }
    /// <summary>在创建节点前校验完整参数与贴图资源。</summary>
    /// <param name="settings">出生参数，位置为全局逻辑像素。</param>
    internal static void Validate(BulletDefaultSet settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Position.IsFinite() || !double.IsFinite(settings.AngleRadians) || !double.IsFinite(settings.Speed) || Math.Abs(settings.Speed) > float.MaxValue
            || !double.IsFinite(settings.LifetimeSeconds) || settings.LifetimeSeconds <= 0 || settings.Damage <= 0
            || !double.IsFinite(settings.Radius) || settings.Radius <= 0 || settings.Radius > float.MaxValue || (float)settings.Radius == 0 || settings.Hframes <= 0 || settings.Vframes <= 0
            || (long)settings.Hframes * settings.Vframes > int.MaxValue || settings.ColorIndex < 0 || settings.ColorIndex >= (long)settings.Hframes * settings.Vframes
            || !double.IsFinite(settings.VisualScale) || settings.VisualScale <= 0 || settings.VisualScale > float.MaxValue || (float)settings.VisualScale == 0 || !Enum.IsDefined(settings.Team) || settings.Behavior is null
            || !ValidColorComponent(settings.CircleColor.R) || !ValidColorComponent(settings.CircleColor.G)
            || !ValidColorComponent(settings.CircleColor.B) || !ValidColorComponent(settings.CircleColor.A))
            throw new ArgumentOutOfRangeException(nameof(settings), "子弹参数无效。");
        if (settings.UseSprite && (string.IsNullOrWhiteSpace(settings.TexturePath) || !ResourceLoader.Exists(settings.TexturePath, "Texture2D")
            || ResourceLoader.Load(settings.TexturePath) is not Texture2D))
            throw new ArgumentException("贴图模式需要有效Texture2D资源。", nameof(settings));
    }
    /// <summary>判断单个颜色分量是否位于有效范围。</summary>
    /// <param name="value">待检查的颜色分量，范围为0至1。</param>
    /// <returns>分量有效时为真。</returns>
    private static bool ValidColorComponent(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
    /// <summary>入树前按完整参数初始化，初始位置稍后由管理器转换。</summary>
    /// <param name="settings">完整配置，位置为全局逻辑像素。</param>
    internal void Configure(BulletDefaultSet settings)
    {
        if (IsInsideTree()) throw new InvalidOperationException("子弹须在入树前初始化。");
        SpawnPosition = Position = settings.Position;
        LifetimeSeconds = settings.LifetimeSeconds;
        Team = settings.Team;
        Damage = settings.Damage;
        Radius = settings.Radius;
        Behavior = settings.Behavior;
        Age = 0;
        _sprite.Texture = settings.UseSprite ? GD.Load<Texture2D>(settings.TexturePath!) : null;
        _sprite.Hframes = settings.Hframes;
        _sprite.Vframes = settings.Vframes;
        _sprite.Frame = settings.ColorIndex;
        _sprite.Scale = Vector2.One * (float)settings.VisualScale;
        _sprite.Visible = settings.UseSprite;
        CircleColor = settings.CircleColor;
        AngleRadians = settings.AngleRadians;
        SetSpeed(settings.Speed);
    }
    /// <summary>改变运动方向并保留速度，同步贴图朝向。</summary>
    /// <param name="angleRadians">有限角度，单位为弧度，0向右、π/2向下。</param>
    public void SetDirection(double angleRadians)
    {
        if (!double.IsFinite(angleRadians)) throw new ArgumentOutOfRangeException(nameof(angleRadians));
        AngleRadians = VMath.StandardizationAngle(angleRadians);
        _velocity = VMath.PolarMove(Vector2.Zero, AngleRadians, Speed);
        _sprite.Rotation = VMath.StandardizationAngleFloat(AngleRadians);
    }
    /// <summary>改变速度并保留方向，同步速度向量。</summary>
    /// <param name="speed">有符号有限速度，单位为逻辑像素/秒；负值反向，0停止。</param>
    public void SetSpeed(double speed)
    {
        if (!double.IsFinite(speed) || Math.Abs(speed) > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
        SetDirection(AngleRadians);
    }
    /// <summary>由管理器推进运动与年龄，销毁由管理器负责。</summary>
    /// <param name="delta">经过的非负秒数。</param>
    internal void Advance(double delta)
    {
        Behavior.Advance(this, delta);
        Timeline?.AdvanceUnits(VTimeline.SecondsToUnits(delta));
        Age = Timeline is null ? Age + delta : Timeline.ElapsedUnits / (double)VTimerProcessor.UnitsPerSecond;
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
        if (!_sprite.Visible) DrawCircle(Vector2.Zero, (float)Radius, CircleColor);
    }
}
