using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>编辑器文档，保留表达式、显式null及复制指令，不保存运行对象。</summary>
public sealed partial class EmitterDocument
{
    // 历史仅保存JSON，避免撤销时共享可变节点；最多保留100次修改。
    private readonly Stack<(string Text, string Selection)> _undo = new(), _redo = new();
    private string _saved = "";
    // 已发布节点从不外借；事务工作副本提交后再次隔离，文本按修订缓存。
    private JsonObject _root = new();
    private JsonObject? _working;
    private string _text = "";
    /// <summary>已应用内容的单调修订号；草稿、选择及保存不改变它。</summary>
    public long Revision { get; private set; }
    /// <summary>是否为Boss目录文档；另一模式编辑独立Emitter。</summary>
    public bool IsCatalog { get; }
    /// <summary>事务内返回工作副本；事务外返回独立快照，修改须通过Edit提交。</summary>
    public JsonObject Root => _working ?? (JsonObject)_root.DeepClone();
    /// <summary>文件绝对路径；新建文档为空。</summary>
    public string FilePath { get; private set; } = "";
    /// <summary>相对已保存内容是否存在修改。</summary>
    public bool Dirty => Text != _saved;
    /// <summary>当前缩进JSON；表达式字符串不求值。</summary>
    public string Text => _working is null ? _text : _working.ToJsonString(TextOptions);
    // 原文保留表达式与显式null，选项在所有文档间只读复用。
    private static readonly JsonSerializerOptions TextOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>随撤销快照保存的界面选择，不写入JSON。</summary>
    public string Selection { get; set; } = "";
    /// <summary>尚未应用的原文草稿，切换文档时保留；null表示没有草稿。</summary>
    public string? Draft { get; set; }
    /// <summary>是否可以撤销。</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>是否可以重做。</summary>
    public bool CanRedo => _redo.Count > 0;
    /// <summary>建立可直接预览的独立文档。</summary>
    /// <param name="catalog">为真时使用Boss目录模板，否则使用Emitter模板。</param>
    public EmitterDocument(bool catalog = false) { IsCatalog = catalog; New(); }
    /// <summary>按当前模式重置为未保存的新建Boss或发射器。</summary>
    public void New()
    {
        EnsureIdle();
        var next = JsonNode.Parse("""
        {"Core":{"Name":"NewEmitter","RefObject":"Boss","Team":"Enemy","Damage":1,"StopMode":"ClearBullets"},
         "VNodes":{"Core":{"Type":"VBullet","Amount":12,"LifeTimeMs":4000},"Display":{"TextureName":"Dot","TextureIndex":0},
         "BaseAttributes":[{"Angle":0,"Speed":180,"RefMoveQueue":[{"Type":"XYMove","X":0,"Y":0}]}],
         "AddAttributes":{"Angle":"TAU/12"},"Timeline":[{"StartMs":0,"IntervalMs":1000}]}}
        """)!.AsObject();
        if (IsCatalog) next = JsonNode.Parse("""
        {"Core":{"Id":"NewBoss","DisplayName":"新Boss","TexturePath":"res://Assets/Units/Boss_01.png",
         "Hframes":2,"Vframes":2,"AnimationFps":4,"MaxHp":100,"CollisionRadius":32,"VisualScale":3,"SpawnPosition":{"X":640,"Y":250}},
         "Phases":[{"Name":"阶段01","Hp":100,"DurationMs":60000,"EndCondition":"HealthOrTime","Emitters":[],
         "Movement":{"Type":"Center","Speed":200,"Target":{"X":640,"Y":240}}}]}
        """)!.AsObject();
        if (IsCatalog) next = new JsonObject { ["Bosses"] = new JsonArray(next) };
        Publish(next, next.ToJsonString(TextOptions));
        Selection = ""; Draft = null;
        FilePath = "";
        _saved = "";
        _undo.Clear(); _redo.Clear();
    }
    /// <summary>检查JSON语法及重复字段，允许载入业务无效文档供修复。</summary>
    /// <param name="text">完整JSON文本。</param>
    /// <returns>独立的根对象。</returns>
    public static JsonObject Parse(string text) => JsonData.Parse(text, "编辑文档", element =>
        JsonNode.Parse(element.GetRawText()) as JsonObject ?? throw new JsonException("文档必须是JSON对象。"));
    /// <summary>读取文件；读取失败时保留当前文档。</summary>
    /// <param name="path">操作系统绝对路径。</param>
    public void Open(string path)
    {
        EnsureIdle();
        // 先完整读取，成功后才替换当前数据。
        var next = Parse(File.ReadAllText(path));
        if ((IsCatalog && (next.ContainsKey("VNodes") || next.ContainsKey("Phases"))) || (!IsCatalog && (next.ContainsKey("Bosses") || next.ContainsKey("Phases"))))
            throw new JsonException("文件属于另一编辑模式，请先切换顶部模式。");
        // 路径和序列化也须成功后才发布。
        string target = Path.GetFullPath(path), text = next.ToJsonString(TextOptions);
        Publish(next, text); FilePath = target; _saved = Text; Selection = ""; Draft = null;
        _undo.Clear(); _redo.Clear();
    }
    /// <summary>提交一次可撤销修改；失败时回滚整份文档。</summary>
    /// <param name="change">对当前根对象进行的修改。</param>
    public void Edit(Action<JsonObject> change)
    {
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(change);
        // 选择随事务回滚；已发布数据和历史直到全部处理成功才更新。
        string selection = Selection;
        _working = (JsonObject)_root.DeepClone();
        try
        {
            change(_working);
            // 提交后不保留回调可能持有的JsonNode引用。
            string text = _working.ToJsonString(TextOptions);
            if (text != _text) Commit(Parse(text), text, selection);
        }
        catch { Selection = selection; throw; }
        finally { _working = null; }
    }
    /// <summary>记录一次实际内容变化，清除重做并限制撤销长度。</summary>
    /// <param name="next">没有外部可变引用的独立根对象。</param>
    /// <param name="text">与根对象一致的缩进JSON。</param>
    /// <param name="selection">修改前的界面选择。</param>
    private void Commit(JsonObject next, string text, string selection)
    {
        if (_undo.Count >= 100)
        {
            // 丢弃最早项，保持栈顶为最近一次修改。
            var history = _undo.ToArray(); _undo.Clear();
            // 按声明顺序处理的零基下标。
            for (int index = 98; index >= 0; index--) _undo.Push(history[index]);
        }
        _undo.Push((_text, selection)); _redo.Clear();
        Publish(next, text);
    }
    /// <summary>发布独立根对象及文本，内容未变时保留修订号和文本实例。</summary>
    /// <param name="next">仅由文档持有的根对象。</param>
    /// <param name="text">已规范化文本。</param>
    private void Publish(JsonObject next, string text)
    {
        _root = next;
        if (_text == text) return;
        _text = text; Revision++;
    }
    /// <summary>拒绝事务回调内重入历史、文件或另一事务操作。</summary>
    private void EnsureIdle()
    {
        if (_working is not null) throw new InvalidOperationException("文档事务内不能重入编辑、历史或文件操作。");
    }
    /// <summary>应用文本编辑结果，先检查语法且保留撤销记录。</summary>
    /// <param name="text">完整JSON草稿。</param>
    public void ApplyText(string text)
    {
        EnsureIdle();
        // 在修改前解析，避免不完整输入丢失当前数据。
        var next = Parse(text);
        string normalized = next.ToJsonString(TextOptions);
        if (normalized != _text) Commit(next, normalized, Selection);
        Draft = null;
    }
    /// <summary>撤销最近一次编辑。</summary>
    public void Undo() => RestoreHistory(_undo, _redo);
    /// <summary>恢复最近一次撤销。</summary>
    public void Redo() => RestoreHistory(_redo, _undo);
    /// <summary>将一个完整历史快照移至当前文档，保留相反方向的历史。</summary>
    /// <param name="source">待恢复的历史栈。</param>
    /// <param name="destination">记录当前内容的历史栈。</param>
    private void RestoreHistory(Stack<(string Text, string Selection)> source, Stack<(string Text, string Selection)> destination)
    {
        EnsureIdle();
        if (source.Count == 0) return;
        // 先解析，异常时不改变历史或当前内容。
        var entry = source.Peek();
        var next = Parse(entry.Text);
        destination.Push((_text, Selection)); source.Pop();
        Publish(next, entry.Text); Selection = entry.Selection;
    }
    /// <summary>使用游戏加载器校验，不启动战斗或消耗随机。</summary>
    /// <returns>未启动且独立的运行定义。</returns>
    public VBulletEmitter Validate() => VBulletEmitter.FromJson(Text, FilePath.Length == 0 ? "新建发射器" : FilePath);
    /// <summary>使用正式Boss加载器验证当前Boss文档。</summary>
    /// <returns>独立的Boss数据。</returns>
    /// <param name="index">目录中的零基Boss下标。</param>
    /// <param name="loadEmitter">可选编辑会话资源入口。</param>
    public BossData ValidateBoss(int index = 0, Func<string, VBulletEmitter>? loadEmitter = null)
        => BossData.FromJson((_working ?? _root)["Bosses"]?[index]?.ToJsonString() ?? throw new InvalidOperationException("请先选择Boss。"), FilePath, loadEmitter);
    /// <summary>校验完整目录及其全部Emitter引用。</summary>
    /// <param name="loadEmitter">可选内存预览资源入口。</param>
    /// <returns>完整有效的目录。</returns>
    public BossCatalog ValidateCatalog(Func<string, VBulletEmitter>? loadEmitter = null) => BossCatalog.FromJson(Text, FilePath, loadEmitter);
    /// <summary>按文档模式校验，始终不推进战斗。</summary>
    public void ValidateCurrent() { if (IsCatalog) ValidateCatalog(); else Validate(); }
    /// <summary>通过JSON Pointer定位节点。</summary>
    /// <param name="pointer">空字符串表示根；字段中的斜线按~1转义。</param>
    /// <returns>事务内工作节点；事务外独立快照；显式null返回null。</returns>
    public JsonNode? At(string pointer)
    {
        // 依次按对象键或数组下标访问。
        JsonNode? current = _working ?? _root;
        // 当前JSON Pointer路径片段。
        foreach (string part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current is JsonArray array ? array[int.Parse(part)] : current?[part.Replace("~1", "/").Replace("~0", "~")];
        return _working is null ? current?.DeepClone() : current;
    }
    /// <summary>转义一个JSON Pointer字段。</summary>
    /// <param name="key">原始字段名。</param>
    /// <returns>已转义字段名。</returns>
    public static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
}
