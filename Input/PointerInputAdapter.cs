using Godot;

/// <summary>现有左键、指针移动与Esc的设备适配，不执行选择或拖动业务。</summary>
public static class PointerInputAdapter
{
    /// <summary>位置保持事件来源坐标系，视图负责命中和局部转换。</summary>
    /// <param name="input">Godot控件或视口输入。</param>
    /// <returns>语义指针事件；不支持的操作返回None。</returns>
    public static PointerInput Decode(InputEvent input) => input switch
    {
        InputEventMouseButton { ButtonIndex: MouseButton.Left } button => new(button.Pressed ? PointerAction.Press : PointerAction.Release, button.Position),
        InputEventMouseMotion motion => new(PointerAction.Move, motion.Position),
        InputEventKey { Pressed: true, Keycode: Key.Escape } => new(PointerAction.Cancel, default),
        _ => default
    };
}
