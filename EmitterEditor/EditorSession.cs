using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Emitter模式独立的文件会话，同一路径只持有一份可撤销文档。</summary>
public sealed class EditorSession
{
    // Windows规范化路径忽略大小写，未命名文档单独登记，不制造文件身份。
    private readonly Dictionary<string, EditorDocument> _emitters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EditorDocument> _untitled = new();
    /// <summary>Emitter模式全部有名和未命名文档。</summary>
    public IEnumerable<EditorDocument> Emitters => _emitters.Values.Concat(_untitled);
    /// <summary>仅检查本模式文档，不读取Boss会话。</summary>
    public bool HasUnsaved => Emitters.Any(document => document.Dirty || document.Draft is not null);

    /// <summary>创建并登记未命名Emitter，历史和草稿由会话持有。</summary>
    /// <returns>独立的新文档。</returns>
    public EditorDocument NewEmitter()
    {
        // 新文档暂时没有文件身份，由首次保存建立映射。
        var document = new EditorDocument();
        _untitled.Add(document);
        return document;
    }
    /// <summary>用户确认放弃后移除未命名文档，不删除任何文件。</summary>
    /// <param name="document">需要丢弃的未命名文档。</param>
    public void DiscardUntitled(EditorDocument document)
    {
        if (document.FilePath.Length != 0) throw new InvalidOperationException("已命名文档须保留在会话中。");
        _untitled.Remove(document);
    }

    /// <summary>取得指定资源的共享文档，首次打开时才读取磁盘。</summary>
    /// <param name="path">Godot资源路径或绝对文件路径。</param>
    /// <returns>本会话唯一的文件文档。</returns>
    public EditorDocument OpenEmitter(string path)
    {
        // 文件存在且成功解析后才登记，失败不污染会话。
        string full = FullPath(path);
        if (_emitters.TryGetValue(full, out var current)) return current;
        var document = new EditorDocument();
        document.Open(full);
        _emitters.Add(full, document);
        return document;
    }

    /// <summary>保存或另存Emitter，成功后才更新路径身份；拒绝覆盖另一份已打开文档。</summary>
    /// <param name="document">本会话持有的Emitter。</param>
    /// <param name="path">目标文件路径，另存后原文件保持不变。</param>
    public void SaveEmitter(EditorDocument document, string path)
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
    private string EmitterTarget(EditorDocument document, string path)
    {
        if (!Emitters.Contains(document)) throw new InvalidOperationException("文档不属于当前会话。");
        if (document.Draft is not null) throw new InvalidOperationException("请先应用JSON草稿。");
        // 目标文件身份必须在任何写入前检查。
        string target = FullPath(path);
        if (_emitters.TryGetValue(target, out var existing) && !ReferenceEquals(existing, document))
            throw new InvalidOperationException("目标文件已在会话中打开，请切换到该文件编辑或选择其他保存位置。");
        EditorDocument.CheckTargetKind(target, EditorDocumentKind.Emitter);
        return target;
    }

    /// <summary>成功写盘后更新Emitter文件身份，不改变撤销与草稿。</summary>
    /// <param name="document">刚完成保存的文档。</param>
    /// <param name="target">已经写入的规范化目标路径。</param>
    private void RegisterSavedEmitter(EditorDocument document, string target)
    {
        // 移除旧别名后登记新路径，其他文件的会话状态不变。
        foreach (string old in _emitters.Where(pair => ReferenceEquals(pair.Value, document)).Select(pair => pair.Key).ToArray())
            _emitters.Remove(old);
        _untitled.Remove(document);
        _emitters[target] = document;
    }

    /// <summary>规范化资源路径，以文件身份共享文档。</summary>
    /// <param name="path">资源路径或操作系统路径。</param>
    /// <returns>完整文件路径。</returns>
    private static string FullPath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
}
