using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>属性搜索、折叠和图标定位，仅管理界面状态。</summary>
public partial class EmitterEditor
{
    // 搜索栏固定在滚动区域外；筛选不写回文档。
    private readonly LineEdit _propertySearch = new();
    private readonly ScrollContainer _propertyScroll = new();
    private readonly Dictionary<string, Button> _groupHeaders = new();
    // 每次重建递增，避免延迟定位滚动到已替换的控件。
    private int _inspectorRevision;

    /// <summary>建立固定搜索栏、折叠操作和可滚动属性区。</summary>
    private void BuildInspectorNavigation()
    {
        // 属性页根容器与操作栏，不随字段滚动。
        var page = new VBoxContainer { Name = "属性" };
        _tabs.AddChild(page);
        _propertySearch.PlaceholderText = "搜索属性：中文 / English（Ctrl+F）";
        _propertySearch.ClearButtonEnabled = true;
        _propertySearch.TooltipText = "筛选当前生成器已填写及可添加的属性；支持中文、英文，不区分大小写。清空恢复原来的折叠状态。";
        page.AddChild(_propertySearch);
        _propertySearch.TextChanged += _ => { BuildInspector(); _propertyScroll.ScrollVertical = 0; };
        // 全部操作只针对当前生成器的属性组。
        var actions = new HBoxContainer(); page.AddChild(actions);
        AddButton(actions, "全部展开", () => ExpandPropertyGroups(true));
        AddButton(actions, "全部收起", () => ExpandPropertyGroups(false));
        AddButton(actions, "定位基础项", RevealSelectedBasis).TooltipText = "清空搜索并定位画布选中基础项；复制继承的属性不会被展开写回。";
        _propertyScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _propertyScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        page.AddChild(_propertyScroll);
        _properties.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _propertyScroll.AddChild(_properties);
    }

    /// <summary>判断字段或其子字段是否匹配；组名匹配时保留组内全部字段。</summary>
    /// <param name="path">从当前选择起算的属性指针。</param>
    /// <param name="value">原始字段值，允许显式null。</param>
    /// <param name="type">属性声明类型，用于查找可添加字段。</param>
    /// <param name="creatorType">当前生成器的运行类型。</param>
    /// <param name="context">当前属性组名称。</param>
    /// <returns>是否应在筛选结果中显示。</returns>
    private bool MatchesProperty(string path, JsonNode? value, Type type, string creatorType, string context)
    {
        // 忽略大小写匹配当前选择内的中文或英文键名。
        string query = _propertySearch.Text.Trim();
        if (query.Length == 0) return true;
        // 仅检查当前生成器的路径，防止祖先Children命中整棵子树。
        string relative = path.StartsWith(_selection + "/", StringComparison.Ordinal) ? path[(_selection.Length + 1)..] : path;
        if (relative.Split('/').Any(part => !int.TryParse(part, out _) &&
            EditorSchema.DisplayName(part.Replace("~1", "/").Replace("~0", "~")).Contains(query, StringComparison.OrdinalIgnoreCase))) return true;
        if (value is JsonObject obj)
        {
            // 已有对象内同时匹配显式字段和可添加字段，不创建任何默认数据。
            var fields = EditorSchema.Fields(type, obj, creatorType, context);
            return fields.Any(field => MatchesProperty(path + "/" + EmitterDocument.Escape(field.Name), null, field.ValueType, creatorType, field.Name)) ||
                obj.Any(pair => MatchesProperty(path + "/" + EmitterDocument.Escape(pair.Key), pair.Value,
                    fields.Find(field => field.Name == pair.Key)?.ValueType ?? InferType(pair.Value), creatorType, pair.Key));
        }
        if (value is JsonArray array)
            return array.Select((item, index) => MatchesProperty(path + "/" + index, item,
                EditorSchema.ElementType(type) ?? typeof(string), creatorType, context)).Any(match => match);
        return false;
    }

    /// <summary>展开或收起全部属性组；搜索期间只临时改变结果显示。</summary>
    /// <param name="expanded">是否展开。</param>
    private void ExpandPropertyGroups(bool expanded)
    {
        // 已装配的每个分组按钮，包括暂时隐藏的深层组。
        foreach (var header in _groupHeaders.Values) header.ButtonPressed = expanded;
    }

    /// <summary>清空搜索并展开选中基础项，布局稳定后滚动到对应标题。</summary>
    private async void RevealSelectedBasis()
    {
        _propertySearch.Text = "";
        // 只定位声明中真实存在的基础项，CopySource继承内容保持原文。
        string group = _selection + "/BaseAttributes", path = group + "/" + Canvas.SelectedBasis;
        if (Canvas.SelectedPath != _selection || Document.At(_selection) is not JsonObject selected ||
            selected["BaseAttributes"] is not JsonArray bases || Canvas.SelectedBasis >= bases.Count || Canvas.SelectedBasis < 0)
        {
            BuildInspector();
            SetStatus("当前选择没有可直接编辑的基础项；若来自复制继承，请在源生成器中编辑，或添加覆盖属性。", false);
            return;
        }
        _expanded[group] = true; _expanded[path] = true;
        BuildInspector();
        // 捕获本次界面版本及目标控件，防止等待期间切换选择。
        int revision = _inspectorRevision;
        if (!_groupHeaders.TryGetValue(path, out var target)) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree()) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInsideTree() && revision == _inspectorRevision && IsInstanceValid(target))
            _propertyScroll.ScrollVertical += (int)(target.GlobalPosition.Y - _propertyScroll.GlobalPosition.Y);
    }

    /// <summary>统一执行文档撤销或重做，空历史不刷新界面或中止预览。</summary>
    /// <param name="redo">true重做，false撤销。</param>
    private void ChangeHistory(bool redo)
    {
        RequireAppliedDraft();
        if (redo ? !Document.CanRedo : !Document.CanUndo) return;
        if (redo) Document.Redo(); else Document.Undo();
        Refresh();
    }

    /// <summary>拖动期间只显示独立草稿，松手后提交一次历史记录。</summary>
    /// <param name="marker">拖动开始时的基础项。</param>
    /// <param name="target">目标世界逻辑像素位置。</param>
    /// <param name="commit">是否松手提交。</param>
    /// <returns>操作是否成功。</returns>
    private bool MoveMarker(EditorCanvas.Marker marker, Vector2 target, bool commit)
    {
        try
        {
            RequireAppliedDraft();
            // 保留亚像素位置到0.001逻辑像素，避免屏幕缩放带来过长小数。
            target = new Vector2((float)Math.Round(target.X, 3), (float)Math.Round(target.Y, 3));
            var before = new EditorLayout(Document.Validate(), Document.Root);
            var draft = new EmitterDocument(); draft.ApplyText(Document.Text);
            draft.Edit(_ =>
            {
                EditorPositionTools.Translate(draft, marker.Path, marker.Basis, target - marker.Position);
                if (!_moveChildren) EditorPositionTools.PreserveChildren(draft, marker.Path, before);
            });
            var layout = new EditorLayout(draft.Validate(), draft.Root);
            if (commit)
            {
                Document.ApplyText(draft.Text); _selection = marker.Path; Refresh(); RevealSelectedBasis();
            }
            else Canvas.ApplyLayout(layout);
            SetStatus($"{(commit ? "已设置" : "拖动中")}基础项 [{marker.Basis}]：X={target.X:F3}，Y={target.Y:F3} · {(_moveChildren ? "子节点随参考变化" : "保持后代静态位置")} · Esc取消", false);
            return true;
        }
        catch (Exception error) { ValidateLayout(); SetStatus("位置修改未应用：" + error.Message, true); return false; }
    }
}
