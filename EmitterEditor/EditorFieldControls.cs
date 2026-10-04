using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Boss与Emitter表单共用的标量输入及一次性提交绑定。</summary>
public static class EditorFieldControls
{
    /// <summary>建立保留表达式和显式null的输入框，回车及失焦只提交一次。</summary>
    /// <param name="value">原始JSON标量，可为空。</param>
    /// <param name="type">字段声明类型。</param>
    /// <param name="path">用于定位控件的JSON指针。</param>
    /// <param name="guard">执行操作并展示错误的面板入口。</param>
    /// <param name="refreshing">面板重建期间为真，禁止提交失焦事件。</param>
    /// <param name="apply">在所属文档事务中应用独立JSON值。</param>
    /// <param name="tip">字段说明与单位，默认空。</param>
    /// <returns>已绑定信号的输入框，由调用方加入布局。</returns>
    public static LineEdit Text(JsonNode? value, Type type, string path, Action<Action> guard,
        Func<bool> refreshing, Action<JsonNode?> apply, string tip = "")
    {
        // 原始字符串不增加JSON引号，数字与null保持原文表示。
        var input = new LineEdit
        {
            Text = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value?.ToJsonString() ?? "null",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 0),
            TooltipText = tip, SelectAllOnFocus = true
        };
        input.SetMeta("json_path", path);
        // 仅在事务成功后更新基线，失败输入继续留在控件中供修复。
        string applied = input.Text;
        /// <summary>提交一次文本变化，忽略重建及随后重复失焦。</summary>
        void Commit()
        {
            if (!GodotObject.IsInstanceValid(input) || refreshing() || input.Text == applied) return;
            string next = input.Text;
            guard(() => { apply(EditorSchema.Scalar(next, type)); applied = next; });
        }
        input.TextSubmitted += _ => Commit(); input.FocusExited += Commit;
        return input;
    }
    /// <summary>建立保留未知原值的协议菜单；重复选择、重建事件及占位项不写入文档。</summary>
    /// <param name="value">原始JSON标量，允许显式null或暂时无效值。</param>
    /// <param name="choices">按声明顺序排列的合法原值。</param>
    /// <param name="path">用于定位控件的JSON指针。</param>
    /// <param name="guard">面板的异常展示入口。</param>
    /// <param name="refreshing">界面重建期间为真。</param>
    /// <param name="apply">在所属事务中应用选中的协议原值。</param>
    /// <param name="tip">说明与单位，默认空。</param>
    /// <param name="label">可选显示文字转换，不改变实际协议值。</param>
    /// <returns>已绑定的一次性选择控件。</returns>
    public static OptionButton Choice(JsonNode? value, IReadOnlyList<string> choices, string path, Action<Action> guard,
        Func<bool> refreshing, Action<string> apply, string tip = "", Func<string, string>? label = null)
    {
        // 冻结选项，后续回调不受外部列表变化影响。
        string[] options = choices.ToArray();
        string applied = value?.ToString() ?? "null";
        var menu = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FitToLongestItem = false, ClipText = true, TooltipText = tip };
        menu.SetMeta("json_path", path);
        foreach (string choice in options) menu.AddItem(label?.Invoke(choice) ?? choice);
        int appliedIndex = Array.IndexOf(options, applied);
        if (appliedIndex < 0) { menu.AddItem(applied + "（当前值）"); appliedIndex = menu.ItemCount - 1; }
        menu.Select(appliedIndex);
        menu.ItemSelected += index =>
        {
            if (refreshing() || index < 0 || index >= options.Length || options[index] == applied) return;
            // 事务失败时恢复上次实际值的显示，不伪装成已经接受选择。
            bool success = false;
            guard(() => { apply(options[index]); applied = options[index]; appliedIndex = (int)index; success = true; });
            if (!success && GodotObject.IsInstanceValid(menu)) menu.Select(appliedIndex);
        };
        return menu;
    }

    /// <summary>建立布尔开关，保留面板事务并抑制重复或重建期间的信号。</summary>
    /// <param name="value">已确认的JSON布尔值。</param>
    /// <param name="path">用于定位控件的JSON指针。</param>
    /// <param name="guard">面板的异常展示入口。</param>
    /// <param name="refreshing">界面重建期间为真。</param>
    /// <param name="apply">在所属事务中应用新值。</param>
    /// <param name="tip">说明与单位，默认空。</param>
    /// <returns>已绑定的布尔开关。</returns>
    public static CheckButton Toggle(bool value, string path, Action<Action> guard, Func<bool> refreshing, Action<bool> apply, string tip = "")
    {
        // 仅成功提交更新基线，错误回调不会改变文档或控件显示。
        bool applied = value;
        var toggle = new CheckButton { ButtonPressed = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip };
        toggle.SetMeta("json_path", path);
        toggle.Toggled += enabled =>
        {
            if (refreshing() || enabled == applied) return;
            bool success = false;
            guard(() => { apply(enabled); applied = enabled; success = true; });
            if (!success && GodotObject.IsInstanceValid(toggle)) toggle.SetPressedNoSignal(applied);
        };
        return toggle;
    }
}
