using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>按JSON结构生成属性表单，支持完整嵌套数组、表达式与可选字段。</summary>
public partial class EmitterPanel
{
    // 折叠状态只属于编辑器，不进入Emitter JSON。
    private readonly Dictionary<string, bool> _expanded = new();
    /// <summary>为当前树选择建立属性编辑菜单。</summary>
    private void BuildInspector()
    {
        _inspectorRevision++; _groupHeaders.Clear();
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
        // 用独立容器判断空结果，说明文字不计入属性命中。
        var fields = new VBoxContainer(); _properties.AddChild(fields);
        ObjectFields(fields, selected, _selection.Length == 0 ? typeof(EditorDocument) : typeof(JsonObject), _selection, creatorType, "", 0);
        if (fields.GetChildCount() == 0)
            fields.AddChild(new Label { Text = "没有匹配的属性。试试中文或英文名称，或清空搜索。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
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
        // 路径种类切换使用共用模板，并保留一次文档事务和后代位置补偿。
        if (type == typeof(VPathSegmentAttribute) && _propertySearch.Text.Trim().Length == 0)
            AddButton(parent, value.ContainsKey("Type") ? "切为普通路径" : "切为瞄准玩家直线",
                () => Mutate(() => SetAt(path, EditorSchema.PathSegment(!value.ContainsKey("Type"))), true, true));
        // 目录提供全部可选属性，原始JSON中的额外属性仍可删除修复。
        var fields = EditorSchema.Fields(type, value, creatorType, context);
        // 当前JSON键值对，按原声明顺序显示。
        foreach (var pair in value.ToArray().OrderBy(pair => { int index = fields.FindIndex(field => field.Name == pair.Key); return index < 0 ? int.MaxValue : index; }))
        {
            if (type == typeof(JsonObject) && pair.Key == "Children") continue;
            if (type == typeof(EditorDocument) && pair.Key == "VNodes") continue;
            // 对应当前JSON字段的类型及说明。
            var field = fields.Find(entry => entry.Name == pair.Key) ?? new EditorSchema.Field(pair.Key, InferType(pair.Value), null, "当前协议未识别的字段；请核对或删除。" );
            if (!MatchesProperty(path + "/" + EditorDocument.Escape(pair.Key), pair.Value, field.ValueType, creatorType, pair.Key)) continue;
            FieldControl(parent, pair.Value, field.ValueType, path + "/" + EditorDocument.Escape(pair.Key), creatorType, pair.Key, field.Tip, depth);
        }
        // 尚未显式填写且允许添加的字段。
        var missing = fields.Where(field => !value.ContainsKey(field.Name) && field.Name != "Children" &&
            MatchesProperty(path + "/" + EditorDocument.Escape(field.Name), null, field.ValueType, creatorType, field.Name)).ToList();
        if (missing.Count == 0) return;
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 待添加字段的选择菜单。
        var menu = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false, ClipText = true };
        // 当前字段元数据。
        foreach (var field in missing) { menu.AddItem(EditorSchema.DisplayName(field.Name)); menu.SetItemTooltip(menu.ItemCount - 1, field.Tip); }
        row.AddChild(menu);
        AddButton(row, "+ 属性", () => Mutate(() =>
        {
            // 对应当前JSON字段的类型及说明。
            var field = missing[menu.Selected];
            EditorSchema.InsertField(Document.At(path)!.AsObject(), fields, field);
        }, true, EditorPositionTools.IsSpatial(path + "/" + missing[menu.Selected].Name)));
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
    /// <param name="itemIndex">队列项零基下标；普通字段为空。</param>
    private void FieldControl(VBoxContainer parent, JsonNode? value, Type type, string path, string creatorType, string name, string tip, int depth, int? itemIndex = null)
    {
        if (value is JsonObject || value is JsonArray)
        {
            // 每组独立折叠；深层列表默认折叠以控制大文档的面板长度。
            var header = new HBoxContainer(); parent.AddChild(header);
            // 记录并控制属性组展开状态的按钮。
            var toggle = new Button { Text = EditorSchema.DisplayName(name) + (value is JsonArray list ? $"  [{list.Count}]" : ""), ToggleMode = true, ButtonPressed = _propertySearch.Text.Trim().Length > 0 || _expanded.GetValueOrDefault(path, depth < 2), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tip };
            _groupHeaders[path] = toggle;
            if (itemIndex.HasValue) toggle.Text = EditorSchema.ItemName(name, itemIndex.Value);
            // 标出基础项下标，画布与属性表使用同一零基索引。
            if (path.StartsWith(_selection + "/BaseAttributes/", StringComparison.Ordinal) && int.TryParse(path[(_selection.Length + "/BaseAttributes/".Length)..], out int basis))
            {
                toggle.Text = $"基础项 [{basis}]";
                if (basis == Canvas.SelectedBasis && Canvas.SelectedPath == _selection)
                {
                    toggle.Text += " · 已选中"; toggle.Modulate = new Color("91d8ff");
                }
            }
            toggle.ClipText = true;
            header.AddChild(toggle); AddQueueActions(header, path, itemIndex); if (!itemIndex.HasValue) AddButton(header, "×", () => Mutate(() => RemoveAt(path), true, EditorPositionTools.IsSpatial(path)));
            // 嵌套属性的左侧缩进容器。
            var inset = new MarginContainer(); inset.AddThemeConstantOverride("margin_left", 10); parent.AddChild(inset);
            // 属性组展开后的内容容器。
            var body = new VBoxContainer(); inset.AddChild(body); inset.Visible = toggle.ButtonPressed;
            toggle.Toggled += expanded => { if (_propertySearch.Text.Trim().Length == 0) _expanded[path] = expanded; inset.Visible = expanded; };
            if (value is JsonObject obj) ObjectFields(body, obj, type, path, creatorType, name, depth + 1);
            else ArrayFields(body, (JsonArray)value, EditorSchema.ElementType(type) ?? typeof(string), path, creatorType, name, depth + 1);
            return;
        }
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 字段名称及悬停说明控件。
        var label = new Label
        {
            Text = itemIndex.HasValue ? EditorSchema.ItemName(name, itemIndex.Value) : EditorSchema.DisplayName(name),
            CustomMinimumSize = new Vector2(172, 0),
            CustomMaximumSize = new Vector2(192, -1),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = tip,
            MouseFilter = MouseFilterEnum.Stop
        };
        row.AddChild(label);
        // 布尔字段用开关，其他标量保留文本和表达式输入。
        Type scalar = Nullable.GetUnderlyingType(type) ?? type;
        // 固定字符串和枚举使用原协议选项，异常原值仍保留供用户修复。
        var choices = EditorSchema.Choices(name, scalar, path, creatorType);
        if (name == "CopySource" && _selection.Contains("/Children/"))
        {
            // 复制源仅允许当前同级中更早声明且未复制的命名节点。
            int split = _selection.LastIndexOf('/'); int index = int.Parse(_selection[(split + 1)..]);
            choices = Document.At(_selection[..split])!.AsArray().Take(index).OfType<JsonObject>()
                .Where(node => node["Core"] is JsonObject core && !core.ContainsKey("CopySource") && core["Name"] is JsonValue)
                .Select(node => node["Core"]!["Name"]!.ToString()).ToArray();
        }
        if (choices.Length > 0)
        {
            var menu = EditorFieldControls.Choice(value, choices, path, Guard, () => _refreshing,
                choice => Mutate(() =>
                {
                    // 普通路径模式沿用端点和表达式，只调整模式专属字段。
                    if (name == "PathMode") EditorSchema.ChangePathMode(Document.At(path[..path.LastIndexOf('/')])!.AsObject(), choice);
                    else SetAt(path, EditorSchema.Scalar(choice, type));
                }, true, EditorPositionTools.IsSpatial(path)), tip);
            row.AddChild(menu);
        }
        else if (scalar == typeof(bool) && value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            var toggle = EditorFieldControls.Toggle(value.GetValue<bool>(), path, Guard, () => _refreshing,
                enabled => Mutate(() => SetAt(path, JsonValue.Create(enabled)), false), tip);
            row.AddChild(toggle);
        }
        else
        {
            // 两类工作区共用标量解析及回车/失焦去重，领域事务仍由当前面板执行。
            var input = EditorFieldControls.Text(value, type, path, Guard, () => _refreshing,
                parsed => Mutate(() => SetAt(path, parsed), name is "Type" or "Name" or "CopySource", EditorPositionTools.IsSpatial(path)), tip);
            row.AddChild(input);
            if (scalar == typeof(double) && name.Contains("Angle", StringComparison.Ordinal))
            {
                // 使用相同表达式规则绘制的角度示意。
                var indicator = new AngleIndicator { CustomMinimumSize = new Vector2(42, 42) }; row.AddChild(indicator);
                indicator.SetText(input.Text); input.TextChanged += indicator.SetText;
            }
        }
        AddQueueActions(row, path, itemIndex); if (!itemIndex.HasValue) AddButton(row, "×", () => Mutate(() => RemoveAt(path), true, EditorPositionTools.IsSpatial(path)));
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
            FieldControl(parent, array[index], type, path + "/" + index, creatorType, context, $"{EditorSchema.DisplayName(context)}[{index}]；按声明顺序处理。", depth, index);
        }
        // 当前属性及操作按钮的横向容器。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 新数组项的模板选择器。
        var variants = new OptionButton();
        // 可添加数组项的模板名称。
        foreach (string variant in type == typeof(VNodeMoveActionAttribute) ? ProtocolModes.Displacement.Values : new[] { "默认" }) variants.AddItem(variant);
        row.AddChild(variants);
        AddButton(row, "+ 添加项", () => Mutate(() => Document.At(path)!.AsArray().Add(EditorSchema.Item(type, context, variants.GetItemText(variants.Selected))), true, EditorPositionTools.IsSpatial(path)));
    }
    /// <summary>把队列操作放到项目标题同一行，边界移动按钮禁用。</summary>
    /// <param name="row">项目标题或标量所在行。</param>
    /// <param name="path">项目JSON指针。</param>
    /// <param name="index">零基下标；非队列字段为空。</param>
    private void AddQueueActions(HBoxContainer row, string path, int? index)
    {
        if (!index.HasValue) return;
        // 当前队列与稳定下标，操作完成后重建控件。
        string parent = path[..path.LastIndexOf('/')]; int position = index.Value;
        EditorArrayControls.AddActions(row, path, position, Document.At(parent)!.AsArray().Count, Guard, () => _refreshing,
            direction => ShiftItem(parent, position, direction),
            duplicate: () => Mutate(() => EditorArrayControls.Duplicate(Document.At(parent)!.AsArray(), position), true, EditorPositionTools.IsSpatial(parent)),
            remove: () => Mutate(() => RemoveAt(path), true, EditorPositionTools.IsSpatial(path)));
    }
    /// <summary>按顺序移动列表成员。</summary>
    /// <param name="path">列表指针。</param>
    /// <param name="index">原下标。</param>
    /// <param name="direction">-1上移或1下移。</param>
    private void ShiftItem(string path, int index, int direction) => Mutate(
        () => EditorArrayControls.Move(Document.At(path)!.AsArray(), index, direction), true, EditorPositionTools.IsSpatial(path));
    /// <summary>提交表单修改并同步校验与原文。</summary>
    /// <param name="change">对当前JSON节点的修改。</param>
    /// <param name="rebuild">结构变化时延迟重建表单。</param>
    /// <param name="spatial">位置字段修改时是否执行后代坐标补偿。</param>
    private void Mutate(Action change, bool rebuild, bool spatial = false)
    {
        RequireAppliedDraft();
        // 位置修改与后代补偿共用一次文档事务，失败完整回滚。
        var before = !_moveChildren && spatial ? new EditorLayout(Document.Validate()) : null;
        Document.Edit(_ => { change(); if (before is not null) EditorPositionTools.PreserveChildren(Document, _selection, before); }); StopPreview();
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
