using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>Boss模式仅显示列表、Boss和阶段；所有内容修改归属独立文件。</summary>
public partial class BossEditorPanel
{
    /// <summary>目录中的Boss下标；-1表示列表根。</summary>
    public int SelectedBossIndex { get; private set; } = -1;
    /// <summary>选中Boss的事务内工作对象或事务外快照。</summary>
    public JsonObject SelectedBossRoot => (_selectedBoss ?? throw new InvalidOperationException("请先选择有效Boss。")).Root;
    // 当前Boss与JSON页分别记住文档身份，刷新或切换不会把旧草稿写到新文件。
    private EditorDocument? _selectedBoss, _shownDocument;
    private string _referenceError = "";
    private HBoxContainer _bossControls = null!;
    private readonly List<Button> _catalogButtons = new();
    private Button _up = null!, _down = null!, _duplicate = null!, _delete = null!, _addPhase = null!;
    private readonly Dictionary<string, TreeItem> _catalogItems = new();
    /// <summary>当前工作区是否拥有真实路径目录。</summary>
    private bool HasCatalog => Session.Root?.Kind == EditorDocumentKind.BossCatalog;
    /// <summary>取得选中Boss的相对字段。</summary>
    /// <param name="path">JSON指针。</param>
    /// <returns>字段工作对象或独立快照。</returns>
    private JsonNode? At(string path) => _selectedBoss?.At(path);

    /// <summary>选择Boss或阶段，先结束旧预览并保留原文档草稿。</summary>
    /// <param name="boss">目录下标；单文件使用0；-1为列表根。</param>
    /// <param name="phase">阶段下标；-1为Boss共用属性。</param>
    public void SelectBoss(int boss, int phase = -1)
    {
        Suspend(); Session.Root!.Selection = boss.ToString();
        ResolveSelection(); SelectedPhase = phase; RememberSelection(); Refresh();
    }
    /// <summary>记录工作区选择和Boss内部阶段选择，两个历史互不混入。</summary>
    private void RememberSelection()
    {
        Session.Root!.Selection = SelectedBossIndex.ToString();
        if (_selectedBoss is not null) _selectedBoss.Selection = HasCatalog ? SelectedPhase.ToString() : $"{SelectedBossIndex}/{SelectedPhase}";
    }
    /// <summary>恢复文件选择；无效引用保留占位，不把其他文件内容冒充目标。</summary>
    private void ResolveSelection()
    {
        _selectedBoss = null; _referenceError = ""; SelectedPhase = -1;
        var root = Session.Root!;
        if (!HasCatalog)
        {
            string[] selection = root.Selection.Split('/');
            SelectedBossIndex = selection[0] == "-1" ? -1 : 0; _selectedBoss = root;
            if (selection.Length == 2 && int.TryParse(selection[1], out int phase)) SelectedPhase = phase;
        }
        else
        {
            var paths = root.At("/Bosses") as JsonArray;
            SelectedBossIndex = int.TryParse(root.Selection, out int index) ? Math.Clamp(index, -1, (paths?.Count ?? 0) - 1) : -1;
            if (SelectedBossIndex >= 0)
            {
                try
                {
                    string path = paths![SelectedBossIndex]?.GetValue<string>() ?? throw new InvalidOperationException("Boss路径为空。");
                    _selectedBoss = Session.OpenBoss(path);
                    if (int.TryParse(_selectedBoss.Selection, out int phase)) SelectedPhase = phase;
                }
                catch (Exception error) { _referenceError = error.Message; }
            }
        }
        int count = (_selectedBoss?.At("/Phases") as JsonArray)?.Count ?? 0;
        SelectedPhase = Math.Clamp(SelectedPhase, -1, count - 1);
    }
    /// <summary>刷新树、当前文件原文及静态布局，模式内没有Emitter编辑内容。</summary>
    private void RefreshCatalog()
    {
        StopPreview(); _refreshing = true;
        try
        {
            ResolveSelection();
            _shownDocument = Document; _jsonBaseline = Document.Text.ReplaceLineEndings("\n");
            _json.Text = Document.Draft ?? _jsonBaseline;
            _bossControls.Visible = _selectedBoss is not null && SelectedBossIndex >= 0;
            foreach (var button in _catalogButtons) button.Disabled = !HasCatalog;
            // 单文件只允许阶段操作；列表根或错误引用不提供Boss内容操作。
            _up.Disabled = _down.Disabled = SelectedBossIndex < 0 || (!HasCatalog && SelectedPhase < 0);
            _duplicate.Disabled = _selectedBoss is null || SelectedBossIndex < 0 || (!HasCatalog && SelectedPhase < 0);
            _delete.Disabled = SelectedBossIndex < 0 || (!HasCatalog && SelectedPhase < 0);
            _addPhase.Disabled = _selectedBoss is null || SelectedBossIndex < 0;
            RebuildCatalogTree(); BuildInspector(); ValidateCanvas(); UpdateTitle();
            if (_referenceError.Length > 0) Status(_referenceError, true);
        }
        catch (Exception error) { Status(error.Message, true); }
        finally { _refreshing = false; }
    }
    /// <summary>建立严格的三级树；失效文件仍显示路径和诊断。</summary>
    private void RebuildCatalogTree()
    {
        var expanded = _catalogItems.Where(pair => !pair.Value.Collapsed).Select(pair => pair.Key).ToHashSet();
        _phases.Clear(); _catalogItems.Clear();
        var root = AddCatalogItem(null, HasCatalog ? "Boss 列表" : "Boss 列表（单文件）", "-1/-1");
        int count = HasCatalog ? (Session.Root!.At("/Bosses") as JsonArray)?.Count ?? 0 : 1;
        for (int index = 0; index < count; index++)
        {
            string path = ""; EditorDocument? document = null; string error = "";
            try
            {
                if (HasCatalog)
                {
                    path = Session.Root!.At("/Bosses/" + index)?.GetValue<string>() ?? "";
                    document = Session.OpenBoss(path);
                }
                else document = Session.Root;
            }
            catch (Exception failure) { error = failure.Message; }
            var data = document?.Root; string key = $"{index}/-1";
            var item = AddCatalogItem(root, data is null ? "无效Boss · " + path
                : $"{data["Core"]?["Id"]} · {data["Core"]?["DisplayName"]}" + (document!.Dirty || document.Draft is not null ? " ●" : ""), key);
            item.SetTooltipText(0, error.Length > 0 ? error : document!.FilePath);
            item.Collapsed = index != SelectedBossIndex && !expanded.Contains(key);
            if (data?["Phases"] is not JsonArray phases) continue;
            for (int phase = 0; phase < phases.Count; phase++)
                AddCatalogItem(item, $"{phase + 1:00} · {(phases[phase] as JsonObject)?["Name"] ?? "无效阶段"}", $"{index}/{phase}");
        }
        if (_catalogItems.TryGetValue($"{SelectedBossIndex}/{SelectedPhase}", out var selected)) selected.Select(0); else root.Select(0);
    }
    /// <summary>追加带选择键的树项。</summary>
    /// <param name="parent">父树项。</param>
    /// <param name="label">可见名称。</param>
    /// <param name="key">Boss与阶段下标。</param>
    /// <returns>新树项。</returns>
    private TreeItem AddCatalogItem(TreeItem? parent, string label, string key)
    {
        var item = _phases.CreateItem(parent); item.SetText(0, label); item.SetMetadata(0, key); _catalogItems[key] = item; return item;
    }
    /// <summary>树事件结束后再切换，避免销毁派发中的树项。</summary>
    private void CatalogTreeSelected()
    {
        if (_refreshing || _phases.GetSelected() is not { } item) return;
        string[] parts = item.GetMetadata(0).AsString().Split('/');
        int boss = int.Parse(parts[0]), phase = int.Parse(parts[1]);
        Callable.From(() => { if (IsInsideTree()) SelectBoss(boss, phase); }).CallDeferred();
    }
    /// <summary>展示路径列表；单文件虚拟根不提供目录编辑。</summary>
    private void BuildCatalogFields()
    {
        _fields.AddChild(new Label { Text = HasCatalog ? "Boss文件路径列表 · 移除引用不会删除文件" : "单文件工作区 · 选择Boss或阶段编辑内容" });
        if (!HasCatalog) return;
        if (Session.Root!.At("/Bosses") is not JsonArray paths) { Status("Bosses必须为路径数组，请在JSON页修复。", true); return; }
        for (int index = 0; index < paths.Count; index++)
        {
            int position = index; string pointer = "/Bosses/" + index;
            var row = new VBoxContainer(); _fields.AddChild(row);
            var input = EditorFieldControls.Text(paths[index], typeof(string), pointer, Guard, () => _refreshing,
                value => ChangeCatalog(array => array[position] = value)); row.AddChild(input);
            var actions = new HBoxContainer(); row.AddChild(actions);
            EditorArrayControls.AddActions(actions, pointer, position, paths.Count, Guard, () => _refreshing,
                direction => ChangeCatalog(array => EditorArrayControls.Move(array, position, direction)),
                remove: () => ChangeCatalog(array => array.RemoveAt(position)), removeText: "移除引用");
            Button(actions, "选择Boss文件…", () => Files.Open("选择Boss文件", "res://Data/Bosses", path =>
                ChangeCatalog(array => array[position] = ProjectSettings.LocalizePath(path)), true));
        }
    }
    /// <summary>仅修改路径目录；切换到列表根，使保存及撤销明确作用于目录。</summary>
    /// <param name="change">路径数组编辑操作。</param>
    private void ChangeCatalog(Action<JsonArray> change)
    {
        if (!HasCatalog) throw new InvalidOperationException("单文件工作区没有可修改的目录。");
        var document = Session.Root!;
        if (document.Draft is not null) throw new InvalidOperationException("请先应用目录JSON草稿。");
        document.Edit(root => change(root["Bosses"]!.AsArray()));
        document.Selection = "-1"; Refresh();
    }
    /// <summary>选择已有Boss并加入路径目录。</summary>
    private void AddBossReference() => Files.Open("引用Boss文件", "res://Data/Bosses", AddReference, true);
    /// <summary>追加有效独立Boss路径，不重复引用同一个文件。</summary>
    /// <param name="path">选择的项目内路径。</param>
    private void AddReference(string path)
    {
        string resource = ProjectSettings.LocalizePath(path); string full = BossCatalog.PathIdentity(resource);
        Session.OpenBoss(resource);
        ChangeCatalog(paths =>
        {
            if (paths.Any(value => value is JsonValue && EditorDocument.FullPath(value.ToString()).Equals(full, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("目录已引用该Boss文件。");
            paths.Add(resource);
        });
        SelectBoss((Session.Root!.At("/Bosses") as JsonArray)!.Count - 1);
    }
    /// <summary>以独立文件创建Boss模板，保存成功后才加入目录。</summary>
    public void AddBoss() => CreateBossFile(new EditorDocument(EditorDocumentKind.Boss));
    /// <summary>保存新建或复制Boss，再加入当前目录；取消或失败不创建引用。</summary>
    /// <param name="source">独立模板或复制文档。</param>
    private void CreateBossFile(EditorDocument source)
    {
        if (!HasCatalog) throw new InvalidOperationException("请在目录工作区新增Boss文件。");
        if (Session.Root!.Draft is not null) throw new InvalidOperationException("请先应用目录JSON草稿。");
        var catalog = Session.Root;
        var names = Session.Documents.Where(document => document.Kind == EditorDocumentKind.Boss).Select(document => document.At("/Core/Id")?.ToString()).ToHashSet(StringComparer.Ordinal);
        string basis = source.At("/Core/Id")!.ToString(), id = basis; int suffix = 2;
        while (names.Contains(id)) id = basis + "_" + suffix++;
        source.Edit(root => root["Core"]!["Id"] = id);
        Files.Save("创建独立Boss文件", "res://Data/Bosses", id + ".json", path =>
        {
            if (!ReferenceEquals(Session.Root, catalog) || catalog.Draft is not null) throw new InvalidOperationException("目录状态已改变，请重新新增。");
            BossCatalog.PathIdentity(ProjectSettings.LocalizePath(path));
            Session.SaveCopy(source, path, false); AddReference(path);
        });
    }
    /// <summary>复制阶段或独立Boss；Boss复制必须选择新文件。</summary>
    private void DuplicateSelection()
    {
        RequireApplied();
        if (SelectedPhase >= 0) DuplicatePhase();
        else if (_selectedBoss is not null && SelectedBossIndex >= 0)
        {
            var copy = new EditorDocument(EditorDocumentKind.Boss); copy.ApplyText(Document.Text); CreateBossFile(copy);
        }
        else throw new InvalidOperationException("请先选择Boss或阶段。");
    }
    /// <summary>删除阶段或目录引用，永不删除磁盘文件。</summary>
    private void DeleteSelection()
    {
        if (SelectedPhase >= 0) DeletePhase();
        else if (SelectedBossIndex >= 0) { int index = SelectedBossIndex; ChangeCatalog(paths => paths.RemoveAt(index)); }
        else throw new InvalidOperationException("不能删除列表根。");
    }
    /// <summary>按当前层级排序，单文件只允许阶段排序。</summary>
    /// <param name="direction">-1向上或1向下。</param>
    private void MoveSelection(int direction)
    {
        if (SelectedPhase >= 0) MovePhase(direction);
        else if (SelectedBossIndex >= 0) { int index = SelectedBossIndex; ChangeCatalog(paths => EditorArrayControls.Move(paths, index, direction)); }
    }
    /// <summary>Emitter引用仅提供路径编辑、选择及跳转。</summary>
    /// <param name="parent">属性容器。</param>
    /// <param name="pointer">引用字段指针。</param>
    /// <param name="path">当前资源路径。</param>
    private void EmitterReferenceField(VBoxContainer parent, string pointer, string path)
    {
        var input = EditorFieldControls.Text(JsonValue.Create(path), typeof(string), pointer, Guard, () => _refreshing,
            value => Change(_ => Set(pointer, value))); parent.AddChild(input);
        var row = new HBoxContainer(); parent.AddChild(row);
        Button(row, "选择Emitter文件…", () => Files.Open("选择Emitter文件", "res://Data/Emitters", selected =>
            Change(_ => Set(pointer, JsonValue.Create(ProjectSettings.LocalizePath(selected)))), true));
        var jump = Button(row, "在 Emitter 模式编辑", () =>
        {
            string current = At(pointer)?.ToString() ?? "";
            if (!EditorDocument.CanOpenEmitter(current)) throw new InvalidOperationException("Emitter引用已失效或文件类型错误。");
            Suspend(); OpenEmitterRequested?.Invoke(current);
        });
        jump.Disabled = !EditorDocument.CanOpenEmitter(path); jump.SetMeta("emitter_path", path);
    }
}
