using Godot;
using System;

/// <summary>单场战斗的运行与结束状态。</summary>
public enum BattleState
{
	// 正在进行战斗。
	Running,
	// Boss 被击败，不受玩家当前生命影响。
	Victory
}
/// <summary>以60Hz固定步和计时事件分段驱动战斗，管理种子、结束与重开。</summary>
public partial class BattleManager : Node
{
    // 每次进入与重开使用相同种子，保证随机序列从固定起点开始。
    private const int BattleRandomSeed = 0;
	/// <summary>当前战斗状态。</summary>
	public BattleState State { get; private set; }
	/// <summary>当前玩家实例。</summary>
	public PlayerController Player { get; private set; } = null!;
	/// <summary>当前 Boss 实例。</summary>
	public BossController Boss { get; private set; } = null!;
	/// <summary>当前独立弹幕容器。</summary>
	public BulletManager Bullets { get; private set; } = null!;
	/// <summary>已进行的战斗时间，单位为秒。</summary>
	public double Elapsed => Timers.NowUnits / (double)VTimerProcessor.UnitsPerSecond;
    /// <summary>本场战斗共享的计时器处理器。</summary>
    public VTimerProcessor Timers => Bullets.Timers;
	/// <summary>首次胜利结束通知。</summary>
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
	/// <summary>创建全新实例，重置随机序列、生命、阶段、位置及所有计时。</summary>
	public void StartBattle()
	{
		if (Boss is not null)
		{
			Boss.Stop();
            Timers.Clear();
			Bullets.Clear();
			_world.RemoveChild(Player);
			_world.RemoveChild(Boss);
			_world.RemoveChild(Bullets);
			Player.QueueFree();
			Boss.QueueFree();
			Bullets.QueueFree();
		}
		VMath.setRandomSeed(BattleRandomSeed);
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
		Player.Dodge.Initialize(Player, Timers);
        Player.Health.Initialize(Player, Timers);
        Player.Attack.Initialize(Player, Bullets, Boss);
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
		Bullets?.Timers.Clear();
		Bullets?.Clear();
	}
	/// <summary>读取当前键盘输入并推进物理步。</summary>
	/// <param name="delta">引擎物理更新秒数；战斗始终推进一个固定步，不累计此值。</param>
	public override void _PhysicsProcess(double delta)
	{
		if (_stopped) return;
		if (_waitForConfirmRelease && !Input.IsActionPressed("player_dodge")) _waitForConfirmRelease = false;
		if (State != BattleState.Running)
		{
			if (Input.IsActionJustPressed("battle_restart")) Restart();
			return;
		}
		StepFixed(Input.GetVector("move_left", "move_right", "move_up", "move_down"), !_waitForConfirmRelease && Input.IsActionJustPressed("player_dodge"));
	}
	/// <summary>按一个60Hz固定步推进，输入在步内只消费一次。</summary>
    /// <param name="movement">屏幕移动输入，右下为正。</param>
    /// <param name="dodgePressed">本步新按下闪避键时为真。</param>
    public void StepFixed(Vector2 movement, bool dodgePressed)
    {
        if (_stopped || State != BattleState.Running) return;
        if (!movement.IsFinite()) throw new ArgumentOutOfRangeException(nameof(movement));
        try
        {
            // 同刻到期状态先完成，再接受本步按键。
            var clock = Timers;
            clock.AdvanceByUnits(0);
            if (_stopped || State != BattleState.Running || !ReferenceEquals(clock, Timers)) return;
            Player.Movement.ReadDirection(movement);
            if (dodgePressed) Player.Dodge.TryStart(Player.Movement.LastDirection);
            clock.AdvanceByUnits(VTimerProcessor.FixedStepUnits, seconds =>
            {
                Player.Advance(seconds, movement);
                Boss.Advance(seconds);
                Bullets.Advance(seconds, Player, Boss);
                // 每段碰撞后只检查Boss是否被击败，玩家零血和负血继续战斗。
                if (Boss.Hp == 0) Finish(BattleState.Victory);
            });
            Player.FinishStep();
        }
        catch
        {
            _stopped = true;
            SetPhysicsProcess(false);
            throw;
        }
    }
    /// <summary>冻结模拟并清理弹幕，重复结束不重复通知。</summary>
	/// <param name="result">胜利状态，不得为运行中。</param>
	private void Finish(BattleState result)
	{
		if (_stopped || State != BattleState.Running) return;
		State = result;
        Timers.Clear();
		Boss.Stop();
		Bullets.Clear();
		BattleEnded?.Invoke(result);
	}
}
