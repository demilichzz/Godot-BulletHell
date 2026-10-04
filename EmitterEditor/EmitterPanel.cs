using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>可复用Emitter内容面板，只负责树、表单、布局和预览，不创建工作区或文件窗口。</summary>
public partial class EmitterPanel : Control
{
    /// <summary>当前编辑文档，预览只读取其快照。</summary>
    public EmitterDocument Document { get; private set; } = new();
    /// <summary>是否显示独立Creator导航；目录模式由外部统一树负责导航。</summary>
    public bool ShowHierarchy { get; init; } = true;
    /// <summary>供顶层添加新建、打开和另存按钮的工具栏。</summary>
    public HBoxContainer FileTools { get; } = new();
    /// <summary>请求所属工作区保存；参数表示是否另存。</summary>
    public event Action<bool>? SaveRequested;
    /// <summary>请求工作区确认丢弃草稿，面板不自行创建窗口。</summary>
    public Action<string, Action>? ConfirmRequested { get; init; }
    /// <summary>文档或Creator选择变化，通知外部统一树刷新。</summary>
    public event Action? WorkspaceChanged;
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
    private readonly Dictionary<string, TreeItem> _items = new();
    private string _selection = "/VNodes", _jsonBaseline = "", _previewText = "";
    private bool _refreshing, _playing, _jsonDirty;
    // 只缓存静态显示结果；显式校验、预览和文档事务继续使用正式加载器。
    private readonly EditorLayoutCache _layoutCache = new();
    private Button _play = null!;
    // 文档历史按钮随撤销栈与JSON草稿状态启用。
    private Button _undoButton = null!, _redoButton = null!;
    // 编辑器位置修改选项：关闭时补偿后代的静态世界坐标。
    private bool _moveChildren = true;
    /// <summary>建立内容控件，不修改窗口和引擎全局设置。</summary>
    public override void _Ready()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        // 内容布局填满上层提供的区域，不设置窗口尺寸。
        var layout = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 10); AddChild(layout);
        _title.AddThemeFontSizeOverride("font_size", 21); layout.AddChild(_title);
        // 文件操作与历史控制工具栏。
        var files = FileTools; layout.AddChild(files);
        AddButton(files, "保存", () => SaveRequested?.Invoke(false)).TooltipText = "保存（Ctrl+S）";
        _undoButton = AddButton(files, "撤销", () => ChangeHistory(false));
        _redoButton = AddButton(files, "重做", () => ChangeHistory(true));
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
        // 只过滤画布，完整树始终保留，便于重新选择其他节点。
        var visibility = new OptionButton { TooltipText = "控制静态画布中的图标、父子连线和路径显示范围。", FitToLongestItem = false };
        foreach (string mode in new[] { "显示全部节点", "仅显示当前和子节点", "仅显示当前节点" }) visibility.AddItem(mode);
        visibility.ItemSelected += mode => { Canvas.CancelDrag(); Canvas.DisplayMode = (int)mode; Canvas.QueueRedraw(); }; hierarchy.AddChild(visibility);
        var moveChildren = new CheckButton { Text = "同时更改子节点位置", ButtonPressed = true, TooltipText = "开启：保留子节点相对配置。关闭：补偿后代出生位移，保持静态世界位置；拖动和右侧位置设值共用此规则。" };
        moveChildren.Toggled += enabled => { Canvas.CancelDrag(); _moveChildren = enabled; }; hierarchy.AddChild(moveChildren);
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
        if (!ShowHierarchy)
        {
            // 目录树负责导航；操作控件搬到横向工具栏，隐藏内部树避免两个左栏。
            var tools = new HBoxContainer(); layout.AddChild(tools); layout.MoveChild(tools, workspace.GetIndex());
            visibility.Reparent(tools); moveChildren.Reparent(tools); addRow.Reparent(tools); nodeRow.Reparent(tools);
            hierarchy.Reparent(layout); hierarchy.Hide();
        }
        // 场地画布和属性面板的分栏容器。
        var content = new HSplitContainer { SplitOffsets = new[] { 730 } }; workspace.AddChild(content);
        // 中央画布及场地说明的纵向容器。
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(center);
        center.AddChild(new Label { Text = "场地 1280 × 800  ·  圆形区域 R400  ·  右下为正" });
        Canvas.SizeFlagsVertical = SizeFlags.ExpandFill; center.AddChild(Canvas);
        Canvas.Selected = marker => SelectMarker(marker);
        Canvas.MoveRequested = MoveMarker; Canvas.MoveCanceled = ValidateLayout;
        center.AddChild(new Label { Text = "基础布局：子树参考父第一项；路径瞄准固定玩家位置。\n左键拖动，Esc取消；重叠图标连续点击切换。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _tabs.CustomMinimumSize = new Vector2(440, 0); content.AddChild(_tabs);
        BuildInspectorNavigation();
        // 完整JSON原文及应用按钮所在页面。
        var source = new VBoxContainer { Name = "JSON" }; _tabs.AddChild(source);
        source.AddChild(new Label { Text = "完整原文 · 应用后同步图形界面\n小数字段支持PI/TAU表达式；毫秒与数量使用整数。" });
        _json.SizeFlagsVertical = SizeFlags.ExpandFill; _json.CustomMinimumSize = new Vector2(400, 0);
        _json.GuttersDrawLineNumbers = true; _json.SyntaxHighlighter = new CodeHighlighter(); source.AddChild(_json);
        _json.TextChanged += () => { if (!_refreshing) { _jsonDirty = _json.Text != _jsonBaseline; Document.Draft = _jsonDirty ? _json.Text : null; UpdateTitle(); WorkspaceChanged?.Invoke(); } };
        AddButton(source, "应用 JSON 草稿", () => { Document.ApplyText(_json.Text); _jsonDirty = false; Refresh(); });
        AddButton(source, "放弃 JSON 草稿", () => ConfirmRequested?.Invoke("放弃当前Emitter尚未应用的JSON草稿？", () => { Document.Draft = null; _jsonDirty = false; SyncJson(); UpdateTitle(); }));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new Vector2(0, 45); layout.AddChild(_status);
        _viewport.Size = new Vector2I(640, 400); _viewport.Size2DOverride = new Vector2I(1280, 800); _viewport.Size2DOverrideStretch = true;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; _viewport.Disable3D = true;
        AddChild(_viewport); _viewport.AddChild(Preview);
        Refresh();
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
        Canvas.CancelDrag();
        StopPreview(); _refreshing = true;
        try { SyncJson(); RebuildTree(); BuildInspector(); ValidateLayout(); UpdateTitle(); }
        finally { _refreshing = false; }
        Document.Selection = _selection;
        WorkspaceChanged?.Invoke();
    }
    /// <summary>校验当前数据并刷新基础图标，错误明确显示。</summary>
    private void ValidateLayout()
    {
        try { _layoutCache.ApplyEmitter(Document, Canvas); SetStatus("校验通过 · 拖动图标设置位置 · 路径按静态参考显示 · Boss(640,250) / 玩家(640,600)", false); }
        catch (Exception error) { Canvas.Markers.Clear(); Canvas.Paths.Clear(); Canvas.QueueRedraw(); SetStatus(error.Message, true); }
    }
    /// <summary>同步完整JSON文本，避免丢失表达式。</summary>
    private void SyncJson()
    {
        // 设置控件文本会触发TextChanged，暂时屏蔽草稿标记。
        bool previous = _refreshing; _refreshing = true;
        // CodeEdit统一使用LF，保存文件仍沿用文档序列化格式。
        _jsonBaseline = Document.Text.ReplaceLineEndings("\n"); _json.Text = Document.Draft ?? _jsonBaseline;
        _jsonDirty = Document.Draft is not null; _refreshing = previous;
    }
    /// <summary>更新文件名和未保存状态。</summary>
    private void UpdateTitle()
    {
        _title.Text = "弹幕编辑器  /  " + (Document.FilePath.Length == 0 ? "未命名" : Path.GetFileName(Document.FilePath)) + (Document.Dirty || _jsonDirty ? "  ● 未保存" : "  已保存") + (_jsonDirty ? "  · JSON草稿未应用" : "");
        _undoButton.Disabled = _jsonDirty || !Document.CanUndo;
        _redoButton.Disabled = _jsonDirty || !Document.CanRedo;
        _undoButton.TooltipText = _jsonDirty ? "请先应用或放弃JSON草稿" : "撤销文档修改（Ctrl+Z）；文本框内使用文本撤销";
        _redoButton.TooltipText = _jsonDirty ? "请先应用或放弃JSON草稿" : "重做文档修改（Ctrl+Y / Ctrl+Shift+Z）；文本框内使用文本重做";
    }
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
        // 新文档或删除数组项后，选中下标不能停留在已经不存在的基础项。
        if (Document.At(_selection) is JsonObject selected && selected["BaseAttributes"] is JsonArray bases)
            Canvas.SelectedBasis = Math.Clamp(Canvas.SelectedBasis, 0, Math.Max(0, bases.Count - 1));
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
        Document.Selection = _selection; WorkspaceChanged?.Invoke();
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
        _tree.ScrollToItem(item); RevealSelectedBasis(); Canvas.QueueRedraw();
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
    internal void DeleteCreator() { RequireAppliedDraft(); Document.Edit(_ => { var (parent, index) = SelectedChild(); parent.RemoveAt(index); }); _selection = _selection[.._selection.LastIndexOf("/Children/", StringComparison.Ordinal)]; Refresh(); }
    /// <summary>复制子树并移除普通名称，复制声明使用独立后缀。</summary>
    internal void DuplicateCreator()
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
    internal void MoveCreator(int direction)
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
        Canvas.CancelDrag();
        RequireAppliedDraft();
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
        if (!IsVisibleInTree() || input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return;
        if (key.Keycode == Key.S) { Guard(() => SaveRequested?.Invoke(key.ShiftPressed)); AcceptEvent(); }
        if (key.Keycode == Key.F && _tabs.CurrentTab == 0) { _propertySearch.GrabFocus(); _propertySearch.SelectAll(); AcceptEvent(); }
        // 文本输入保留控件自身的撤销记录，不回退整份文档。
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (key.Keycode is Key.Z or Key.Y)
        {
            Guard(() => ChangeHistory(key.Keycode == Key.Y || key.ShiftPressed)); AcceptEvent();
        }
    }
}
