using System;

/// <summary>与设备无关的菜单行为，None表示输入未映射。</summary>
public enum MenuAction
{
    /// <summary>没有菜单行为。</summary>
    None,
    /// <summary>向左选择。</summary>
    Left,
    /// <summary>向右选择。</summary>
    Right,
    /// <summary>向上选择。</summary>
    Up,
    /// <summary>向下选择。</summary>
    Down,
    /// <summary>确认当前选择。</summary>
    Confirm,
    /// <summary>返回上一级场景。</summary>
    Back
}

/// <summary>菜单行为层入口，输入适配器依据返回值消费事件。</summary>
public interface IMenuActions
{
    /// <summary>处理本场景支持的菜单行为。</summary>
    /// <param name="action">语义动作。</param>
    /// <returns>本场景接受该动作时为真。</returns>
    bool Handle(MenuAction action);
}

/// <summary>战斗场景的返回行为，不认识具体按键与UI。</summary>
public sealed class BattleMenuController : IMenuActions
{
    // 生命周期检查和导航请求由所属场景注入。
    private readonly Func<bool> _active;
    private readonly Action _back;
    /// <summary>装配返回行为。</summary>
    /// <param name="active">场景是否仍活动。</param>
    /// <param name="back">返回选关请求。</param>
    public BattleMenuController(Func<bool> active, Action back) { _active = active; _back = back; }
    /// <summary>只接受活动战斗场景的返回行为。</summary>
    /// <param name="action">待处理动作。</param>
    /// <returns>返回请求已提交时为真。</returns>
    public bool Handle(MenuAction action)
    {
        if (!_active() || action != MenuAction.Back) return false;
        _back(); return true;
    }
}
