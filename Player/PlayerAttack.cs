using Godot;

/// <summary>通过重复计时器瞄准Boss当前中心，逐次创建单发批次。</summary>
public sealed class PlayerAttack
{
    // 参数模板与自动攻击句柄。
    private readonly BulletDefaultSet _template = BulletDefaultSet.Get(BulletType.PlayerSet);
    private VTimer? _timer;
    /// <summary>绑定玩家后开始周期射击，首次等待模板间隔。</summary>
    /// <param name="owner">玩家节点，发射位置取当前全局坐标。</param>
    /// <param name="bullets">共享计时器及弹幕容器。</param>
    public void Initialize(Node2D owner, BulletManager bullets)
    {
        _timer?.Cancel();
        // 秒模板集中转换为毫秒，重复间隔为0时由VTimer拒绝。
        long interval = VTimerProcessor.SecondsToMilliseconds(_template.IntervalSeconds);
        _timer = GlobalEvent.RegisterTimer(new VTimer(interval, interval, 0, VTimerType.RepeatForever,
            new[] { owner }, targets =>
            {
                var boss = GlobalEvent.GetBoss();
                if (!GodotObject.IsInstanceValid(boss) || boss.IsQueuedForDeletion() || !boss.IsInsideTree() || boss.Hp == 0) return;
                // 采用事件时刻的全局起点和目标，弧度0向右、π/2向下。
                var origin = targets[0].GlobalPosition;
                var angle = VMath.StandardizationAngleFloat(VMath.GetAngleBetween2Points(origin, boss.GlobalPosition));
                var emitter = new SingleBulletEmitter(_template with { AngleRadians = angle });
                emitter.Emit(bullets, origin);
            }));
    }
    /// <summary>停止自动射击并解除回调。</summary>
    public void Stop() => _timer?.Cancel();
}
