using Godot;
using System;
using System.Text.Json.Nodes;

/// <summary>数组项的共用操作按钮与顺序变更；文档事务、业务校验和选择归属仍由面板负责。</summary>
public static class EditorArrayControls
{
    /// <summary>按复制、上移、下移、删除的顺序加入操作按钮；仅创建调用方提供的动作。</summary>
    /// <param name="row">已有标题行或标量行，不改变其布局。</param>
    /// <param name="path">当前数组项的JSON指针，仅用于控件定位。</param>
    /// <param name="index">本次显示的零基下标。</param>
    /// <param name="count">本次显示的数组长度。</param>
    /// <param name="guard">所属面板的错误展示入口。</param>
    /// <param name="refreshing">表单正在重建时返回真。</param>
    /// <param name="move">在所属事务中按-1或1移动。</param>
    /// <param name="duplicate">可选复制动作，不为未提供复制能力的面板增加入口。</param>
    /// <param name="remove">可选删除动作。</param>
    /// <param name="removeText">删除按钮文字，默认叉号。</param>
    public static void AddActions(HBoxContainer row, string path, int index, int count, Action<Action> guard,
        Func<bool> refreshing, Action<int> move, Action? duplicate = null, Action? remove = null, string removeText = "×")
    {
        if (duplicate is not null) Add("复制", "copy", duplicate);
        Add("↑", "up", () => move(-1), index == 0);
        Add("↓", "down", () => move(1), index == count - 1);
        if (remove is not null) Add(removeText, "remove", remove);

        /// <summary>绑定一次表单按钮，禁用边界和重建信号不进入文档事务。</summary>
        /// <param name="text">现有界面的按钮文字。</param>
        /// <param name="actionName">用于验证和定位的动作标识。</param>
        /// <param name="action">面板提供的事务回调。</param>
        /// <param name="disabled">本次显示位置是否不可执行。</param>
        void Add(string text, string actionName, Action action, bool disabled = false)
        {
            var button = new Button { Text = text, TooltipText = text, Disabled = disabled };
            button.SetMeta("json_path", path); button.SetMeta("array_action", actionName);
            row.AddChild(button);
            button.Pressed += () => { if (!button.Disabled && !refreshing()) guard(action); };
        }
    }

    /// <summary>移动一个现有JSON节点；越过首尾边界时不变，不复制节点或改变其原文。</summary>
    /// <param name="array">事务内的有序数组。</param>
    /// <param name="index">有效的零基下标。</param>
    /// <param name="direction">-1上移或1下移。</param>
    /// <returns>位置实际改变时为真，调用方再同步领域选择。</returns>
    public static bool Move(JsonArray array, int index, int direction)
    {
        if ((uint)index >= (uint)array.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        // 先解除父引用，再按相邻位置插回；null和表达式保持原样。
        int target = index + direction;
        if (target < 0 || target >= array.Count) return false;
        var item = array[index]; array.RemoveAt(index); array.Insert(target, item);
        return true;
    }

    /// <summary>在原项后插入独立深副本；显式null同样可复制，名称等业务规则由调用方处理。</summary>
    /// <param name="array">事务内的有序数组。</param>
    /// <param name="index">待复制项的有效零基下标。</param>
    public static void Duplicate(JsonArray array, int index) => array.Insert(index + 1, array[index]?.DeepClone());
}
