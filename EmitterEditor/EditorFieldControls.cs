using Godot;
using System;
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
}
