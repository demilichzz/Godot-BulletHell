using Godot;

/// <summary>注册全游戏默认动作，保留用户已有的同名映射。</summary>
public static class GameInput
{
    /// <summary>配置战斗与场景导航所需的默认键位。</summary>
    public static void EnsureBindings()
    {
        Bind("move_left", Key.A, Key.Left);
        Bind("move_right", Key.D, Key.Right);
        Bind("move_up", Key.W, Key.Up);
        Bind("move_down", Key.S, Key.Down);
        Bind("player_dodge", Key.Space);
        Bind("battle_restart", Key.R);
        Bind("stage_left", Key.Left);
        Bind("stage_right", Key.Right);
        Bind("stage_up", Key.Up);
        Bind("stage_down", Key.Down);
        Bind("stage_confirm", Key.Space);
        Bind("stage_back", Key.Escape);
    }
    /// <summary>注册物理和逻辑键，兼容辅助输入设备。</summary>
    /// <param name="action">非空输入动作名称。</param>
    /// <param name="keys">可触发动作的等价按键。</param>
    private static void Bind(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        // 当前动作接受的各个按键。
        foreach (var key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            InputMap.ActionAddEvent(action, new InputEventKey { Keycode = key });
        }
    }
}
