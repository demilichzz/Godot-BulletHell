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
        if (!Emitters.Contains(document)) throw new InvalidOperationException("文档不属于当前会话。");
        if (document.Draft is not null) throw new InvalidOperationException("请先应用JSON草稿。");
        // 目标文件身份必须在任何写入前检查。
        string target = FullPath(path);
        if (_emitters.TryGetValue(target, out var existing) && !ReferenceEquals(existing, document))
            throw new InvalidOperationException("目标文件已在会话中打开，请切换到该文件编辑或选择其他保存位置。");
        if (_catalogOpened && Catalog.FilePath.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(target, Catalog.FilePath))
            throw new InvalidOperationException("Emitter不能覆盖当前Boss目录。");
        // 写盘失败时保留旧路径映射和未命名身份。
        document.Save(target);
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

    /// <summary>创建预览资源快照，每次调用创建独立运行树。</summary>
    /// <returns>只保存JSON文本的独立创建函数。</returns>
    public Func<string, VBulletEmitter> CaptureEmitters()
    {
        // 已打开文件采用已应用文本，未打开文件在首次解析时冻结。
        var snapshots = _emitters.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase);
        var drafts = _emitters.Where(pair => pair.Value.Draft is not null).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            // 引用使用与编辑会话完全相同的规范化文件身份。
            string full = FullPath(path);
            if (drafts.Contains(full)) throw new InvalidOperationException("请先应用Emitter草稿：" + path);
            if (!snapshots.TryGetValue(full, out string? json)) snapshots[full] = json = File.ReadAllText(full);
            return VBulletEmitter.FromJson(json, path);
        };
    }

    /// <summary>预先校验全部待保存文档，然后先保存Emitter、最后保存目录。</summary>
    public void SaveAll()
    {
        if (Catalog.Draft is not null || Emitters.Any(document => document.Draft is not null))
            throw new InvalidOperationException("请先应用所有JSON草稿。");
        if (_untitled.Count > 0) throw new InvalidOperationException("请先为未命名Emitter选择保存位置。");
        if (Catalog.FilePath.Length == 0) throw new InvalidOperationException("请先为Boss目录选择保存位置。");
        Catalog.ValidateCatalog(CaptureEmitters());
        foreach (var document in _emitters.Values) document.Validate();
        // 保存会更新映射，冻结待保存文档队列后逐个写盘。
        foreach (var document in _emitters.Values.Where(document => document.Dirty).ToArray()) SaveEmitter(document, document.FilePath);
        SaveCatalog(Catalog.FilePath);
    }

    /// <summary>规范化资源路径，以文件身份共享文档。</summary>
    /// <param name="path">资源路径或操作系统路径。</param>
    /// <returns>完整操作系统路径。</returns>
    private static string FullPath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
}
