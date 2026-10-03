using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>独立弹幕编辑场景，负责文件、树选择、JSON草稿及预览控制。</summary>
public partial class EmitterEditor : Control
{
    /// <summary>当前编辑文档，预览只读取其快照。</summary>
    public EmitterDocument Document { get; } = new();
    /// <summary>基础布局及动态图像画布。</summary>
    public EditorCanvas Canvas { get; } = new();
    /// <summary>只属于编辑器进程的预览环境。</summary>
    public EditorPreview Preview { get; } = new();
    // 界面控件及选择状态；JSON Pointer始终指向编辑数据，不是运行成员。
    private readonly Tree _tree = new();
    private readonly VBoxContainer _properties = new();
    private readonly CodeEdit _json = new();
    private readonly TabContainer _tabs = new();
    private readonly Label _status = new(), _title = new(), _clock = new();
    private readonly SubViewport _viewport = new();
    private readonly FileDialog _open = new(), _save = new();
    private readonly ConfirmationDialog _discard = new();
    private readonly Dictionary<string, TreeItem> _items = new();
    private string _selection = "/VNodes", _jsonBaseline = "", _previewText = "";
    private bool _refreshing, _playing, _jsonDirty;
    private Action? _afterDiscard;
    private Button _play = null!;
    /// <summary>搭建独立工具界面并加载可选命令行文件。</summary>
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        Engine.PhysicsTicksPerSecond = 60;
        GetWindow().Title = "弹幕编辑器 · BulletHell";
        GetWindow().MinSize = new Vector2I(1200, 760);
        GetWindow().Size = new Vector2I(1600, 950);
        // 深色编辑工作区，字号不随预览分辨率缩放。
        var theme = new Theme { DefaultFontSize = 15 };
        Theme = theme;
        // 不截获鼠标输入的工作区底色。
        var background = new ColorRect { Color = new Color("111b28"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        // 工作区四边的固定留白容器。
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 当前留白方向的主题键。
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 12);
        AddChild(margin);
        // 从标题到状态栏的主纵向布局。
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
        _title.AddThemeFontSizeOverride("font_size", 21); layout.AddChild(_title);
        // 文件操作与历史控制工具栏。
        var files = new HBoxContainer(); layout.AddChild(files);
        AddButton(files, "新建", () => DiscardThen(() => { Document.New(); _selection = "/VNodes"; Refresh(); }));
        AddButton(files, "打开…", () => DiscardThen(() => _open.PopupCentered(new Vector2I(960, 640))));
        AddButton(files, "保存", () => Save(false));
        AddButton(files, "另存为…", () => Save(true));
        AddButton(files, "撤销", () => { RequireAppliedDraft(); Document.Undo(); Refresh(); });
        AddButton(files, "重做", () => { RequireAppliedDraft(); Document.Redo(); Refresh(); });
        AddButton(files, "校验", () => { RequireAppliedDraft(); Document.Validate(); SetStatus("校验通过；尚未运行的几何约束将在预览生成时检查。", false); });
        // 预览播放及显示质量工具栏。
        var controls = new HBoxContainer(); layout.AddChild(controls);
        _play = AddButton(controls, "▶ 播放", TogglePlay);
        AddButton(controls, "单步 1/60s", () => { EnsurePreview(); _playing = false; Preview.Advance(); UpdatePreview(); });
        AddButton(controls, "重置预览", () => { RequireAppliedDraft(); StartPreview(); _playing = false; UpdatePreview(); });
        AddButton(controls, "返回布局", StopPreview);
        // 只控制普通子弹显示的开关。
        var simple = new CheckButton { Text = "简化子弹", ButtonPressed = true, TooltipText = "普通子弹统一圆点；激光保留曲线和阶段。只改变显示，容量、碰撞与随机保持一致。" };
        simple.Toggled += value => { Preview.Simple = value; Preview.RefreshDisplay(); }; controls.AddChild(simple);
        // 预览渲染分辨率选择器。
        var resolution = new OptionButton { TooltipText = "仅降低预览像素数；逻辑场地始终1280×800。" };
        resolution.AddItem("预览 50%"); resolution.AddItem("预览 100%");
        resolution.ItemSelected += index => _viewport.Size = index == 0 ? new Vector2I(640, 400) : new Vector2I(1280, 800);
        controls.AddChild(resolution); controls.AddChild(_clock);
        // 生成器树与编辑工作区的分栏容器。
        var workspace = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = new[] { 230 } }; layout.AddChild(workspace);
        // 生成器树及其操作按钮容器。
        var hierarchy = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) }; workspace.AddChild(hierarchy);
        hierarchy.AddChild(new Label { Text = "CREATOR  /  生成器树" });
        _tree.HideRoot = false; _tree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _tree.ItemSelected += TreeSelected; hierarchy.AddChild(_tree);
        // 子Creator类型和添加按钮所在行。
        var addRow = new HBoxContainer(); hierarchy.AddChild(addRow);
        // 新建Creator的类型选择器。
        var kind = new OptionButton(); foreach (string type in new[] { "VNode", "VBullet", "VPath", "VLaser" }) kind.AddItem(type);
        addRow.AddChild(kind); AddButton(addRow, "添加子节点", () => AddCreator(kind.GetItemText(kind.Selected)));
        // 当前Creator的复制删除与排序按钮行。
        var nodeRow = new HBoxContainer(); hierarchy.AddChild(nodeRow);
        AddButton(nodeRow, "复制", DuplicateCreator); AddButton(nodeRow, "删除", DeleteCreator);
        AddButton(nodeRow, "↑", () => MoveCreator(-1)); AddButton(nodeRow, "↓", () => MoveCreator(1));
        // 场地画布和属性面板的分栏容器。
        var content = new HSplitContainer { SplitOffsets = new[] { 730 } }; workspace.AddChild(content);
        // 中央画布及场地说明的纵向容器。
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(center);
        center.AddChild(new Label { Text = "场地 1280 × 800  ·  圆形区域 R400  ·  右下为正" });
        Canvas.SizeFlagsVertical = SizeFlags.ExpandFill; center.AddChild(Canvas);
        Canvas.Selected = marker => SelectMarker(marker);
        center.AddChild(new Label { Text = "基础布局：仅基础项；子树参考父第一项。路径显示参考锚点。\n重叠图标连续点击切换；动态效果请播放预览。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _tabs.CustomMinimumSize = new Vector2(440, 0); content.AddChild(_tabs);
        // 属性面板的纵向滚动区域。
        var scroll = new ScrollContainer { Name = "属性", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tabs.AddChild(scroll); _properties.SizeFlagsHorizontal = SizeFlags.ExpandFill; scroll.AddChild(_properties);
        // 完整JSON原文及应用按钮所在页面。
        var source = new VBoxContainer { Name = "JSON" }; _tabs.AddChild(source);
        source.AddChild(new Label { Text = "完整原文 · 应用后同步图形界面\n小数字段支持PI/TAU表达式；毫秒与数量使用整数。" });
        _json.SizeFlagsVertical = SizeFlags.ExpandFill; _json.CustomMinimumSize = new Vector2(400, 0);
        _json.GuttersDrawLineNumbers = true; _json.SyntaxHighlighter = new CodeHighlighter(); source.AddChild(_json);
        _json.TextChanged += () => { if (!_refreshing) { _jsonDirty = _json.Text != _jsonBaseline; UpdateTitle(); } };
        AddButton(source, "应用 JSON 草稿", () => { Document.ApplyText(_json.Text); _jsonDirty = false; Refresh(); });
        AddButton(source, "放弃 JSON 草稿", () => DiscardThen(() => { _jsonDirty = false; SyncJson(); UpdateTitle(); }));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new Vector2(0, 45); layout.AddChild(_status);
        _viewport.Size = new Vector2I(640, 400); _viewport.Size2DOverride = new Vector2I(1280, 800); _viewport.Size2DOverrideStretch = true;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; _viewport.Disable3D = true;
        AddChild(_viewport); _viewport.AddChild(Preview);
        SetupDialogs(); Refresh();
        // 可直接用Godot场景启动，再通过参数指定Emitter文件。
        string? argument = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--emitter="));
        if (argument is not null) Guard(() => { Document.Open(ProjectSettings.GlobalizePath(argument[10..])); Refresh(); });
    }
    /// <summary>创建文件对话框和丢弃修改确认。</summary>
    private void SetupDialogs()
    {
        // 当前配置的打开或保存窗口。
        foreach (var dialog in new[] { _open, _save })
        {
            dialog.Access = FileDialog.AccessEnum.Filesystem; dialog.Filters = new[] { "*.json ; Emitter JSON" };
            dialog.CurrentDir = ProjectSettings.GlobalizePath("res://Data/Emitters"); AddChild(dialog);
        }
        _open.FileMode = FileDialog.FileModeEnum.OpenFile; _open.Title = "打开 Emitter JSON";
        _save.FileMode = FileDialog.FileModeEnum.SaveFile; _save.Title = "保存 Emitter JSON";
        _open.FileSelected += path => Guard(() => { Document.Open(path); _selection = "/VNodes"; _jsonDirty = false; Refresh(); });
        _save.FileSelected += path => Guard(() => { Document.Save(path); UpdateTitle(); SetStatus("已保存：" + Document.FilePath, false); });
        _discard.Title = "未保存的修改"; _discard.DialogText = "当前修改尚未保存。继续将放弃这些修改。";
        _discard.OkButtonText = "放弃并继续"; _discard.CancelButtonText = "取消"; AddChild(_discard);
        _discard.Confirmed += () => { var action = _afterDiscard; _afterDiscard = null; if (action is not null) Guard(action); };
        _discard.Canceled += () => _afterDiscard = null;
    }
    /// <summary>文件切换和退出前保护未保存内容。</summary>
    /// <param name="action">确认后执行的操作。</param>
    private void DiscardThen(Action action)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        _jsonDirty = _json.Text != _jsonBaseline;
        if (!Document.Dirty && !_jsonDirty) { action(); return; }
        _afterDiscard = action; _discard.PopupCentered();
    }
    /// <summary>校验并保存或选择目标文件。</summary>
    /// <param name="choosePath">是否强制另存为。</param>
    private void Save(bool choosePath)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        RequireAppliedDraft(); Document.Validate();
        if (choosePath || Document.FilePath.Length == 0) { _save.CurrentFile = Document.FilePath.Length == 0 ? "NewEmitter.json" : Path.GetFileName(Document.FilePath); _save.PopupCentered(new Vector2I(960, 640)); }
        else { Document.Save(Document.FilePath); UpdateTitle(); SetStatus("已保存：" + Document.FilePath, false); }
    }
    /// <summary>阻止未应用草稿与图形修改互相覆盖。</summary>
    private void RequireAppliedDraft()
    {
        _jsonDirty = _json.Text != _jsonBaseline;
        if (_jsonDirty) throw new InvalidOperationException("JSON有未应用草稿，请先在JSON页应用或放弃草稿。");
    }
    /// <summary>完整刷新编辑数据相关界面。</summary>
    public void Refresh()
    {
        StopPreview(); _refreshing = true;
        try { SyncJson(); RebuildTree(); BuildInspector(); ValidateLayout(); UpdateTitle(); }
        finally { _refreshing = false; }
    }
    /// <summary>校验当前数据并刷新基础图标，错误明确显示。</summary>
    private void ValidateLayout()
    {
        try { Canvas.Rebuild(Document.Validate()); SetStatus("校验通过 · 固定种子0 · 60Hz · Boss(640,250) / 玩家(640,600)", false); }
        catch (Exception error) { Canvas.Markers.Clear(); Canvas.QueueRedraw(); SetStatus(error.Message, true); }
    }
    /// <summary>同步完整JSON文本，避免丢失表达式。</summary>
    private void SyncJson()
    {
        // 设置控件文本会触发TextChanged，暂时屏蔽草稿标记。
        bool previous = _refreshing; _refreshing = true;
        _json.Text = Document.Text; _jsonBaseline = _json.Text; _jsonDirty = false; _refreshing = previous;
    }
    /// <summary>更新文件名和未保存状态。</summary>
    private void UpdateTitle() => _title.Text = "弹幕编辑器  /  " + (Document.FilePath.Length == 0 ? "未命名" : Path.GetFileName(Document.FilePath)) + (Document.Dirty || _jsonDirty ? "  ● 未保存" : "  已保存") + (_jsonDirty ? "  · JSON草稿未应用" : "");
    /// <summary>显示可复制的完整诊断，长错误可悬停阅读。</summary>
    /// <param name="message">状态或错误文本。</param>
    /// <param name="error">是否使用错误颜色。</param>
    private void SetStatus(string message, bool error)
    {
        _status.Text = message.Length > 420 ? message[..420] + "…（悬停查看全文）" : message;
        _status.TooltipText = message; _status.Modulate = error ? new Color("ffaba5") : new Color("97c4b8");
    }
    /// <summary>给用户操作附加异常诊断。</summary>
    /// <param name="action">用户请求的操作。</param>
    private void Guard(Action action) { try { action(); } catch (Exception error) { SetStatus(error.Message, true); } }
    /// <summary>创建统一按钮。</summary>
    /// <param name="parent">按钮所属容器。</param>
    /// <param name="text">按钮文本。</param>
    /// <param name="action">点击行为。</param>
    /// <returns>创建的按钮。</returns>
    private Button AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, TooltipText = text }; parent.AddChild(button); button.Pressed += () => Guard(action); return button;
    }
    /// <summary>重建Creator树并保持当前路径选择。</summary>
    private void RebuildTree()
    {
        _tree.Clear(); _items.Clear();
        // 树的Emitter根项。
        var root = _tree.CreateItem(); root.SetText(0, "Emitter · 共用属性"); root.SetMetadata(0, ""); _items[""] = root;
        if (Document.Root["VNodes"] is JsonObject creator) AddTreeCreator(creator, "/VNodes", root);
        if (!_items.ContainsKey(_selection)) _selection = Document.Root["VNodes"] is JsonObject ? "/VNodes" : "";
        _items[_selection].Select(0); Canvas.SelectedPath = _selection; Canvas.QueueRedraw();
    }
    /// <summary>追加一个Creator及其子树。</summary>
    /// <param name="creator">原始JSON声明。</param>
    /// <param name="path">文档指针。</param>
    /// <param name="parent">上层树项。</param>
    private void AddTreeCreator(JsonObject creator, string path, TreeItem parent)
    {
        // 需要显示或移动的当前列表项。
        var item = _tree.CreateItem(parent); _items[path] = item;
        // Creator的原始Core声明。
        var core = creator["Core"] as JsonObject;
        item.SetText(0, (core?["Name"]?.ToString() ?? "未命名") + " · " + (core?["Type"]?.ToString() ?? "复制"));
        item.SetTooltipText(0, path); item.SetMetadata(0, path);
        if (creator["Children"] is JsonArray children)
            // 按声明顺序处理的零基下标。
            for (int index = 0; index < children.Count; index++) if (children[index] is JsonObject child) AddTreeCreator(child, path + "/Children/" + index, item);
    }
    /// <summary>响应树选择并打开属性菜单。</summary>
    private void TreeSelected()
    {
        if (_refreshing) return;
        _selection = _tree.GetSelected()?.GetMetadata(0).AsString() ?? "";
        _tabs.CurrentTab = 0;
        Canvas.SelectedPath = _selection; Canvas.SelectedBasis = 0; Canvas.QueueRedraw(); BuildInspector();
    }
    /// <summary>画布选择定位Creator和基础项；继承子树定位到复制声明。</summary>
    /// <param name="marker">点击的基础位置图标。</param>
    private void SelectMarker(EditorCanvas.Marker marker)
    {
        // 当前操作的文件路径或文档定位指针。
        string path = marker.Path;
        while (!_items.ContainsKey(path) && path.Contains("/Children/")) path = path[..path.LastIndexOf("/Children/", StringComparison.Ordinal)];
        if (!_items.TryGetValue(path, out var item)) return;
        _tabs.CurrentTab = 0;
        item.Select(0); _selection = path; Canvas.SelectedPath = marker.Path; Canvas.SelectedBasis = marker.Basis;
        _tree.ScrollToItem(item); BuildInspector(); Canvas.QueueRedraw();
    }
    /// <summary>为选中Creator增加子节点；Emitter选择下添加到根。</summary>
    /// <param name="type">Creator类型。</param>
    private void AddCreator(string type)
    {
        RequireAppliedDraft();
        // 目标字段或子节点所在的父容器。
        string parent = _selection.Length == 0 ? "/VNodes" : _selection;
        Document.Edit(_ =>
        {
            // 当前操作的Creator对象。
            var node = Document.At(parent)?.AsObject() ?? throw new InvalidOperationException("请先创建VNodes根对象。");
            if (node["Children"] is null) node["Children"] = new JsonArray();
            var children = node["Children"]!.AsArray(); children.Add(EditorSchema.Creator(type)); _selection = parent + "/Children/" + (children.Count - 1);
        });
        Refresh();
    }
    /// <summary>取得选中节点在父Children数组中的位置。</summary>
    /// <returns>父列表及下标。</returns>
    private (JsonArray Parent, int Index) SelectedChild()
    {
        if (!_selection.Contains("/Children/")) throw new InvalidOperationException("根节点不能删除、复制或排序；可在属性面板编辑根类型。");
        // 字段指针的最后分隔位置及对应父容器。
        int split = _selection.LastIndexOf('/');
        return (Document.At(_selection[..split])!.AsArray(), int.Parse(_selection[(split + 1)..]));
    }
    /// <summary>删除选中子树，可撤销。</summary>
    private void DeleteCreator() { RequireAppliedDraft(); Document.Edit(_ => { var (parent, index) = SelectedChild(); parent.RemoveAt(index); }); _selection = _selection[.._selection.LastIndexOf("/Children/", StringComparison.Ordinal)]; Refresh(); }
    /// <summary>复制子树并移除普通名称，复制声明使用独立后缀。</summary>
    private void DuplicateCreator()
    {
        RequireAppliedDraft();
        Document.Edit(_ =>
        {
            var (parent, index) = SelectedChild();
            // 不共享原始节点引用的子树副本。
            var clone = parent[index]!.DeepClone();
            // 名称按全篇已有名字递增，保留子树内部CopySource引用。
            RenameClone(clone!, Document.Text);
            parent.Insert(index + 1, clone);
        });
        Refresh();
    }
    /// <summary>重命名复制子树及其内部复制引用。</summary>
    /// <param name="node">独立克隆。</param>
    /// <param name="existing">当前文档文本，用于避免已有名称。</param>
    private static void RenameClone(JsonNode node, string existing)
    {
        // 先建立整棵树的名称替换，随后同步引用。
        var names = new Dictionary<string, string>();
        /// <summary>为克隆树中的名称分配不冲突的新名称。</summary>
        /// <param name="current">当前克隆Creator。</param>
        void Collect(JsonNode current)
        {
            if (current is JsonObject obj && obj["Core"] is JsonObject core && core["Name"] is JsonValue value)
            {
                // 原名称、新名称及递增后缀，用于避免冲突。
                string name = value.ToString(), next = name + "_2"; int suffix = 2;
                while (existing.Contains(System.Text.Json.JsonSerializer.Serialize(next), StringComparison.Ordinal) || names.ContainsValue(next)) next = name + "_" + ++suffix;
                names[name] = next;
            }
            if (current is JsonObject creator && creator["Children"] is JsonArray children) foreach (var child in children) if (child is not null) Collect(child);
        }
        /// <summary>同步重写名称与内部复制引用。</summary>
        /// <param name="current">当前克隆Creator。</param>
        void Replace(JsonNode current)
        {
            if (current is not JsonObject obj) return;
            if (obj["Core"] is JsonObject core)
            {
                if (core["Name"] is JsonValue name && names.TryGetValue(name.ToString(), out var renamed)) core["Name"] = renamed;
                if (core["CopySource"] is JsonValue source && names.TryGetValue(source.ToString(), out var replacement)) core["CopySource"] = replacement;
            }
            if (obj["Children"] is JsonArray children) foreach (var child in children) if (child is not null) Replace(child);
        }
        Collect(node); Replace(node);
    }
    /// <summary>调整同级Creator顺序，CopySource约束由加载器重新校验。</summary>
    /// <param name="direction">-1上移，1下移。</param>
    private void MoveCreator(int direction)
    {
        RequireAppliedDraft();
        Document.Edit(_ =>
        {
            var (parent, index) = SelectedChild(); int target = index + direction;
            if (target < 0 || target >= parent.Count) return;
            // 当前字段或列表项的原始JSON值。
            var value = parent[index]; parent.RemoveAt(index); parent.Insert(target, value);
            _selection = _selection[..(_selection.LastIndexOf('/') + 1)] + target;
        }); Refresh();
    }
    /// <summary>创建新的预览快照，完整重置角色和随机状态。</summary>
    private void StartPreview()
    {
        RequireAppliedDraft(); Document.Validate();
        Preview.Start(Document.Text); _previewText = Document.Text;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Canvas.PreviewTexture = _viewport.GetTexture(); UpdatePreview();
    }
    /// <summary>确保存在与当前文档一致的预览。</summary>
    private void EnsurePreview() { RequireAppliedDraft(); if (Preview.Emitter is null || _previewText != Document.Text) StartPreview(); }
    /// <summary>切换播放与暂停。</summary>
    private void TogglePlay() { EnsurePreview(); _playing = !_playing; UpdatePreview(); }
    /// <summary>停止并返回可选择图标的静态布局。</summary>
    private void StopPreview()
    {
        _playing = false; if (Preview.IsInsideTree()) Preview.Stop();
        Canvas.PreviewTexture = null; _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        UpdatePreview();
    }
    /// <summary>刷新预览控制与计数。</summary>
    private void UpdatePreview()
    {
        if (_play is not null) _play.Text = _playing ? "Ⅱ 暂停" : "▶ 播放";
        _clock.Text = $"{Preview.Elapsed:F3}s  ·  {Preview.BulletCount} / {BattleConfig.MaxBullets}"; Canvas.QueueRedraw();
    }
    /// <summary>每个物理帧仅推进一次现有战斗固定步。</summary>
    /// <param name="delta">引擎帧间隔，不用于计算战斗时间。</param>
    public override void _PhysicsProcess(double delta)
    {
        if (!_playing) return;
        try { Preview.Advance(); UpdatePreview(); }
        catch (Exception error) { _playing = false; _previewText = ""; UpdatePreview(); SetStatus("预览已暂停：" + error.Message, true); }
    }
    /// <summary>处理文件快捷键，文本框内部撤销由自身处理。</summary>
    /// <param name="input">键盘输入。</param>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return;
        if (key.Keycode == Key.S) { Guard(() => Save(key.ShiftPressed)); AcceptEvent(); }
        if (key.Keycode == Key.O) { Guard(() => DiscardThen(() => _open.PopupCentered(new Vector2I(960, 640)))); AcceptEvent(); }
    }
    /// <summary>窗口关闭前保护未保存内容。</summary>
    /// <param name="what">Godot生命周期通知。</param>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) DiscardThen(() => GetTree().Quit());
    }
}
