using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>整个编辑器共用的文件会话，同一路径只持有一份可撤销文档。</summary>
public sealed class EditorSession
{
    /// <summary>保存列表、Boss与阶段的唯一目录文档，首次进入目录模式才加载。</summary>
    public EmitterDocument Catalog { get; } = new(true);
    // Windows规范化路径忽略大小写，未命名文档单独登记，不制造文件身份。
    private readonly Dictionary<string, EmitterDocument> _emitters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EmitterDocument> _untitled = new();
    // 从未进入目录模式时，不把未使用的模板计入未保存状态。
    private bool _catalogOpened;
    /// <summary>本会话全部Emitter文档，包括尚未选择保存位置的新文档。</summary>
    public IEnumerable<EmitterDocument> Emitters => _emitters.Values.Concat(_untitled);
    /// <summary>已使用的目录或任意Emitter仍有未保存修改及草稿。</summary>
    public bool HasUnsaved => (_catalogOpened && (Catalog.Dirty || Catalog.Draft is not null))
        || Emitters.Any(document => document.Dirty || document.Draft is not null);

    /// <summary>首次进入目录模式时读取正式目录，后续保留会话内容。</summary>
    public void EnsureCatalog() { if (!_catalogOpened) OpenCatalog(BossCatalog.DefaultPath); }
    /// <summary>替换目录内容，成功后标记目录已使用；外部Emitter保持原状态。</summary>
    /// <param name="path">Godot资源路径或操作系统路径。</param>
    public void OpenCatalog(string path) { Catalog.Open(FullPath(path)); _catalogOpened = true; }
    /// <summary>创建并登记未命名Emitter，历史和草稿由会话持有。</summary>
    /// <returns>独立的新文档。</returns>
    public EmitterDocument NewEmitter()
    {
        // 新文档暂时没有文件身份，由首次保存建立映射。
        var document = new EmitterDocument();
        _untitled.Add(document);
        return document;
    }
    /// <summary>用户确认放弃后移除未命名文档，不删除任何文件。</summary>
    /// <param name="document">需要丢弃的未命名文档。</param>
    public void DiscardUntitled(EmitterDocument document)
    {
        if (document.FilePath.Length != 0) throw new InvalidOperationException("已命名文档须保留在会话中。");
        _untitled.Remove(document);
    }

    /// <summary>取得指定资源的共享文档，首次打开时才读取磁盘。</summary>
    /// <param name="path">Godot资源路径或绝对文件路径。</param>
    /// <returns>本会话唯一的文件文档。</returns>
    public EmitterDocument OpenEmitter(string path)
    {
        // 文件存在且成功解析后才登记，失败不污染会话。
        string full = FullPath(path);
        if (_emitters.TryGetValue(full, out var current)) return current;
        var document = new EmitterDocument();
        document.Open(full);
        _emitters.Add(full, document);
        return document;
    }

    /// <summary>保存或另存Emitter，成功后才更新路径身份；拒绝覆盖另一份已打开文档。</summary>
    /// <param name="document">本会话持有的Emitter。</param>
    /// <param name="path">目标文件路径，另存后原文件保持不变。</param>
    public void SaveEmitter(EmitterDocument document, string path)
    {
        // 文件身份与草稿在准备操作前检查；失败时不修改路径映射。
        string target = EmitterTarget(document, path);
        document.Save(target);
        RegisterSavedEmitter(document, target);
    }

    /// <summary>检查Emitter保存目标，供单文件与批量保存共用。</summary>
    /// <param name="document">本会话持有的Emitter。</param>
    /// <param name="path">请求的保存路径。</param>
    /// <returns>可写入的规范化目标路径。</returns>
    private string EmitterTarget(EmitterDocument document, string path)
    {
        if (!Emitters.Contains(document)) throw new InvalidOperationException("文档不属于当前会话。");
        if (document.Draft is not null) throw new InvalidOperationException("请先应用JSON草稿。");
        // 目标文件身份必须在任何写入前检查。
        string target = FullPath(path);
        if (_emitters.TryGetValue(target, out var existing) && !ReferenceEquals(existing, document))
            throw new InvalidOperationException("目标文件已在会话中打开，请切换到该文件编辑或选择其他保存位置。");
        if (_catalogOpened && Catalog.FilePath.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(target, Catalog.FilePath))
            throw new InvalidOperationException("Emitter不能覆盖当前Boss目录。");
        return target;
    }

    /// <summary>成功写盘后更新Emitter文件身份，不改变撤销与草稿。</summary>
    /// <param name="document">刚完成保存的文档。</param>
    /// <param name="target">已经写入的规范化目标路径。</param>
    private void RegisterSavedEmitter(EmitterDocument document, string target)
    {
        // 移除旧别名后登记新路径，其他文件的会话状态不变。
        foreach (string old in _emitters.Where(pair => ReferenceEquals(pair.Value, document)).Select(pair => pair.Key).ToArray())
            _emitters.Remove(old);
        _untitled.Remove(document);
        _emitters[target] = document;
    }

    /// <summary>保存目录，避免覆盖会话中的Emitter文件。</summary>
    /// <param name="path">目录目标文件路径。</param>
    public void SaveCatalog(string path)
    {
        if (Catalog.Draft is not null) throw new InvalidOperationException("请先应用目录JSON草稿。");
        // 目录和Emitter也不能占用同一个文件身份。
        string target = FullPath(path);
        if (_emitters.ContainsKey(target)) throw new InvalidOperationException("Boss目录不能覆盖已打开的Emitter。");
        Catalog.Save(target);
        _catalogOpened = true;
    }

    /// <summary>读取当前引用内容用于静态校验；已打开文档优先，未打开文件每次读取真实文本。</summary>
    /// <param name="path">Emitter资源路径或绝对路径。</param>
    /// <returns>当前文本及已打开文档的修订号；磁盘来源的修订号为空。</returns>
    internal (string Text, long? Revision) ReadEmitterSource(string path)
    {
        string full = FullPath(path);
        if (!_emitters.TryGetValue(full, out var document)) return (File.ReadAllText(full), null);
        if (document.Draft is not null) throw new InvalidOperationException("请先应用Emitter草稿：" + path);
        return (document.Text, document.Revision);
    }

    /// <summary>创建预览资源快照，每次调用创建独立运行树。</summary>
    /// <returns>只保存JSON文本的独立创建函数。</returns>
    public Func<string, VBulletEmitter> CaptureEmitters()
    {
        // 只复用冻结文本，预览阶段切换始终创建独立运行树。
        var source = CaptureEmitterTexts();
        return path => VBulletEmitter.FromJson(source(path), path);
    }

    /// <summary>冻结引用源文本和草稿状态，未打开文件在本次首次读取时冻结。</summary>
    /// <returns>按规范化文件身份读取文本的函数，不持有运行对象。</returns>
    private Func<string, string> CaptureEmitterTexts()
    {
        // 已打开文件采用已应用文本，后续会话编辑不改变本次快照。
        var snapshots = _emitters.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase);
        var drafts = _emitters.Where(pair => pair.Value.Draft is not null).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            // 引用使用与编辑会话完全相同的规范化文件身份。
            string full = FullPath(path);
            if (drafts.Contains(full)) throw new InvalidOperationException("请先应用Emitter草稿：" + path);
            if (!snapshots.TryGetValue(full, out string? json)) snapshots[full] = json = File.ReadAllText(full);
            return json;
        };
    }

    /// <summary>校验冻结内容后按Emitter、目录顺序写入；部分写入失败保留准确的逐文件状态。</summary>
    public void SaveAll()
    {
        if (Catalog.Draft is not null || Emitters.Any(document => document.Draft is not null))
            throw new InvalidOperationException("请先应用所有JSON草稿。");
        if (_untitled.Count > 0) throw new InvalidOperationException("请先为未命名Emitter选择保存位置。");
        if (Catalog.FilePath.Length == 0) throw new InvalidOperationException("请先为Boss目录选择保存位置。");
        if (_emitters.ContainsKey(FullPath(Catalog.FilePath))) throw new InvalidOperationException("Boss目录不能覆盖已打开的Emitter。");
        // 校验结果只在本次保存内复用，不跨修改、草稿变化或外部文件变化保留。
        var source = CaptureEmitterTexts();
        var checkedTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>同一来源的相同冻结文本仅检查一次，不缓存或返回运行树。</summary>
        /// <param name="path">Emitter来源路径。</param>
        /// <param name="json">本次冻结的完整JSON。</param>
        void CheckEmitter(string path, string json)
        {
            // 文本参与匹配，避免同一路径的新内容误用已有检查结果。
            string full = FullPath(path);
            if (checkedTexts.TryGetValue(full, out string? previous) && previous == json) return;
            VBulletEmitter.FromJson(json, path);
            checkedTexts[full] = json;
        }
        // 全部文档先准备，再统一开始写入；未修改文件仍接受完整校验。
        var pending = new List<(EmitterDocument Document, EmitterDocument.PreparedSave Save)>();
        foreach (var document in _emitters.Values)
        {
            // 保存目标与冻结内容属于同一个会话文档。
            string target = EmitterTarget(document, document.FilePath);
            var save = document.PrepareSave(target, json => CheckEmitter(target, json));
            if (document.Dirty) pending.Add((document, save));
        }
        var catalogSave = Catalog.PrepareSave(Catalog.FilePath,
            json => BossCatalog.CheckJson(json, Catalog.FilePath, path => CheckEmitter(path, source(path))));
        foreach (var entry in pending) entry.Save.EnsureCurrent();
        catalogSave.EnsureCurrent();
        // 已完成文件不可伪装为回滚；后续失败时提供准确的进度和重试入口。
        var completed = new List<string>();
        try
        {
            foreach (var entry in pending)
            {
                entry.Save.Write(); RegisterSavedEmitter(entry.Document, entry.Save.Target);
                completed.Add(entry.Save.Target);
            }
            catalogSave.Write(); _catalogOpened = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("保存全部未完成。已保存文件：" +
                (completed.Count == 0 ? "无" : string.Join("、", completed)) +
                "。其余修改仍保留在会话中，可重试保存。原因：" + error.Message, error);
        }
    }
    /// <summary>规范化资源路径，以文件身份共享文档。</summary>
    /// <param name="path">资源路径或操作系统路径。</param>
    /// <returns>完整操作系统路径。</returns>
    private static string FullPath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
}
