using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>按JSON结构生成属性表单，支持完整嵌套数组、表达式与可选字段。</summary>
public partial class EmitterEditor
{
    // 折叠状态只属于编辑器，不进入Emitter JSON。
    private readonly Dictionary<string, bool> _expanded = new();
    /// <summary>为当前树选择建立属性编辑菜单。</summary>
    private void BuildInspector()
    {
        // 当前子节点，逐个处理以保持原有顺序。
        foreach (Node child in _properties.GetChildren()) { _properties.RemoveChild(child); child.QueueFree(); }
        // 当前选中的原始JSON对象。
        var selected = Document.At(_selection) as JsonObject;
        if (selected is null) { _properties.AddChild(new Label { Text = "当前结构无效，可在JSON页修复。" }); return; }
        // 当前选择实际使用的Creator类型。
        string creatorType = CreatorType(selected);
        _properties.AddChild(new Label { Text = _selection.Length == 0 ? "Emitter 共用属性" : $"{creatorType}  ·  基础项 [{Canvas.SelectedBasis}]", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _properties.AddChild(new Label { Text = "输入后回车或移开焦点应用 · × 移除可选字段\n角度单位rad；表达式原文保留。null为显式空值。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (selected["Core"] is JsonObject core && core.ContainsKey("CopySource"))
            _properties.AddChild(new Label { Text = "当前编辑复制声明的覆盖项；未填写字段继承复制源。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        ObjectFields(_properties, selected, _selection.Length == 0 ? typeof(EmitterDocument) : typeof(JsonObject), _selection, creatorType, "", 0);
    }
    /// <summary>取得原始或CopySource继承的Creator类型。</summary>
    /// <param name="node">当前声明。</param>
    /// <returns>有效类型；暂时无效时按VNode显示可用字段。</returns>
    private string CreatorType(JsonObject node)
    {
        if (node["Core"] is JsonObject core && core["Type"] is JsonValue value) return value.ToString();
        try
        {
            // 按当前文档选择定位的运行定义。
            var creator = Document.Validate().Root;
            // 用于逐层定位子Creator的路径片段。
            var parts = _selection.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // 按声明顺序处理的零基下标。
            for (int index = 2; index < parts.Length; index += 2) creator = creator.Children[int.Parse(parts[index])];
            return creator.Core.Type;
        }
        catch { return "VNode"; }
    }
    /// <summary>生成对象内的字段和缺省字段添加菜单。</summary>
    /// <param name="parent">显示容器。</param>
    /// <param name="value">对象数据。</param>
    /// <param name="type">属性组类型。</param>
    /// <param name="path">JSON Pointer。</param>
    /// <param name="creatorType">所属Creator类型。</param>
    /// <param name="context">属性组名。</param>
    /// <param name="depth">显示层级。</param>
    private void ObjectFields(VBoxContainer parent, JsonObject value, Type type, string path, string creatorType, string context, int depth)
    {
        // 目录提供全部可选属性，原始JSON中的额外属性仍可删除修复。
        var fields = EditorSchema.Fields(type, value, creatorType, context);
        // 当前JSON键值对，按原声明顺序显示。
        foreach (var pair in value.ToArray())
        {
            if (type == typeof(JsonObject) && pair.Key == "Children") continue;
            if (type == typeof(EmitterDocument) && pair.Key == "VNodes") continue;
            // 对应当前JSON字段的类型及说明。
            var field = fields.Find(entry => entry.Name == pair.Key) ?? new EditorSchema.Field(pair.Key, InferType(pair.Value), null, "当前协议未识别的字段；请核对或删除。" );
            FieldControl(parent, pair.Value, field.ValueType, path + "/" + EmitterDocument.Escape(pair.Key), creatorType, pair.Key, field.Tip, depth);
        }
        // 尚未显式填写且允许添加的字段。
        var missing = fields.Where(field => !value.ContainsKey(field.Name) && field.Name != "Children").ToList();
        if (missing.Count == 0) return;
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 待添加字段的选择菜单。
        var menu = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        // 当前字段元数据。
        foreach (var field in missing) { menu.AddItem(EditorSchema.DisplayName(field.Name)); menu.SetItemTooltip(menu.ItemCount - 1, field.Tip); }
        row.AddChild(menu);
        AddButton(row, "+ 属性", () => Mutate(() =>
        {
            // 对应当前JSON字段的类型及说明。
            var field = missing[menu.Selected];
            Document.At(path)!.AsObject()[field.Name] = field.Default?.DeepClone();
        }, true));
    }
    /// <summary>为对象、数组或标量建立对应控件。</summary>
    /// <param name="parent">显示容器。</param>
    /// <param name="value">当前值。</param>
    /// <param name="type">字段类型。</param>
    /// <param name="path">文档指针。</param>
    /// <param name="creatorType">所属Creator类型。</param>
    /// <param name="name">字段或数组项名称。</param>
    /// <param name="tip">悬停说明。</param>
    /// <param name="depth">层级。</param>
    private void FieldControl(VBoxContainer parent, JsonNode? value, Type type, string path, string creatorType, string name, string tip, int depth)
    {
        if (value is JsonObject || value is JsonArray)
        {
            // 每组独立折叠；深层列表默认折叠以控制大文档的面板长度。
            var header = new HBoxContainer(); parent.AddChild(header);
            // 记录并控制属性组展开状态的按钮。
            var toggle = new Button { Text = EditorSchema.DisplayName(name) + (value is JsonArray list ? $"  [{list.Count}]" : ""), ToggleMode = true, ButtonPressed = _expanded.GetValueOrDefault(path, depth < 2), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tip };
            header.AddChild(toggle); AddButton(header, "×", () => Mutate(() => RemoveAt(path), true));
            // 嵌套属性的左侧缩进容器。
            var inset = new MarginContainer(); inset.AddThemeConstantOverride("margin_left", 10); parent.AddChild(inset);
            // 属性组展开后的内容容器。
            var body = new VBoxContainer(); inset.AddChild(body); inset.Visible = toggle.ButtonPressed;
            toggle.Toggled += expanded => { _expanded[path] = expanded; inset.Visible = expanded; };
            if (value is JsonObject obj) ObjectFields(body, obj, type, path, creatorType, name, depth + 1);
            else ArrayFields(body, (JsonArray)value, EditorSchema.ElementType(type) ?? typeof(string), path, creatorType, name, depth + 1);
            return;
        }
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 字段名称及悬停说明控件。
        var label = new Label
        {
            Text = EditorSchema.DisplayName(name),
            CustomMinimumSize = new Vector2(172, 0),
            CustomMaximumSize = new Vector2(192, -1),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = tip,
            MouseFilter = MouseFilterEnum.Stop
        };
        row.AddChild(label);
        // 布尔字段用开关，其他标量保留文本和表达式输入。
        Type scalar = Nullable.GetUnderlyingType(type) ?? type;
        if (scalar == typeof(bool) && value is not null)
        {
            // 布尔字段的实际开关控件。
            var check = new CheckButton { ButtonPressed = value.ToString().Equals("true", StringComparison.OrdinalIgnoreCase), TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(check); check.Toggled += enabled => Guard(() => Mutate(() => SetAt(path, JsonValue.Create(enabled)), false));
        }
        else
        {
            // 保留表达式原文的字段输入框。
            var input = new LineEdit { Text = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value?.ToJsonString() ?? "null", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 0), TooltipText = tip, SelectAllOnFocus = true };
            row.AddChild(input);
            // 记录已应用文本，焦点离开及回车不会重复生成撤销记录。
            string applied = input.Text;
            /// <summary>提交当前输入；保持未通过业务校验的文本供继续修复。</summary>
            void Commit()
            {
                if (input.Text == applied || !IsInstanceValid(input)) return;
                Guard(() =>
                {
                    // 按照字段类型转换的独立JSON值。
                    JsonNode? parsed = EditorSchema.Scalar(input.Text, type);
                    Mutate(() => SetAt(path, parsed), name is "Type" or "Name" or "CopySource"); applied = input.Text;
                });
            }
            input.TextSubmitted += _ => Commit(); input.FocusExited += Commit;
            if (scalar == typeof(double) && name.Contains("Angle", StringComparison.Ordinal))
            {
                // 使用相同表达式规则绘制的角度示意。
                var indicator = new AngleIndicator { CustomMinimumSize = new Vector2(42, 42) }; row.AddChild(indicator);
                indicator.SetText(input.Text); input.TextChanged += indicator.SetText;
            }
        }
        AddButton(row, "×", () => Mutate(() => RemoveAt(path), true));
    }
    /// <summary>显示数组项，并提供追加、复制、移动和删除。</summary>
    /// <param name="parent">显示容器。</param>
    /// <param name="array">数组数据。</param>
    /// <param name="type">元素类型。</param>
    /// <param name="path">数组指针。</param>
    /// <param name="creatorType">所属Creator类型。</param>
    /// <param name="context">数组字段名。</param>
    /// <param name="depth">显示层级。</param>
    private void ArrayFields(VBoxContainer parent, JsonArray array, Type type, string path, string creatorType, string context, int depth)
    {
        // 按声明顺序处理的零基下标。
        for (int index = 0; index < array.Count; index++)
        {
            // 捕获固定下标；修改后统一重建，避免闭包读到下一次循环值。
            int position = index;
            // 当前数组项的复制与排序按钮行。
            var actions = new HBoxContainer(); parent.AddChild(actions);
            actions.AddChild(new Label { Text = $"[{index}]", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            AddButton(actions, "复制", () => Mutate(() => { var list = Document.At(path)!.AsArray(); list.Insert(position + 1, list[position]?.DeepClone()); }, true));
            AddButton(actions, "↑", () => ShiftItem(path, position, -1)); AddButton(actions, "↓", () => ShiftItem(path, position, 1));
            FieldControl(parent, array[index], type, path + "/" + index, creatorType, context, $"{EditorSchema.DisplayName(context)}[{index}]；按声明顺序处理。", depth);
        }
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 新数组项的模板选择器。
        var variants = new OptionButton();
        // 可添加数组项的模板名称。
        foreach (string variant in type == typeof(VNodeMoveActionAttribute) ? new[] { "XYMove", "PMove", "TarMove" } : new[] { "默认" }) variants.AddItem(variant);
        row.AddChild(variants);
        AddButton(row, "+ 添加项", () => Mutate(() => Document.At(path)!.AsArray().Add(EditorSchema.Item(type, context, variants.GetItemText(variants.Selected))), true));
    }
    /// <summary>按顺序移动列表成员。</summary>
    /// <param name="path">列表指针。</param>
    /// <param name="index">原下标。</param>
    /// <param name="direction">-1上移或1下移。</param>
    private void ShiftItem(string path, int index, int direction) => Mutate(() =>
    {
        // 正在调整顺序的数组及目标下标。
        var list = Document.At(path)!.AsArray(); int target = index + direction;
        if (target < 0 || target >= list.Count) return;
        // 需要显示或移动的当前列表项。
        var item = list[index]; list.RemoveAt(index); list.Insert(target, item);
    }, true);
    /// <summary>提交表单修改并同步校验与原文。</summary>
    /// <param name="change">对当前JSON节点的修改。</param>
    /// <param name="rebuild">结构变化时延迟重建表单。</param>
    private void Mutate(Action change, bool rebuild)
    {
        RequireAppliedDraft(); Document.Edit(_ => change()); StopPreview();
        SyncJson(); ValidateLayout(); UpdateTitle();
        if (rebuild) Callable.From(() => { if (IsInsideTree()) Refresh(); }).CallDeferred();
    }
    /// <summary>设置文档中现有字段或数组项。</summary>
    /// <param name="path">目标指针。</param>
    /// <param name="value">独立JSON值。</param>
    private void SetAt(string path, JsonNode? value)
    {
        // 字段指针的最后分隔位置及对应父容器。
        int split = path.LastIndexOf('/'); var parent = Document.At(path[..split]);
        // 解除JSON Pointer转义后的字段名。
        string key = path[(split + 1)..].Replace("~1", "/").Replace("~0", "~");
        if (parent is JsonArray array) array[int.Parse(key)] = value;
        else parent!.AsObject()[key] = value;
    }
    /// <summary>移除字段或数组项；校验错误允许继续编辑修复。</summary>
    /// <param name="path">目标指针。</param>
    private void RemoveAt(string path)
    {
        // 字段指针的最后分隔位置及对应父容器。
        int split = path.LastIndexOf('/'); var parent = Document.At(path[..split]);
        // 解除JSON Pointer转义后的字段名。
        string key = path[(split + 1)..].Replace("~1", "/").Replace("~0", "~");
        if (parent is JsonArray array) array.RemoveAt(int.Parse(key)); else parent!.AsObject().Remove(key);
    }
    /// <summary>给未知字段提供保守编辑类型。</summary>
    /// <param name="value">已有JSON值。</param>
    /// <returns>用于文本编辑的类型。</returns>
    private static Type InferType(JsonNode? value) => value switch
    {
        JsonObject => typeof(JsonObject), JsonArray => typeof(string[]),
        _ => value?.GetValueKind() == JsonValueKind.Number ? typeof(double) : value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? typeof(bool) : typeof(string)
    };
}
