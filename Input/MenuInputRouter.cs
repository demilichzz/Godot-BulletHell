using Godot;

/// <summary>菜单输入适配器；保持原键盘回调时机与长按规则。</summary>
public partial class MenuInputRouter : Node
{
    /// <summary>所属场景注入的行为入口。</summary>
    public IMenuActions Target { get; init; } = null!;
    /// <summary>输入层解码后交给行为层，只有被接受的动作才消费事件。</summary>
    /// <param name="input">未被控件消费的键盘事件。</param>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (Target.Handle(Decode(input))) GetViewport().SetInputAsHandled();
    }
    /// <summary>保留方向长按、确认不重复和返回不重复的现有行为。</summary>
    /// <param name="input">Godot输入事件。</param>
    /// <returns>映射后的动作；非现有键盘操作返回None。</returns>
    public static MenuAction Decode(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true } key) return MenuAction.None;
        if (key.IsActionPressed("stage_left", true)) return MenuAction.Left;
        if (key.IsActionPressed("stage_right", true)) return MenuAction.Right;
        if (key.IsActionPressed("stage_up", true)) return MenuAction.Up;
        if (key.IsActionPressed("stage_down", true)) return MenuAction.Down;
        if (key.IsActionPressed("stage_confirm") && !key.Echo) return MenuAction.Confirm;
        if (key.IsActionPressed("stage_back")) return MenuAction.Back;
        return MenuAction.None;
    }
}
