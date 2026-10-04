using Godot;

/// <summary>集中识别当前编辑器的键鼠操作，不添加新映射或执行文档行为。</summary>
public static class EditorInput
{
    /// <summary>解码已有Ctrl快捷键，保留Shift和长按处理。</summary>
    /// <param name="input">原始输入事件。</param>
    /// <returns>语义动作；未知键或重复事件返回None。</returns>
    public static EditorAction Shortcut(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return EditorAction.None;
        return key.Keycode switch
        {
            Key.S => key.ShiftPressed ? EditorAction.SaveAs : EditorAction.Save,
            Key.O => EditorAction.Open,
            Key.F => EditorAction.Search,
            Key.Z => key.ShiftPressed ? EditorAction.Redo : EditorAction.Undo,
            Key.Y => EditorAction.Redo,
            _ => EditorAction.None
        };
    }
}
