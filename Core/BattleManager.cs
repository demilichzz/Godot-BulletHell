using Godot;
using System;

/// <summary>单场战斗的运行与结束状态。</summary>
public enum BattleState
{
	// 正在进行战斗。
	Running,
	// Boss 被击败且玩家存活。
	Victory,
	// 玩家生命归零，优先于同帧胜利。
	Defeat
}
/// <summary>统一驱动战斗物理步，管理生成、结束与重开。</summary>
public partial class BattleManager : Node
{
	/// <summary>当前战斗状态。</summary>
	public BattleState State { get; private set; }
	/// <summary>当前玩家实例。</summary>
	public PlayerController Player { get; private set; } = null!;
	/// <summary>当前 Boss 实例。</summary>
	public BossController Boss { get; private set; } = null!;
	/// <summary>当前独立弹幕容器。</summary>
	public BulletManager Bullets { get; private set; } = null!;
	/// <summary>已进行的战斗时间，单位为秒。</summary>
	public double Elapsed { get; private set; }
	/// <summary>首次结束通知，参数为胜利或失败。</summary>
	public event Action<BattleState>? BattleEnded;
	// 场景装配父节点，保持 Boss 与弹幕容器互为兄弟节点。
	private Node2D _world = null!;
	// 当前挑战配置，重开时复用配置并创建新的控制器与阶段。
	private BossData? _bossData;
	// 场景确认键尚未释放时，暂不允许触发闪避。
	private bool _waitForConfirmRelease;
	// 离场后禁止外部继续推进战斗。
	private bool _stopped;
	/// <summary>绑定战场并开始第一场战斗。</summary>
	/// <param name="world">容纳玩家、Boss 和独立弹幕节点的战场。</param>
	/// <param name="bossData">当前 Boss 配置；默认空时使用旧版 Boss 参数。</param>
	public void Initialize(Node2D world, BossData? bossData = null)
	{
		_world = world;
		_bossData = bossData;
		StartBattle();
	}
	/// <summary>创建全新实例，重置生命、阶段、位置及所有计时。</summary>
	public void StartBattle()
	{
		if (Boss is not null)
		{
			Boss.Stop();
			Bullets.Clear();
			_world.RemoveChild(Player);
			_world.RemoveChild(Boss);
			_world.RemoveChild(Bullets);
			Player.QueueFree();
			Boss.QueueFree();
			Bullets.QueueFree();
		}
		Bullets = new BulletManager { Name = "Bullets" };
		_world.AddChild(Bullets);
		if (_bossData is null)
		{
			Boss = new Boss { Name = "Boss", Position = BattleConfig.BossSpawn };
			Boss.Initialize(Bullets);
		}
		else Boss = BossFactory.Create(_bossData, Bullets);
		_world.AddChild(Boss);
		Player = new PlayerController { Name = "Player", Position = BattleConfig.PlayerSpawn };
		_world.AddChild(Player);
		Elapsed = 0;
		_stopped = false;
		State = BattleState.Running;
	}
	/// <summary>重建整场战斗。</summary>
	public void Restart() => StartBattle();
	/// <summary>进入战斗时屏蔽上一场景尚未释放的确认键。</summary>
	public void WaitForConfirmRelease() => _waitForConfirmRelease = true;
	/// <summary>离开场景时停止更新并清理弹幕，可重复调用。</summary>
	public void StopBattle()
	{
		_stopped = true;
		SetPhysicsProcess(false);
		Boss?.Stop();
		Bullets?.Clear();
	}
	/// <summary>读取当前键盘输入并推进物理步。</summary>
	/// <param name="delta">本次物理更新秒数。</param>
	public override void _PhysicsProcess(double delta)
	{
		if (_stopped) return;
		if (_waitForConfirmRelease && !Input.IsActionPressed("player_dodge")) _waitForConfirmRelease = false;
		if (State != BattleState.Running)
		{
			if (Input.IsActionJustPressed("battle_restart")) Restart();
			return;
		}
		Step(delta, Input.GetVector("move_left", "move_right", "move_up", "move_down"), !_waitForConfirmRelease && Input.IsActionJustPressed("player_dodge"));
	}
	/// <summary>依次更新双方和弹幕，再统一判定胜负；可注入输入进行验证。</summary>
	/// <param name="delta">正数有限物理步秒数。</param>
	/// <param name="movement">屏幕移动输入，右和下为正。</param>
	/// <param name="dodgePressed">本步新按下闪避键时为真。</param>
	public void Step(double delta, Vector2 movement, bool dodgePressed)
	{
		if (!double.IsFinite(delta) || delta <= 0) throw new ArgumentOutOfRangeException(nameof(delta));
		if (_stopped || State != BattleState.Running) return;
		// 保存旧无敌计时，避免本步新受伤立即减少保护时间。
		var hurtAtStart = Player.Health.Invulnerability;
		Player.Advance(delta, movement, dodgePressed, Bullets, Boss);
		Boss.Advance(delta);
		Bullets.Advance(delta, Player, Boss);
		Player.FinishStep(delta, hurtAtStart);
		Elapsed += delta;
		if (Player.Health.Hp == 0) Finish(BattleState.Defeat);
		else if (Boss.Hp == 0) Finish(BattleState.Victory);
	}
	/// <summary>冻结模拟并清理弹幕，重复结束不重复通知。</summary>
	/// <param name="result">胜利或失败状态，不得为运行中。</param>
	private void Finish(BattleState result)
	{
		if (_stopped || State != BattleState.Running) return;
		State = result;
		Boss.Stop();
		Bullets.Clear();
		BattleEnded?.Invoke(result);
	}
}
