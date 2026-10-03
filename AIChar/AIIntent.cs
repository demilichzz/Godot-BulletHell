using Godot;

/// <summary>策略输出的单步角色操作，默认停留且不闪避、不攻击。</summary>
public readonly record struct AIIntent
{
    /// <summary>屏幕移动向量，右下为正；角色控制器限制长度不超过1。</summary>
    public Vector2 Movement { get; init; }
    /// <summary>本步是否请求触发闪避，默认否。</summary>
    public bool DodgePressed { get; init; }
    /// <summary>是否保持自动攻击开启，默认否。</summary>
    public bool AttackEnabled { get; init; }
}

/// <summary>供策略读取的单步角色状态，不允许策略直接修改角色。</summary>
public readonly record struct AICharState
{
    /// <summary>当前世界位置，逻辑像素。</summary>
    public Vector2 Position { get; init; }
    /// <summary>上一步移动方向，右下为正；停留时为零。</summary>
    public Vector2 LastMovement { get; init; }
    /// <summary>当前固定步秒数，正式战斗为1/60。</summary>
    public double StepSeconds { get; init; }
    /// <summary>普通移动速度，逻辑像素每秒。</summary>
    public float MoveSpeed { get; init; }
    /// <summary>角色碰撞半径，逻辑像素。</summary>
    public float Radius { get; init; }
}