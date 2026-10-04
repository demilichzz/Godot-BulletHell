using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Boss模式独立会话；目录和Boss按文件共享身份，不读取Emitter编辑草稿。</summary>
public sealed class BossEditorSession
{
    // 有名文档和未命名文档分别登记，切换工作区不丢弃修改。
    private readonly Dictionary<string, EditorDocument> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EditorDocument> _untitled = new();
    /// <summary>当前打开的目录或单个Boss；不为单文件制造目录文档。</summary>
    public EditorDocument? Root { get; private set; }
    /// <summary>本模式所有已打开文档。</summary>
    public IEnumerable<EditorDocument> Documents => _files.Values.Concat(_untitled);
    /// <summary>包括隐藏文档及未应用草稿的修改状态。</summary>
    public bool HasUnsaved => Documents.Any(document => document.Dirty || document.Draft is not null);
    /// <summary>首次进入Boss模式时打开默认目录。</summary>
    public void EnsureRoot() { if (Root is null) Open(BossCatalog.DefaultPath); }
    /// <summary>打开目录或Boss，只有读取成功才切换当前工作区。</summary>
    /// <param name="path">目标文件路径。</param>
    public void Open(string path) => Root = OpenDocument(path);
    /// <summary>取得唯一文档，保持该文件的历史、草稿及已应用内容。</summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="expected">可选期望类型，类型不匹配时不登记文档。</param>
    /// <returns>本会话拥有的Boss或目录文档。</returns>
    public EditorDocument OpenDocument(string path, EditorDocumentKind? expected = null)
    {
        string full = EditorDocument.FullPath(path);
        if (_files.TryGetValue(full, out var existing))
        {
            if (expected is not null && existing.Kind != expected) throw new InvalidOperationException("引用目标不是Boss文件：" + path);
            return existing;
        }
        var kind = EditorDocument.DetectKind(EditorDocument.Parse(File.ReadAllText(full)));
        if (kind == EditorDocumentKind.Emitter || (expected is not null && kind != expected)) throw new InvalidOperationException("文件类型不符合Boss引用或打开要求：" + path);
        var document = new EditorDocument(kind); document.Open(full); _files.Add(full, document); return document;
    }
    /// <summary>打开指定Boss引用，不接受另一个目录作为Boss。</summary>
    /// <param name="path">目录中的资源路径。</param>
    /// <returns>唯一的独立Boss文档。</returns>
    public EditorDocument OpenBoss(string path)
    {
        string full = BossCatalog.PathIdentity(path);
        var document = OpenDocument(full, EditorDocumentKind.Boss);
        if (document.Kind != EditorDocumentKind.Boss) throw new InvalidOperationException("引用目标不是Boss文件：" + path);
        return document;
    }
    /// <summary>新建独立文档，已有工作区保留在会话中。</summary>
    /// <param name="kind">Boss或BossCatalog。</param>
    /// <returns>成为当前工作区的新文档。</returns>
    public EditorDocument New(EditorDocumentKind kind)
    {
        if (kind == EditorDocumentKind.Emitter) throw new ArgumentException("Boss模式不能创建Emitter。");
        var document = new EditorDocument(kind); _untitled.Add(document); Root = document; return document;
    }
    /// <summary>确认丢弃后移除已被替换的未命名工作区，不删除有名文件。</summary>
    /// <param name="document">已离开当前工作区的未命名文档。</param>
    internal void DiscardUntitled(EditorDocument document)
    {
        if (document.FilePath.Length > 0 || ReferenceEquals(document, Root)) throw new InvalidOperationException("只能移除已替换的未命名文档。");
        _untitled.Remove(document);
    }
    /// <summary>保存一个当前文件；目录引用检查始终以磁盘内容为准。</summary>
    /// <param name="document">本会话文档。</param>
    /// <param name="path">保存目标。</param>
    public void Save(EditorDocument document, string path)
    {
        if (!Documents.Contains(document)) throw new InvalidOperationException("文档不属于Boss会话。");
        string target = CheckTarget(document, path);
        document.Save(target); Register(document, target);
    }
    /// <summary>创建独立副本，成功后可由调用方加入目录或切换为单文件工作区。</summary>
    /// <param name="source">已应用源文档。</param>
    /// <param name="path">新文件路径，不覆盖会话已有文档。</param>
    /// <param name="makeRoot">成功后是否独立打开副本。</param>
    /// <returns>新文件对应的文档。</returns>
    public EditorDocument SaveCopy(EditorDocument source, string path, bool makeRoot)
    {
        if (source.Draft is not null) throw new InvalidOperationException("请先应用JSON草稿。");
        string target = EditorDocument.FullPath(path);
        if (_files.ContainsKey(target) || (source.FilePath.Length > 0 && target.Equals(source.FilePath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("另存副本须选择未在本会话打开的新路径。");
        EditorDocument.CheckTargetKind(target, source.Kind);
        var copy = new EditorDocument(source.Kind); copy.ApplyText(source.Text); copy.Save(target);
        _files.Add(target, copy); if (makeRoot) Root = copy; return copy;
    }
    /// <summary>检查写入目标，不在写盘前改变文件身份。</summary>
    /// <param name="document">源文档。</param>
    /// <param name="path">目标路径。</param>
    /// <returns>规范化目标。</returns>
    private string CheckTarget(EditorDocument document, string path)
    {
        string target = EditorDocument.FullPath(path);
        if (_files.TryGetValue(target, out var occupied) && !ReferenceEquals(occupied, document))
            throw new InvalidOperationException("目标文件已在Boss会话中打开。");
        EditorDocument.CheckTargetKind(target, document.Kind); return target;
    }
    /// <summary>成功保存后登记新身份。</summary>
    /// <param name="document">已写盘文档。</param>
    /// <param name="target">目标完整路径。</param>
    private void Register(EditorDocument document, string target)
    {
        foreach (string key in _files.Where(pair => ReferenceEquals(pair.Value, document)).Select(pair => pair.Key).ToArray()) _files.Remove(key);
        _untitled.Remove(document); _files[target] = document;
    }
    /// <summary>冻结磁盘Emitter文本，每次调用创建独立树；不访问另一模式的会话。</summary>
    /// <returns>同一次预览或保存使用的独立运行树工厂。</returns>
    internal static Func<string, VBulletEmitter> CaptureDiskEmitters()
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            string full = EditorDocument.FullPath(path);
            if (!texts.TryGetValue(full, out string? text)) texts[full] = text = File.ReadAllText(full);
            return VBulletEmitter.FromJson(text, path);
        };
    }
    /// <summary>预校验全部冻结内容，再按Boss、目录顺序保存；中途失败如实报告已完成文件。</summary>
    public void SaveAll()
    {
        var documents = Documents.ToArray();
        if (documents.Any(document => document.Draft is not null)) throw new InvalidOperationException("请先应用Boss模式的全部JSON草稿。");
        if (documents.Any(document => document.FilePath.Length == 0)) throw new InvalidOperationException("请先为未命名Boss或目录选择保存位置。");
        var texts = documents.ToDictionary(document => document.FilePath, document => document.Text, StringComparer.OrdinalIgnoreCase);
        var emitters = CaptureDiskEmitters(); var checkedEmitters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>冻结并读取Boss文本；未打开引用只在本次首次访问时读取。</summary>
        /// <param name="path">Boss引用。</param>
        /// <returns>本次固定文本。</returns>
        string ReadBoss(string path)
        {
            string full = EditorDocument.FullPath(path);
            if (!texts.TryGetValue(full, out string? text)) texts[full] = text = File.ReadAllText(full);
            return text;
        }
        /// <summary>同一保存批次仅检查一次相同磁盘Emitter。</summary>
        /// <param name="path">Emitter引用。</param>
        void CheckEmitter(string path) { if (checkedEmitters.Add(EditorDocument.FullPath(path))) emitters(path); }
        var pending = new List<EditorDocument.PreparedSave>();
        foreach (var document in documents.OrderBy(document => document.Kind == EditorDocumentKind.BossCatalog))
        {
            string target = CheckTarget(document, document.FilePath);
            var save = document.PrepareSave(target, text =>
            {
                if (document.Kind == EditorDocumentKind.BossCatalog) BossCatalog.CheckJson(text, target, CheckEmitter, ReadBoss);
                else JsonData.Parse(text, target, root => BossData.Read(root, validateEmitter: CheckEmitter));
            });
            if (document.Dirty) pending.Add(save);
        }
        foreach (var save in pending) save.EnsureCurrent();
        var completed = new List<string>();
        try { foreach (var save in pending) { save.Write(); completed.Add(save.Target); } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new IOException("保存全部未完成。已保存文件：" + (completed.Count == 0 ? "无" : string.Join("、", completed)) + "。其余修改仍保留。原因：" + error.Message, error); }
    }
}
