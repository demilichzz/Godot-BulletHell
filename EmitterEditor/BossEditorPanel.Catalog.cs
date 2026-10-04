using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>统一导航目录、Boss、阶段、Emitter引用与Creator，所有目录操作共用一份历史。</summary>
public partial class BossEditorPanel
{
    /// <summary>当前Boss零基下标；负一代表目录根。</summary>
    public int SelectedBossIndex { get; private set; } = -1;
    /// <summary>选中Boss的原始数据，修改须经过文档事务。</summary>
    public JsonObject SelectedBossRoot => Document.Root["Bosses"]?[SelectedBossIndex]?.AsObject()
        ?? throw new InvalidOperationException("请先选择有效Boss。");
    /// <summary>按需建立的共享Emitter编辑内容。</summary>
    public EmitterPanel? EmitterPanel { get; private set; }
    // 当前Emitter引用及相对Creator指针，不作为业务标识保存。
    private int _selectedEmitter = -1;
    private string _creatorPath = "";
    private HSplitContainer _bossContent = null!;
    private HBoxContainer _bossControls = null!;
    private VBoxContainer _contentHost = null!;
    private readonly Dictionary<string, TreeItem> _catalogItems = new();
    private bool _emitterRefreshQueued;

    /// <summary>将Boss相对字段指针映射到完整目录。</summary>
    /// <param name="path">以Core或Phases开头的相对指针。</param>
    /// <returns>原文节点，缺失时为空。</returns>
    private JsonNode? At(string path) => Document.At("/Bosses/" + SelectedBossIndex + path);

    /// <summary>选择Boss或其阶段，保留所有文档草稿。</summary>
    /// <param name="boss">Boss零基下标，-1选择目录。</param>
    /// <param name="phase">阶段零基下标，-1选择Boss。</param>
    public void SelectBoss(int boss, int phase = -1)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        SelectedBossIndex = boss; SelectedPhase = phase; _selectedEmitter = -1; _creatorPath = "";
        RememberSelection(); Refresh();
    }

    /// <summary>从阶段引用进入同一会话中的Emitter和Creator。</summary>
    /// <param name="boss">Boss零基下标。</param>
    /// <param name="phase">阶段零基下标。</param>
    /// <param name="emitter">引用在阶段内的零基下标。</param>
    /// <param name="creatorPath">相对Emitter的Creator指针，空值选择Emitter属性。</param>
    public void SelectEmitter(int boss, int phase, int emitter, string creatorPath = "")
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        SelectedBossIndex = boss; SelectedPhase = phase; _selectedEmitter = emitter; _creatorPath = creatorPath;
        RememberSelection(); Refresh();
    }

    /// <summary>记录选择，使撤销和重做恢复操作前后的正确对象。</summary>
    private void RememberSelection() => Document.Selection = SelectionKey(SelectedBossIndex, SelectedPhase, _selectedEmitter, _creatorPath);

    /// <summary>生成仅用于编辑会话的树项键。</summary>
    /// <param name="boss">Boss下标。</param>
    /// <param name="phase">阶段下标。</param>
    /// <param name="emitter">引用下标。</param>
    /// <param name="creator">Creator相对指针。</param>
    /// <returns>可恢复的选择文本，不写入游戏JSON。</returns>
    private static string SelectionKey(int boss, int phase, int emitter = -1, string creator = "") => $"{boss}/{phase}/{emitter}|{creator}";

    /// <summary>恢复历史中的树位置并把已失效选择收敛到有效父项。</summary>
    private void RestoreSelection()
    {
        // 空文档状态默认选择目录根。
        string[] parts = Document.Selection.Split('|');
        string[] indexes = parts[0].Split('/');
        SelectedBossIndex = indexes.Length == 3 && int.TryParse(indexes[0], out int boss) ? boss : -1;
        SelectedPhase = indexes.Length == 3 && int.TryParse(indexes[1], out int phase) ? phase : -1;
        _selectedEmitter = indexes.Length == 3 && int.TryParse(indexes[2], out int emitter) ? emitter : -1;
        _creatorPath = parts.Length == 2 ? parts[1] : "";
        // 只校验结构边界，业务非法数据仍允许在属性或原文中修复。
        int count = (Document.Root["Bosses"] as JsonArray)?.Count ?? 0;
        SelectedBossIndex = Math.Clamp(SelectedBossIndex, -1, count - 1);
        int phases = SelectedBossIndex < 0 ? 0 : (SelectedBossRoot["Phases"] as JsonArray)?.Count ?? 0;
        SelectedPhase = Math.Clamp(SelectedPhase, -1, phases - 1);
        int emitters = SelectedPhase < 0 ? 0 : (SelectedBossRoot["Phases"]?[SelectedPhase]?["Emitters"] as JsonArray)?.Count ?? 0;
        _selectedEmitter = Math.Clamp(_selectedEmitter, -1, emitters - 1);
        RememberSelection();
    }

    /// <summary>刷新统一树与选中的编辑内容，不丢弃原文草稿。</summary>
    private void RefreshCatalog()
    {
        StopPreview(); EmitterPanel?.StopWorkspacePreview(); _refreshing = true;
        try
        {
            RestoreSelection();
            // CodeEdit统一使用LF，不能用Windows序列化换行判定草稿。
            _jsonBaseline = Document.Text.ReplaceLineEndings("\n"); _json.Text = Document.Draft ?? _jsonBaseline;
            _bossContent.Visible = _bossControls.Visible = _selectedEmitter < 0;
            // 嵌入内容使用自身校验状态，避免两条状态栏挤占画布高度。
            _status.Visible = _selectedEmitter < 0;
            if (EmitterPanel is not null) EmitterPanel.Visible = _selectedEmitter >= 0;
            if (_selectedEmitter >= 0)
            {
                // 引用只打开会话中的唯一文档，重复引用共享同一撤销栈。
                string path = SelectedBossRoot["Phases"]![SelectedPhase]!["Emitters"]![_selectedEmitter]!.GetValue<string>();
                var document = Session.OpenEmitter(path);
                if (EmitterPanel is null)
                {
                    EmitterPanel = new EmitterPanel { ShowHierarchy = false, ConfirmRequested = Files.Confirm, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    // 引用面板只保存当前引用；另存由独立文件模式提供。
                    EmitterPanel.SaveRequested += _ => SaveEmitterRequested?.Invoke(EmitterPanel, false);
                    _contentHost.AddChild(EmitterPanel);
                    EmitterPanel.WorkspaceChanged += EmitterChanged;
                }
                EmitterPanel.UseDocument(document, _creatorPath);
                _creatorPath = EmitterPanel.SelectedCreator; RememberSelection();
            }
            else { BuildInspector(); ValidateCanvas(); }
            RebuildCatalogTree(); UpdateTitle();
        }
        catch (Exception error)
        {
            // 引用读取失败时保留目录和原始字段，仍可回到阶段修复路径。
            RebuildCatalogTree(); Status(error.Message, true);
        }
        finally { _refreshing = false; }
    }

    /// <summary>从真实树项恢复选择，延迟刷新避免删除正在派发信号的项。</summary>
    private void CatalogTreeSelected()
    {
        if (_refreshing || _phases.GetSelected() is not { } item) return;
        Document.Selection = item.GetMetadata(0).AsString();
        Callable.From(() => { if (IsInsideTree()) Refresh(); }).CallDeferred();
    }

    /// <summary>建立主层级，只有选中Emitter才展开Creator原文以避免一次加载全部文件。</summary>
    private void RebuildCatalogTree()
    {
        // 保留当前手动展开状态；选中分支总是展开。
        var expanded = _catalogItems.Where(pair => !pair.Value.Collapsed).Select(pair => pair.Key).ToHashSet();
        _phases.Clear(); _catalogItems.Clear();
        var root = AddCatalogItem(null, "Boss 列表", SelectionKey(-1, -1));
        if (Document.Root["Bosses"] is JsonArray bosses)
        {
            // Boss和阶段均严格使用原文数组顺序。
            for (int boss = 0; boss < bosses.Count; boss++)
            {
                var data = bosses[boss] as JsonObject;
                var core = data?["Core"] as JsonObject;
                string key = SelectionKey(boss, -1);
                var item = AddCatalogItem(root, core is null ? "无效Boss · 请在JSON页修复" : $"{core["Id"]} · {core["DisplayName"]}", key);
                item.Collapsed = boss != SelectedBossIndex && !expanded.Contains(key);
                if (data?["Phases"] is not JsonArray phases) continue;
                for (int phase = 0; phase < phases.Count; phase++)
                {
                    key = SelectionKey(boss, phase);
                    var phaseData = phases[phase] as JsonObject;
                    var phaseItem = AddCatalogItem(item, $"{phase + 1:00} · {phaseData?["Name"] ?? "无效阶段"}", key);
                    phaseItem.Collapsed = (boss != SelectedBossIndex || phase != SelectedPhase) && !expanded.Contains(key);
                    if (phaseData?["Emitters"] is not JsonArray emitters) continue;
                    for (int emitter = 0; emitter < emitters.Count; emitter++)
                    {
                        string path = emitters[emitter]?.ToString() ?? "无效引用";
                        var reference = AddCatalogItem(phaseItem, System.IO.Path.GetFileNameWithoutExtension(path), SelectionKey(boss, phase, emitter));
                        reference.SetTooltipText(0, path + "\n修改文件会影响所有引用此文件的阶段；删除引用不会删除文件。");
                        if (boss == SelectedBossIndex && phase == SelectedPhase && emitter == _selectedEmitter
                            && EmitterPanel?.Document.Root["VNodes"] is JsonObject creator)
                            AddCatalogCreator(reference, creator, "/VNodes");
                    }
                }
            }
        }
        if (_catalogItems.TryGetValue(Document.Selection, out var selected)) selected.Select(0);
        else root.Select(0);
    }

    /// <summary>追加带稳定界面定位信息的树项。</summary>
    /// <param name="parent">父项；空值创建根。</param>
    /// <param name="label">显示文本。</param>
    /// <param name="key">会话选择键。</param>
    /// <returns>新树项。</returns>
    private TreeItem AddCatalogItem(TreeItem? parent, string label, string key)
    {
        var item = _phases.CreateItem(parent); item.SetText(0, label); item.SetMetadata(0, key);
        _catalogItems[key] = item; return item;
    }

    /// <summary>将当前Emitter原始Creator树接在引用之下。</summary>
    /// <param name="parent">上层引用或Creator。</param>
    /// <param name="creator">原始Creator声明。</param>
    /// <param name="path">相对Emitter的JSON指针。</param>
    private void AddCatalogCreator(TreeItem parent, JsonObject creator, string path)
    {
        var core = creator["Core"] as JsonObject;
        var item = AddCatalogItem(parent, $"{core?["Name"] ?? "未命名"} · {core?["Type"] ?? "复制"}",
            SelectionKey(SelectedBossIndex, SelectedPhase, _selectedEmitter, path));
        if (creator["Children"] is JsonArray children)
            for (int index = 0; index < children.Count; index++)
                if (children[index] is JsonObject child) AddCatalogCreator(item, child, path + "/Children/" + index);
    }

    /// <summary>合并本帧Emitter变化通知，更新统一树和保存标记。</summary>
    private void EmitterChanged()
    {
        if (_refreshing || _emitterRefreshQueued) return;
        _emitterRefreshQueued = true;
        Callable.From(() =>
        {
            _emitterRefreshQueued = false;
            if (!IsInsideTree() || _selectedEmitter < 0 || EmitterPanel is null) return;
            _creatorPath = EmitterPanel.SelectedCreator; RememberSelection();
            _refreshing = true;
            try { RebuildCatalogTree(); UpdateTitle(); }
            finally { _refreshing = false; }
        }).CallDeferred();
    }

    /// <summary>执行目录数组事务，同时保存操作前后选择。</summary>
    /// <param name="change">修改Boss数组的回调。</param>
    private void ChangeCatalog(Action<JsonArray> change)
    {
        RequireApplied(); RememberSelection();
        Document.Edit(root => { change(root["Bosses"]!.AsArray()); RememberSelection(); });
        Refresh();
    }

    /// <summary>添加一个通用数据Boss，分配唯一ID。</summary>
    public void AddBoss() => ChangeCatalog(bosses =>
    {
        // 模板与新目录共用同一结构，复制节点后解除原父引用。
        var next = new EmitterDocument(true).Root["Bosses"]![0]!.DeepClone().AsObject();
        next["Core"]!["Id"] = UniqueBossId(bosses, "NewBoss");
        bosses.Add(next); SelectedBossIndex = bosses.Count - 1; SelectedPhase = _selectedEmitter = -1; _creatorPath = "";
    });

    /// <summary>为新增或复制Boss分配不冲突身份，不改变其他Boss。</summary>
    /// <param name="bosses">当前目录数组。</param>
    /// <param name="basis">建议的ID前缀。</param>
    /// <returns>目录内唯一ID。</returns>
    private static string UniqueBossId(JsonArray bosses, string basis)
    {
        var names = bosses.Select(boss => boss?["Core"]?["Id"]?.ToString()).ToHashSet(StringComparer.Ordinal);
        string next = basis; int suffix = 2;
        while (names.Contains(next)) next = basis + "_" + suffix++;
        return next;
    }

    /// <summary>复制当前Boss、阶段或Emitter引用，文件内容不重复保存。</summary>
    private void DuplicateSelection()
    {
        if (_creatorPath.Length > 0) { EmitterPanel!.DuplicateCreator(); return; }
        if (SelectedBossIndex < 0) throw new InvalidOperationException("请先选择Boss、阶段或引用。");
        if (_selectedEmitter >= 0)
        {
            Change(root => { var references = root["Phases"]![SelectedPhase]!["Emitters"]!.AsArray(); references.Insert(_selectedEmitter + 1, references[_selectedEmitter]?.DeepClone()); _selectedEmitter++; _creatorPath = ""; });
        }
        else if (SelectedPhase >= 0) DuplicatePhase();
        else ChangeCatalog(bosses =>
        {
            var copy = SelectedBossRoot.DeepClone().AsObject();
            copy["Core"]!["Id"] = UniqueBossId(bosses, copy["Core"]!["Id"]!.ToString());
            bosses.Insert(SelectedBossIndex + 1, copy); SelectedBossIndex++;
        });
    }

    /// <summary>删除Boss、阶段或引用；从不删除共享Emitter文件。</summary>
    private void DeleteSelection()
    {
        if (_creatorPath.Length > 0) { EmitterPanel!.DeleteCreator(); return; }
        if (SelectedBossIndex < 0) throw new InvalidOperationException("不能删除目录根。");
        if (_selectedEmitter >= 0)
            Change(root => { root["Phases"]![SelectedPhase]!["Emitters"]!.AsArray().RemoveAt(_selectedEmitter); _selectedEmitter = -1; _creatorPath = ""; });
        else if (SelectedPhase >= 0) DeletePhase();
        else ChangeCatalog(bosses => { bosses.RemoveAt(SelectedBossIndex); SelectedBossIndex = Math.Min(SelectedBossIndex, bosses.Count - 1); });
    }

    /// <summary>调整当前层级数组顺序并跟随移动后的对象。</summary>
    /// <param name="direction">-1向上，1向下。</param>
    private void MoveSelection(int direction)
    {
        if (_creatorPath.Length > 0) { EmitterPanel!.MoveCreator(direction); return; }
        if (_selectedEmitter >= 0)
        {
            Change(root =>
            {
                var references = root["Phases"]![SelectedPhase]!["Emitters"]!.AsArray(); int next = _selectedEmitter + direction;
                if (next < 0 || next >= references.Count) return;
                var value = references[_selectedEmitter]; references.RemoveAt(_selectedEmitter); references.Insert(next, value); _selectedEmitter = next;
            });
        }
        else if (SelectedPhase >= 0) MovePhase(direction);
        else if (SelectedBossIndex >= 0) ChangeCatalog(bosses =>
        {
            int next = SelectedBossIndex + direction;
            if (next < 0 || next >= bosses.Count) return;
            var value = bosses[SelectedBossIndex]; bosses.RemoveAt(SelectedBossIndex); bosses.Insert(next, value); SelectedBossIndex = next;
        });
    }
}
