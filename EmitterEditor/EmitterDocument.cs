using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>编辑器文档，保留表达式、显式null及复制指令，不保存运行对象。</summary>
public sealed class EmitterDocument
{
    // 历史仅保存JSON，避免撤销时共享可变节点；最多保留100次修改。
    private readonly Stack<string> _undo = new(), _redo = new();
    private string _saved = "";
    /// <summary>是否为Boss文档；模式决定模板、加载校验及保存命名。</summary>
    public bool IsBoss { get; }
    /// <summary>当前可编辑JSON对象。</summary>
    public JsonObject Root { get; private set; } = new();
    /// <summary>文件绝对路径；新建文档为空。</summary>
    public string FilePath { get; private set; } = "";
    /// <summary>相对已保存内容是否存在修改。</summary>
    public bool Dirty => Text != _saved;
    /// <summary>当前缩进JSON；表达式字符串不求值。</summary>
    public string Text => Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    /// <summary>是否可以撤销。</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>是否可以重做。</summary>
    public bool CanRedo => _redo.Count > 0;
    /// <summary>建立可直接预览的独立文档。</summary>
    /// <param name="boss">为真时使用Boss模板，否则使用Emitter模板。</param>
    public EmitterDocument(bool boss = false) { IsBoss = boss; New(); }
    /// <summary>按当前模式重置为未保存的新建Boss或发射器。</summary>
    public void New()
    {
        Root = JsonNode.Parse("""
        {"Core":{"Name":"NewEmitter","RefObject":"Boss","Team":"Enemy","Damage":1,"StopMode":"ClearBullets"},
         "VNodes":{"Core":{"Type":"VBullet","Amount":12,"LifeTimeMs":4000},"Display":{"TextureName":"Dot","TextureIndex":0},
         "BaseAttributes":[{"Angle":0,"Speed":180,"RefMoveQueue":[{"Type":"XYMove","X":0,"Y":0}]}],
         "AddAttributes":{"Angle":"TAU/12"},"Timeline":[{"StartMs":0,"IntervalMs":1000}]}}
        """)!.AsObject();
        if (IsBoss) Root = JsonNode.Parse("""
        {"Core":{"Id":"NewBoss","DisplayName":"新Boss","TexturePath":"res://Assets/Units/Boss_01.png",
         "Hframes":2,"Vframes":2,"AnimationFps":4,"MaxHp":100,"CollisionRadius":32,"VisualScale":3,"SpawnPosition":{"X":640,"Y":250}},
         "Phases":[{"Name":"阶段01","Hp":100,"DurationMs":60000,"EndCondition":"HealthOrTime","Emitters":[],
         "Movement":{"Type":"Center","Speed":200,"Target":{"X":640,"Y":240}}}]}
        """)!.AsObject();
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
        // 先完整读取，成功后才替换当前数据。
        var next = Parse(File.ReadAllText(path));
        if ((IsBoss && next.ContainsKey("VNodes")) || (!IsBoss && next.ContainsKey("Phases")))
            throw new JsonException("文件属于另一编辑模式，请先切换顶部模式。");
        Root = next; FilePath = Path.GetFullPath(path); _saved = Text;
        _undo.Clear(); _redo.Clear();
    }
    /// <summary>提交一次可撤销修改；失败时回滚整份文档。</summary>
    /// <param name="change">对当前根对象进行的修改。</param>
    public void Edit(Action<JsonObject> change)
    {
        // 保存修改前的独立快照。
        string before = Text;
        try { change(Root); }
        catch { Root = Parse(before); throw; }
        if (before == Text) return;
        if (_undo.Count >= 100)
        {
            // 丢弃最早项，保持栈顶为最近一次修改。
            var history = _undo.ToArray(); _undo.Clear();
            // 按声明顺序处理的零基下标。
            for (int index = 98; index >= 0; index--) _undo.Push(history[index]);
        }
        _undo.Push(before); _redo.Clear();
    }
    /// <summary>应用文本编辑结果，先检查语法且保留撤销记录。</summary>
    /// <param name="text">完整JSON草稿。</param>
    public void ApplyText(string text)
    {
        // 在修改前解析，避免不完整输入丢失当前数据。
        var next = Parse(text);
        Edit(_ => Root = next);
    }
    /// <summary>撤销最近一次编辑。</summary>
    public void Undo() { if (!CanUndo) return; _redo.Push(Text); Root = Parse(_undo.Pop()); }
    /// <summary>恢复最近一次撤销。</summary>
    public void Redo() { if (!CanRedo) return; _undo.Push(Text); Root = Parse(_redo.Pop()); }
    /// <summary>使用游戏加载器校验，不启动战斗或消耗随机。</summary>
    /// <returns>未启动且独立的运行定义。</returns>
    public VBulletEmitter Validate() => VBulletEmitter.FromJson(Text, FilePath.Length == 0 ? "新建发射器" : FilePath);
    /// <summary>使用正式Boss加载器验证当前Boss文档。</summary>
    /// <returns>独立的Boss数据。</returns>
    public BossData ValidateBoss() => BossData.FromJson(Text, FilePath.Length == 0 ? "新建Boss" : FilePath);
    /// <summary>按文档模式校验，始终不推进战斗。</summary>
    public void ValidateCurrent() { if (IsBoss) ValidateBoss(); else Validate(); }
    /// <summary>校验后原子写入JSON；失败不更新保存状态。</summary>
    /// <param name="path">目标文件绝对路径。</param>
    public void Save(string path)
    {
        ValidateCurrent();
        if (IsBoss && !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^B[0-9]{2,}\.json$"))
            throw new ArgumentException("Boss文件须命名为Bxx.json，编号至少两位。", nameof(path));
        // 临时文件和目标位于同目录，避免跨卷移动；不覆盖其他临时文件。
        string target = Path.GetFullPath(path), temporary = target + ".editor-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
            {
                // 写入UTF-8且刷新磁盘后再替换正式文件。
                byte[] bytes = new UTF8Encoding(false).GetBytes(Text + Environment.NewLine);
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, target, true);
            FilePath = target; _saved = Text;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <summary>通过JSON Pointer定位节点。</summary>
    /// <param name="pointer">空字符串表示根；字段中的斜线按~1转义。</param>
    /// <returns>对应节点，显式null返回null。</returns>
    public JsonNode? At(string pointer)
    {
        // 依次按对象键或数组下标访问。
        JsonNode? current = Root;
        // 当前JSON Pointer路径片段。
        foreach (string part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current is JsonArray array ? array[int.Parse(part)] : current?[part.Replace("~1", "/").Replace("~0", "~")];
        return current;
    }
    /// <summary>转义一个JSON Pointer字段。</summary>
    /// <param name="key">原始字段名。</param>
    /// <returns>已转义字段名。</returns>
    public static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
}
