using Godot;

/// <summary>集中保存首版战斗参数；位置为像素，时间为秒。</summary>
public static class BattleConfig
{
	// 基准画面矩形，单位为逻辑像素，坐标向右、向下递增。
	public static readonly Rect2 Bounds = new(0, 0, 1280, 800);
	// 圆形活动区域中心，单位为逻辑像素，不随窗口尺寸变化。
	public static readonly Vector2 ArenaCenter = Bounds.GetCenter();
	// 圆形活动区域半径，单位为逻辑像素，玩家判定圆须完整位于其中。
	public const float ArenaRadius = 400;
	// 双方出生位置，单位为逻辑像素，保持原有相对画面中心的偏移。
	public static readonly Vector2 PlayerSpawn = new(640, 600), BossSpawn = new(640, 250);
	// 玩家正常速度与闪避速度，单位为像素/秒。
	public const float MoveSpeed = 240, DodgeSpeed = 720;
	// 闪避持续、冷却和受击无敌时间，单位为秒；冷却从触发计时。
	public const double DodgeDuration = 0.15, DodgeCooldown = 1, HurtInvulnerability = 1;
	// 双方初始生命与子弹伤害，单位为点。
	public const int PlayerHp = 3, BossHp = 100, Damage = 1;
	// 双方与弹幕碰撞半径，单位为像素，不随图片缩放。
	public const float PlayerRadius = 5, BossRadius = 32, EnemyBulletRadius = 6, PlayerBulletRadius = 3;
	// 双方射击间隔，单位为秒。
	public const double BossInterval = 1, PlayerInterval = 0.2;
	// 弹速为像素/秒，寿命为秒。
	public const float EnemySpeed = 180, EnemyLifetime = 4, ShotSpeed = 600, ShotLifetime = 2;
	// 环形数量、敌弹图集索引（0～9）与活动弹幕上限。
	public const int RingCount = 24, EnemyColor = 0, MaxBullets = 2048;
	// Boss 与敌弹显示倍率，均为正数。
	public const float BossScale = 3, EnemyScale = 3;
}
