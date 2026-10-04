using Godot;

/// <summary>指针事件语义；坐标转换和命中检测仍由视图负责。</summary>
public enum PointerAction
{
    /// <summary>不支持的输入。</summary>
    None,
    /// <summary>主指针按下。</summary>
    Press,
    /// <summary>主指针释放。</summary>
    Release,
    /// <summary>指针移动。</summary>
    Move,
    /// <summary>取消当前手势。</summary>
    Cancel
}

/// <summary>与具体按钮类型隔离的指针事件。</summary>
/// <param name="Action">当前手势动作。</param>
/// <param name="Position">事件来源坐标系的像素位置；取消时不使用。</param>
public readonly record struct PointerInput(PointerAction Action, Vector2 Position);
