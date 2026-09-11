using Godot;
using System;

/// <summary>保存弹幕状态与外观；受管理时由管理器统一更新。</summary>
public partial class Bullet : Node2D
{
	/// <summary>初始局部位置，单位为像素。</summary>
	public Vector2 SpawnPosition { get; private set; }
	/// <summary>发射角度（度），0向右、90向下。</summary>
	public float AngleDegrees { get; private set; }
	/// <summary>初始速度，单位为像素/秒。</summary>
	public float Speed { get; private set; }
	/// <summary>存活上限，单位为秒。</summary>
	public float LifetimeSeconds { get; private set; }
	/// <summary>当前局部速度，单位为像素/秒。</summary>
	public Vector2 Velocity { get; set; }
	/// <summary>已经存活的秒数。</summary>
	public double Age { get; private set; }
	/// <summary>是否到达寿命上限。</summary>
	public bool Expired => Age + 1e-9 >= LifetimeSeconds;
	/// <summary>所属阵营，默认敌方。</summary>
	public BulletTeam Team { get; private set; }
	/// <summary>伤害点数。</summary>
	public int Damage { get; private set; } = BattleConfig.Damage;
	/// <summary>碰撞半径，单位为像素。</summary>
	public float Radius { get; private set; } = BattleConfig.EnemyBulletRadius;
	/// <summary>当前运动策略，默认直线。</summary>
	public BulletBehavior Behavior { get; set; } = new StraightBehavior();
	// 横向十格图集的居中贴图，仅修改外观变换。
	private readonly Sprite2D _sprite = new() { Name = "Sprite", Hframes = 10 };
	/// <summary>保留旧接口，设置直线敌弹并重置年龄。</summary>
	/// <param name="position">父节点局部发射位置，单位为像素。</param>
	/// <param name="angleDegrees">角度（度），0向右、90向下。</param>
	/// <param name="speed">速度，单位为像素/秒。</param>
	/// <param name="lifetimeSeconds">寿命，单位为秒。</param>
	/// <param name="colorIndex">从左到右图集索引0～9，默认0。</param>
	/// <param name="visualScale">正数有限贴图倍率，默认2。</param>
	public void Initialize(Vector2 position, float angleDegrees, float speed, float lifetimeSeconds, int colorIndex = 0, float visualScale = 2f)
	{
		if (colorIndex < 0 || colorIndex > 9) throw new ArgumentOutOfRangeException(nameof(colorIndex));
		if (!float.IsFinite(visualScale) || visualScale <= 0) throw new ArgumentOutOfRangeException(nameof(visualScale));
		_sprite.Frame = colorIndex;
		_sprite.Scale = Vector2.One * visualScale;
		_sprite.RotationDegrees = angleDegrees;
		_sprite.Visible = true;
		SpawnPosition = Position = position;
		AngleDegrees = angleDegrees;
		Speed = speed;
		LifetimeSeconds = lifetimeSeconds;
		Velocity = Vector2.Right.Rotated(Mathf.DegToRad(angleDegrees)) * speed;
		Age = 0;
		Team = BulletTeam.Enemy;
		Radius = BattleConfig.EnemyBulletRadius;
		Behavior = new StraightBehavior();
	}
	/// <summary>使用集中参数配置指定阵营的子弹。</summary>
	/// <param name="position">局部发射位置，单位为像素。</param>
	/// <param name="angle">屏幕角度（度），0向右、90向下。</param>
	/// <param name="team">攻击方阵营。</param>
	public void ConfigureShot(Vector2 position, float angle, BulletTeam team)
	{
		Initialize(position, angle, team == BulletTeam.Enemy ? BattleConfig.EnemySpeed : BattleConfig.ShotSpeed,
			team == BulletTeam.Enemy ? BattleConfig.EnemyLifetime : BattleConfig.ShotLifetime,
			BattleConfig.EnemyColor, BattleConfig.EnemyScale);
		Team = team;
		Radius = team == BulletTeam.Enemy ? BattleConfig.EnemyBulletRadius : BattleConfig.PlayerBulletRadius;
		_sprite.Visible = team == BulletTeam.Enemy;
		QueueRedraw();
	}
	/// <summary>推进运动与年龄，销毁由调用者负责。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	public void Advance(double delta)
	{
		Behavior.Advance(this, delta);
		Age += delta;
	}
	/// <summary>兼容未加入管理器的独立子弹。</summary>
	/// <param name="delta">物理帧秒数。</param>
	public override void _PhysicsProcess(double delta)
	{
		Advance(delta);
		if (Expired) QueueFree();
	}
	/// <summary>加载居中像素贴图。</summary>
	public override void _Ready()
	{
		_sprite.Texture = GD.Load<Texture2D>("res://Assets/Sprite_02.png");
		_sprite.Centered = true;
		_sprite.TextureFilter = TextureFilterEnum.Nearest;
		AddChild(_sprite);
		SetPhysicsProcess(GetParent() is not BulletManager);
	}
	/// <summary>绘制玩家攻击弹的独立几何外观。</summary>
	public override void _Draw()
	{
		if (Team == BulletTeam.Player) DrawCircle(Vector2.Zero, Radius, Colors.Cyan);
	}
}
