using Godot;
using System;

/// <summary>在VNode运动与时间线基础上增加弹幕显示、伤害和碰撞参数。</summary>
public partial class Bullet : VNode
{
    /// <summary>出生时复制的阵营。</summary>
    public BulletTeam Team { get; private set; }
    /// <summary>出生时复制的伤害点数。</summary>
    public int Damage { get; private set; }
    /// <summary>碰撞半径，逻辑像素。</summary>
    public double Radius { get; private set; }
    /// <summary>圆点模式的显示颜色。</summary>
    public Color CircleColor { get; private set; }
    // 只有实际子弹才创建显示节点。
    private readonly Sprite2D _sprite = new() { Name = "Sprite" };
    /// <summary>在创建节点前校验完整参数与贴图资源。</summary>
    /// <param name="settings">出生参数，位置为全局逻辑像素。</param>
    internal static void Validate(BulletDefaultSet settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Position.IsFinite() || !double.IsFinite(settings.AngleRadians) || !double.IsFinite(settings.Speed) || Math.Abs(settings.Speed) > float.MaxValue
            || !double.IsFinite(settings.AAngle) || !double.IsFinite(settings.ASpeed) || Math.Abs(settings.ASpeed) > float.MaxValue
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
    /// <summary>入树前初始化显示、伤害以及继承的公共运动参数。</summary>
    /// <param name="settings">完整出生参数，位置为全局逻辑像素。</param>
    internal void Configure(BulletDefaultSet settings)
    {
        ConfigureMotion(settings.Position, new BulletMoveAttribute
        {
            Angle = settings.AngleRadians,
            Speed = settings.Speed,
            AAngle = settings.AAngle,
            ASpeed = settings.ASpeed
        }, settings.LifetimeSeconds, settings.AAngleIsSameAsAngle, settings.Behavior);
        Team = settings.Team;
        Damage = settings.Damage;
        Radius = settings.Radius;
        _sprite.Texture = settings.UseSprite ? GD.Load<Texture2D>(settings.TexturePath!) : null;
        _sprite.Hframes = settings.Hframes;
        _sprite.Vframes = settings.Vframes;
        _sprite.Frame = settings.ColorIndex;
        _sprite.Scale = Vector2.One * (float)settings.VisualScale;
        _sprite.Visible = settings.UseSprite;
        CircleColor = settings.CircleColor;
    }

    /// <summary>将实际运动方向同步到贴图，保持公共状态与显示解耦。</summary>
    /// <param name="angle">实际方向弧度，0向右且顺时针为正。</param>
    protected override void OnDirectionChanged(double angle)
        => _sprite.Rotation = VMath.StandardizationAngleFloat(angle);

    /// <summary>添加显示节点并使用基类的统一更新方式。</summary>
    public override void _Ready()
    {
        base._Ready();
        _sprite.Centered = true;
        _sprite.TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_sprite);
    }

    /// <summary>绘制代码入口生成的圆点弹幕。</summary>
    public override void _Draw()
    {
        if (!_sprite.Visible) DrawCircle(Vector2.Zero, (float)Radius, CircleColor);
    }
}
