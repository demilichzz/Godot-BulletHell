using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>整体Boss编辑工作区，保留独立文档、阶段队列、属性表单与正式战斗预览。</summary>
public partial class BossEditorPanel : VBoxContainer
{
    /// <summary>本模式独立的Boss文档及撤销栈。</summary>
    public EditorDocument Document => _selectedBoss ?? Session.Root!;
    /// <summary>仅持有目录和Boss文件的独立会话。</summary>
    public BossEditorSession Session { get; init; } = null!;
    /// <summary>顶层提供的文件窗口服务，不由内容面板另建窗口。</summary>
    public EditorFileDialogs Files { get; init; } = null!;
    /// <summary>仅以文件路径请求顶层切换Emitter模式。</summary>
    public event Action<string>? OpenEmitterRequested;
    /// <summary>Boss预览环境；与Emitter模式不会同时运行。</summary>
    public EditorPreview Preview { get; } = new();
    /// <summary>静态移动示意与动态战斗共用画布。</summary>
    public EditorCanvas Canvas { get; } = new();
    /// <summary>当前阶段索引，-1代表Boss共用属性。</summary>
    public int SelectedPhase { get; private set; } = -1;
    /// <summary>包括尚未应用的JSON草稿在内的未保存状态。</summary>
    public bool HasUnsaved => Session.HasUnsaved || HasDraft;
    /// <summary>当前是否有未应用的JSON草稿。</summary>
    public bool HasDraft => _json.Text != _jsonBaseline;
    // 目录内容控件；文件窗口由顶层注入。
    private readonly Tree _phases = new();
    // 当前Boss静态布局及依赖检查不参与预览实例的生命周期。
    private readonly EditorLayoutCache _layoutCache = new();
    private readonly VBoxContainer _fields = new();
    private readonly CodeEdit _json = new();
    private readonly Label _title = new(), _status = new(), _clock = new();
    private readonly SubViewport _viewport = new();
    private Button _play = null!;
    // 刷新保护、播放状态及文本快照。
    private bool _refreshing, _playing;
    private string _jsonBaseline = "", _previewText = "";

    /// <summary>建立专用Boss界面，所有操作共用正式数据验证。</summary>
    public override void _Ready()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        _title.AddThemeFontSizeOverride("font_size", 21); AddChild(_title);
        // 文件及历史工具栏。
        var files = new HBoxContainer(); AddChild(files);
        Button(files, "新建目录", () => ReplaceRootThen(() => { Session.New(EditorDocumentKind.BossCatalog); Refresh(); }));
        Button(files, "新建 Boss", () => ReplaceRootThen(() => { Session.New(EditorDocumentKind.Boss); SelectBoss(0); }));
        Button(files, "打开…", () => Files.Open("打开Boss目录或Boss数据", "res://Data", Open));
        Button(files, "保存当前文件", () => Save(false));
        Button(files, "另存为…", () => Save(true));
        Button(files, "保存全部", () => { Suspend(); Session.SaveAll(); Refresh(); });
        Button(files, "撤销", () => ChangeHistory(false));
        Button(files, "重做", () => ChangeHistory(true));
        Button(files, "校验当前文件", () => { RequireApplied(); Document.ValidateCurrent(); Status("当前文件及磁盘引用校验通过。"); });
        // 正式60Hz预览与阶段调试工具栏。
        var controls = new HBoxContainer(); _bossControls = controls; AddChild(controls);
        _play = Button(controls, "▶ 播放", () => { EnsurePreview(); _playing = !_playing; UpdateClock(); });
        Button(controls, "单步 1/60s", () => { EnsurePreview(); _playing = false; Preview.Advance(); UpdateClock(); });
        Button(controls, "重置预览", StartPreview);
        Button(controls, "返回布局", StopPreview);
        Button(controls, "预览此阶段", () =>
        {
            StartPreview();
            // 数组操作捕获本次显示的零基序号。
            for (int index = 0; index < SelectedPhase; index++) Preview.Boss!.TrySwitchAdjacentPhase(1);
            UpdateClock();
        });
        Button(controls, "阶段伤害 −100", () => { EnsurePreview(); Preview.Boss!.TakeDamage(100); Preview.Advance(); UpdateClock(); });
        controls.AddChild(_clock);
        // 左阶段队列、中画布、右属性/原文三个区域。
        var workspace = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = new[] { 225 } }; AddChild(workspace);
        var hierarchy = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) }; workspace.AddChild(hierarchy);
        hierarchy.AddChild(new Label { Text = "Boss 列表 → Boss → 阶段" });
        _phases.SizeFlagsVertical = SizeFlags.ExpandFill; hierarchy.AddChild(_phases);
        _phases.ItemSelected += CatalogTreeSelected;
        _catalogButtons.Add(Button(hierarchy, "+ 新建Boss文件…", AddBoss));
        _catalogButtons.Add(Button(hierarchy, "+ 引用Boss文件…", AddBossReference));
        _addPhase = Button(hierarchy, "+ 添加阶段", AddPhase);
        var actions = new HBoxContainer(); hierarchy.AddChild(actions);
        _duplicate = Button(actions, "复制", DuplicateSelection); _delete = Button(actions, "删除", DeleteSelection);
        _up = Button(actions, "↑", () => MoveSelection(-1)); _down = Button(actions, "↓", () => MoveSelection(1));
        var contentHost = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; workspace.AddChild(contentHost);
        var content = new HSplitContainer { SplitOffsets = new[] { 675 }, SizeFlagsVertical = SizeFlags.ExpandFill };
        contentHost.AddChild(content);
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(center);
        center.AddChild(new Label { Text = "场地 1280 × 800 · 坐标右下为正 · 速度：像素/秒" });
        Canvas.SizeFlagsVertical = SizeFlags.ExpandFill; center.AddChild(Canvas);
        Canvas.Selected = _ => { };
        Canvas.TooltipText = "显示Boss出生点及所选阶段的移动范围。动态移动与弹幕请播放预览。";
        center.AddChild(new Label { Text = "独立血池：超额伤害不带入下一阶段。\n超时清空当前血池；末阶段满足条件后胜利。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        // 表单与完整JSON使用同一右侧标签区。
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(470, 0) }; content.AddChild(tabs);
        // 独立滚动容器容纳较长的阶段及路径字段。
        var scroll = new ScrollContainer { Name = "属性", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; tabs.AddChild(scroll);
        _fields.SizeFlagsHorizontal = SizeFlags.ExpandFill; scroll.AddChild(_fields);
        // JSON页保持完整原文及未应用草稿。
        var source = new VBoxContainer { Name = "JSON" }; tabs.AddChild(source);
        source.AddChild(new Label { Text = "当前文件完整 JSON · 列表只保存路径，Boss文件保存Core与Phases" });
        _json.SizeFlagsVertical = SizeFlags.ExpandFill; _json.GuttersDrawLineNumbers = true; _json.SyntaxHighlighter = new CodeHighlighter(); source.AddChild(_json);
        _json.TextChanged += () => { if (!_refreshing) { if (_shownDocument is not null) _shownDocument.Draft = HasDraft ? _json.Text : null; UpdateTitle(); } };
        Button(source, "应用 JSON 草稿", () => { Document.ApplyText(_json.Text); Refresh(); });
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new Vector2(0, 44); AddChild(_status);
        _viewport.Size = new Vector2I(640, 400); _viewport.Size2DOverride = new Vector2I(1280, 800); _viewport.Size2DOverrideStretch = true;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; _viewport.Disable3D = true;
        AddChild(_viewport); _viewport.AddChild(Preview);
        Session.EnsureRoot();
        Refresh();
    }
    /// <summary>载入Boss文件，失败时保留原文档。</summary>
    /// <param name="path">文件绝对路径或res://路径。</param>
    public void Open(string path) => ReplaceRootThen(() => { Session.Open(path); Refresh(); });
    /// <summary>选择Boss共用属性或指定阶段。</summary>
    /// <param name="index">-1为Boss；其他值为零基阶段序号。</param>
    public void SelectPhase(int index)
    {
        SelectBoss(SelectedBossIndex < 0 ? 0 : SelectedBossIndex, index);
    }
    /// <summary>刷新阶段队列、表单、JSON和静态布局。</summary>
    public void Refresh()
    {
        RefreshCatalog();
    }
    /// <summary>生成当前选择的完整表单，包括VPath嵌套结构。</summary>
    private void BuildInspector()
    {
        foreach (Node child in _fields.GetChildren()) { _fields.RemoveChild(child); child.QueueFree(); }
        if (_selectedBoss is null || SelectedBossIndex < 0) { BuildCatalogFields(); return; }
        // 队列选择映射到Boss核心或具体阶段的JSON指针。
        string path = SelectedPhase < 0 ? "/Core" : "/Phases/" + SelectedPhase;
        if (At(path) is not JsonObject value) { _fields.AddChild(new Label { Text = "结构无效，请在JSON页修复。" }); return; }
        _fields.AddChild(new Label { Text = SelectedPhase < 0 ? "Boss共用属性" : $"阶段 {SelectedPhase + 1:00}", ThemeTypeVariation = "HeaderLarge" });
        _fields.AddChild(new Label { Text = SelectedPhase < 0 ? "总血量须等于各阶段血池之和。阶段血量编辑会自动更新合计。" : "时间单位：整数毫秒。仅血量允许null时限；两者皆可表示任一满足。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        ObjectFields(_fields, value, SelectedPhase < 0 ? typeof(BossCoreDefinition) : typeof(BossPhaseDefinition), path, "");
    }
    /// <summary>显示已有字段及可添加的缺省属性。</summary>
    /// <param name="parent">表单容器。</param>
    /// <param name="value">当前JSON对象。</param>
    /// <param name="type">属性类型。</param>
    /// <param name="path">JSON指针。</param>
    /// <param name="context">字段名。</param>
    private void ObjectFields(VBoxContainer parent, JsonObject value, Type type, string path, string context)
    {
        if (type == typeof(VPathSegmentAttribute))
            Button(parent, value.ContainsKey("Type") ? "切为普通路径" : "切为瞄准玩家直线",
                () => Change(_ => Set(path, EditorSchema.PathSegment(!value.ContainsKey("Type")))));
        // 运行属性类型提供当前模式的可编辑字段。
        var fields = BossEditorSchema.Fields(type, value, context);
        // 按声明顺序保留字段，包括待修复的未知输入。
        foreach (var pair in value.ToArray())
        {
            // 匹配字段类型，未匹配项保持原文供修复。
            var field = fields.FirstOrDefault(candidate => candidate.Name == pair.Key);
            ValueField(parent, pair.Value, field?.ValueType ?? typeof(string), path + "/" + EditorDocument.Escape(pair.Key), pair.Key);
        }
        // 可选字段仍可逐项添加，非法文档保留原始键供JSON页修复。
        // 缺省属性通过显式添加按钮写入文档。
        var missing = fields.Where(field => !value.ContainsKey(field.Name)).ToArray();
        if (missing.Length == 0) return;
        // 字段标签、输入框或添加菜单所在行。
        var row = new HBoxContainer(); parent.AddChild(row);
        // 可选字段菜单保持中文名与真实JSON键的对应。
        var menu = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        // 按元数据顺序列出尚未填写的属性。
        foreach (var field in missing) menu.AddItem(BossEditorSchema.Label(field.Name));
        row.AddChild(menu);
        Button(row, "+ 属性", () => Change(_ => At(path)![missing[menu.Selected].Name] = missing[menu.Selected].Default?.DeepClone()));
    }
    /// <summary>递归显示对象、数组和标量。</summary>
    /// <param name="parent">显示容器。</param>
    /// <param name="value">当前JSON值。</param>
    /// <param name="type">运行字段类型。</param>
    /// <param name="path">JSON指针。</param>
    /// <param name="name">字段名。</param>
    private void ValueField(VBoxContainer parent, JsonNode? value, Type type, string path, string name)
    {
        if (value is JsonObject obj)
        {
            parent.AddChild(new HSeparator()); parent.AddChild(new Label { Text = BossEditorSchema.Label(name) });
            // 嵌套对象仅增加左侧缩进。
            var inset = new MarginContainer(); inset.AddThemeConstantOverride("margin_left", 12); parent.AddChild(inset);
            // 嵌套字段的独立纵向容器。
            var body = new VBoxContainer(); inset.AddChild(body);
            ObjectFields(body, obj, type, path, name); return;
        }
        if (value is JsonArray array)
        {
            parent.AddChild(new HSeparator()); parent.AddChild(new Label { Text = $"{BossEditorSchema.Label(name)} [{array.Count}]" });
            // 数组元素按运行类型构建表单。
            Type itemType = EditorSchema.ElementType(type) ?? typeof(string);
            // 数组操作捕获本次显示的零基序号。
            for (int index = 0; index < array.Count; index++)
            {
                // 固定闭包序号，刷新前不引用循环的后续值。
                int position = index;
                // 数组项排序与删除按钮行。
                var actions = new HBoxContainer(); parent.AddChild(actions);
                actions.AddChild(new Label { Text = $"[{index}]", SizeFlagsHorizontal = SizeFlags.ExpandFill });
                EditorArrayControls.AddActions(actions, path + "/" + position, position, array.Count, Guard, () => _refreshing,
                    direction => Shift(path, position, direction),
                    remove: () => Change(_ => At(path)!.AsArray().RemoveAt(position)), removeText: "删除项");
                if (name == "Emitters") EmitterReferenceField(parent, path + "/" + index, array[index]?.ToString() ?? "");
                else ValueField(parent, array[index], itemType, path + "/" + index, name + "项");
            }
            if (name == "Emitters")
                Button(parent, "+ 引用Emitter文件…", () => { RequireApplied(); Files.Open("引用Emitter文件", "res://Data/Emitters", selected => Guard(() => Change(_ => At(path)!.AsArray().Add(ProjectSettings.LocalizePath(selected)))), true); });
            else Button(parent, "+ 添加项", () => Change(_ => At(path)!.AsArray().Add(EditorSchema.Item(itemType, name, "XYMove"))));
            return;
        }
        // 字段标签、输入框或添加菜单所在行。
        var row = new HBoxContainer(); parent.AddChild(row);
        row.AddChild(new Label { Text = BossEditorSchema.Label(name), CustomMinimumSize = new Vector2(172, 0), CustomMaximumSize = new Vector2(192, -1), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        // 固定协议字符串用下拉选择，存储值保持英文键。
        var choices = EditorSchema.Choices(name, Nullable.GetUnderlyingType(type) ?? type, path, "");
        if (choices.Length > 0)
        {
            var menu = EditorFieldControls.Choice(value, choices, path, Guard, () => _refreshing,
                choice => Change(root => Choose(path, choice, root)), label: BossEditorSchema.ChoiceLabel);
            row.AddChild(menu); return;
        }
        if (type == typeof(bool) && value?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
        {
            var toggle = EditorFieldControls.Toggle(value.GetValue<bool>(), path, Guard, () => _refreshing,
                enabled => Change(_ => Set(path, JsonValue.Create(enabled))));
            row.AddChild(toggle); return;
        }
        // 输入提交共用基础控件；Boss血量汇总仍在同一文档事务中执行。
        var input = EditorFieldControls.Text(value, type, path, Guard, () => _refreshing,
            parsed => Change(root => { Set(path, parsed); if (name == "Hp") BossEditorSchema.SumHealth(root); }));
        input.CustomMinimumSize = new Vector2(110, 0); row.AddChild(input);
    }
    /// <summary>应用枚举选择并提供新模式的必需字段。</summary>
    /// <param name="path">字段指针。</param>
    /// <param name="choice">真实JSON枚举值。</param>
    /// <param name="root">Boss文档。</param>
    private void Choose(string path, string choice, JsonObject root)
    {
        // 定位所属属性组，以原子方式切换模式配置。
        string parentPath = path[..path.LastIndexOf('/')];
        if (path.EndsWith("/Movement/Type")) { Set(parentPath, BossEditorSchema.Movement(choice)); return; }
        Set(path, JsonValue.Create(choice));
        // 当前模式所属的JSON对象。
        var parent = At(parentPath)!.AsObject();
        if (path.EndsWith("/EndCondition") && choice != "Health" && parent["DurationMs"] is null) parent["DurationMs"] = 60000;
        if (!path.EndsWith("/PathMode")) return;
        EditorSchema.ChangePathMode(parent, choice);
    }
    /// <summary>将独立JSON值写入指针位置。</summary>
    /// <param name="path">非空JSON指针。</param>
    /// <param name="value">独立JSON节点。</param>
    private void Set(string path, JsonNode? value)
    {
        // 分离父指针与末级字段或数组下标。
        int split = path.LastIndexOf('/'); var parent = At(path[..split]);
        // 解除JSON Pointer对斜线和波浪号的转义。
        string key = path[(split + 1)..].Replace("~1", "/").Replace("~0", "~");
        if (parent is JsonArray array) array[int.Parse(key)] = value; else parent!.AsObject()[key] = value;
    }
    /// <summary>一次文档事务并延迟刷新，避免销毁正在派发信号的输入框。</summary>
    /// <param name="change">修改完整Boss文档的回调。</param>
    private void Change(Action<JsonObject> change)
    {
        RequireApplied(); RememberSelection();
        Document.Edit(root => { change(root); RememberSelection(); }); StopPreview();
        Callable.From(() => { if (IsInsideTree()) Refresh(); }).CallDeferred();
    }
    /// <summary>追加独立阶段，自动同步Boss总血量。</summary>
    public void AddPhase() => Change(root => { var phases = root["Phases"]!.AsArray(); phases.Add(BossEditorSchema.Phase()); SelectedPhase = phases.Count - 1; BossEditorSchema.SumHealth(root); });
    /// <summary>深复制选中阶段，不共享可变JSON对象。</summary>
    private void DuplicatePhase() => Change(root => { RequirePhase(); var phases = root["Phases"]!.AsArray(); EditorArrayControls.Duplicate(phases, SelectedPhase); SelectedPhase++; BossEditorSchema.SumHealth(root); });
    /// <summary>删除选中阶段；至少保留一个阶段。</summary>
    private void DeletePhase() => Change(root => { RequirePhase(); var phases = root["Phases"]!.AsArray(); if (phases.Count == 1) throw new InvalidOperationException("至少保留一个阶段。"); phases.RemoveAt(SelectedPhase); SelectedPhase = Math.Min(SelectedPhase, phases.Count - 1); BossEditorSchema.SumHealth(root); });
    /// <summary>调整阶段队列顺序。</summary>
    /// <param name="direction">-1向上，1向下。</param>
    private void MovePhase(int direction) { RequirePhase(); Shift("/Phases", SelectedPhase, direction); }
    /// <summary>稳定移动任意有序数组中的一项。</summary>
    /// <param name="path">数组JSON指针。</param>
    /// <param name="index">原零基序号。</param>
    /// <param name="direction">-1或1。</param>
    private void Shift(string path, int index, int direction) => Change(_ =>
    {
        // 只有实际移动后才改变阶段选择，嵌套队列不影响当前阶段。
        if (EditorArrayControls.Move(At(path)!.AsArray(), index, direction) && path == "/Phases")
            SelectedPhase = index + direction;
    });
    /// <summary>阻止对Boss共用项使用阶段操作。</summary>
    private void RequirePhase() { if (SelectedPhase < 0) throw new InvalidOperationException("请先选择一个阶段。"); }
    /// <summary>保护尚未应用的JSON草稿。</summary>
    private void RequireApplied() { if (HasDraft) throw new InvalidOperationException("请先应用JSON草稿，再执行此操作。"); }
    /// <summary>保存当前文件；另存创建副本，成功后独立打开，不改写原引用。</summary>
    /// <param name="choosePath">是否总是请求另存。</param>
    private void Save(bool choosePath)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus(); RequireApplied();
        var document = Document;
        /// <summary>保存捕获的文档，不受文件窗口期间选择变化影响。</summary>
        /// <param name="path">目标路径。</param>
        void Write(string path)
        {
            if (choosePath && document.FilePath.Length > 0) Session.SaveCopy(document, path, true);
            else Session.Save(document, path);
            Refresh(); Status("已保存：" + path);
        }
        if (choosePath || document.FilePath.Length == 0)
            Files.Save("保存当前文件", "res://Data", document.FilePath.Length == 0
                ? document.Kind == EditorDocumentKind.BossCatalog ? "BossCatalog.json" : "NewBoss.json" : Path.GetFileName(document.FilePath), Write);
        else Write(document.FilePath);
    }
    /// <summary>只在替换未命名工作区时确认丢弃；打开失败不移除原文档。</summary>
    /// <param name="action">成功后切换根文档的操作。</param>
    private void ReplaceRootThen(Action action)
    {
        Suspend(); var previous = Session.Root;
        /// <summary>完成切换后才注销旧的未命名文档。</summary>
        void Run()
        {
            action();
            if (previous is not null && previous.FilePath.Length == 0 && !ReferenceEquals(previous, Session.Root)) Session.DiscardUntitled(previous);
        }
        if (previous is not null && previous.FilePath.Length == 0 && (previous.Dirty || previous.Draft is not null))
            Files.Confirm("当前未命名Boss或目录尚未保存，继续将放弃其内容及JSON草稿。", Run);
        else Run();
    }
    /// <summary>撤销或重做当前文件，目录历史不会把下一次历史操作切换到Boss文件。</summary>
    /// <param name="redo">为真时重做，否则撤销。</param>
    private void ChangeHistory(bool redo)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus(); RequireApplied(); var document = Document;
        if (redo) document.Redo(); else document.Undo();
        if (document.Kind == EditorDocumentKind.BossCatalog) document.Selection = "-1";
        Refresh();
    }
    /// <summary>切出目录模式前保留草稿并停止全部预览。</summary>
    public void Suspend()
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (_shownDocument is not null) _shownDocument.Draft = HasDraft ? _json.Text : null;
        StopPreview();
    }
    /// <summary>接收顶层文件窗口的操作诊断。</summary>
    /// <param name="message">完整诊断。</param>
    /// <param name="error">是否为错误。</param>
    internal void ReportWorkspaceStatus(string message, bool error) => Status(message, error);
    /// <summary>创建完整Boss战斗预览，时钟与随机从固定初态开始。</summary>
    private void StartPreview()
    {
        RequireApplied();
        Preview.StartBoss(SelectedBossRoot.ToJsonString(), BossEditorSession.CaptureDiskEmitters()); _previewText = Document.Text; _playing = false;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always; Canvas.PreviewTexture = _viewport.GetTexture(); UpdateClock();
    }
    /// <summary>保证预览对应当前已应用文档。</summary>
    private void EnsurePreview() { RequireApplied(); if (Preview.Boss is null || _previewText != Document.Text) StartPreview(); }
    /// <summary>释放模式预览并恢复静态布局，不修改文档或草稿。</summary>
    public void StopPreview()
    {
        _playing = false; if (Preview.IsInsideTree()) Preview.Stop();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; Canvas.PreviewTexture = null; UpdateClock();
    }
    /// <summary>仅在当前可见模式播放时推进60Hz正式固定步。</summary>
    /// <param name="delta">引擎秒数，不作为业务时钟输入。</param>
    public override void _PhysicsProcess(double delta)
    {
        if (!_playing || !IsVisibleInTree()) return;
        Guard(() => { Preview.Advance(); if (Preview.Victory) _playing = false; UpdateClock(); });
    }
    /// <summary>刷新计时、血池、阶段和预览胜利状态。</summary>
    private void UpdateClock()
    {
        if (_play is not null) _play.Text = _playing ? "Ⅱ 暂停" : "▶ 播放";
        _clock.Text = Preview.Boss is { } boss ? $"{Preview.Elapsed:F2}s · 阶段{boss.PhaseIndex + 1}/{boss.PhaseCount} · HP {boss.PhaseHp}/{boss.PhaseMaxHp}" + (Preview.Victory ? " · 胜利" : "") : "60Hz · 固定种子0";
        Canvas.QueueRedraw();
    }
    /// <summary>校验文档并显示无随机消耗的出生与目标布局。</summary>
    private void ValidateCanvas()
    {
        Canvas.Markers.Clear(); Canvas.Paths.Clear();
        if (_selectedBoss is null || SelectedBossIndex < 0) { Canvas.QueueRedraw(); return; }
        try
        {
            Status(_layoutCache.ApplyBoss(Document, SelectedPhase, Canvas));
        }
        catch (Exception error) { Status(error.Message, true); }
        Canvas.QueueRedraw();
    }
    /// <summary>更新当前文档和草稿的保存状态。</summary>
    private void UpdateTitle() => _title.Text = (Document.Kind == EditorDocumentKind.BossCatalog ? "Boss 目录  /  " : "Boss 数据  /  ") + (Document.FilePath.Length == 0 ? "未命名文件" : Path.GetFileName(Document.FilePath)) + (Document.Dirty || Document.Draft is not null ? "  ● 未保存" : "  已保存") + (HasDraft ? " · JSON草稿未应用" : "");
    /// <summary>显示状态，完整内容保留在悬停说明。</summary>
    /// <param name="message">完整诊断。</param>
    /// <param name="error">是否为错误。</param>
    private void Status(string message, bool error = false)
    {
        _status.Text = message.Length > 350 ? message[..350] + "…" : message;
        _status.TooltipText = message; _status.Modulate = error ? new Color("ffaba5") : new Color("97c4b8");
    }
    /// <summary>统一捕获用户操作错误并停止失败预览。</summary>
    /// <param name="action">请求执行的操作。</param>
    private void Guard(Action action) { try { action(); } catch (Exception error) { _playing = false; UpdateClock(); Status(error.Message, true); } }
    /// <summary>建立带错误保护的普通按钮。</summary>
    /// <param name="parent">父节点。</param>
    /// <param name="text">按钮文本。</param>
    /// <param name="action">点击操作。</param>
    /// <returns>已绑定的按钮。</returns>
    private Button Button(Node parent, string text, Action action) { var button = new Button { Text = text }; parent.AddChild(button); button.Pressed += () => Guard(action); return button; }
    /// <summary>在本模式处理保存快捷键。</summary>
    /// <param name="input">用户键盘输入。</param>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        // 模式只处理自身支持的文件动作。
        var action = EditorInput.Shortcut(input);
        if (action is EditorAction.Save or EditorAction.SaveAs) { Guard(() => Save(action == EditorAction.SaveAs)); AcceptEvent(); }
        if (action == EditorAction.Open) { Guard(() => Files.Open("打开Boss目录或Boss数据", "res://Data", Open)); AcceptEvent(); }
    }
}
