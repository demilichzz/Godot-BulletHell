using Godot;

/// <summary>将现有Godot动作映射采样为语义快照，不执行战斗业务。</summary>
public sealed class GodotBattleInput : IBattleInputSource
{
    /// <summary>保持现有移动、闪避、重开及阶段切换的物理步采样方式。</summary>
    /// <returns>本步方向、保持状态和按下沿。</returns>
    public BattleInputFrame Read() => new(Input.GetVector("move_left", "move_right", "move_up", "move_down"),
        Input.IsActionPressed("player_dodge"), Input.IsActionJustPressed("player_dodge"),
        Input.IsActionJustPressed("battle_restart"), Input.IsActionJustPressed("battle_previous_phase"),
        Input.IsActionJustPressed("battle_next_phase"));
}
