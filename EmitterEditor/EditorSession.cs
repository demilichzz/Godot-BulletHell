using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>目录与外部Emitter共用的编辑会话，同一路径只持有一份可撤销文档。</summary>
public sealed class EditorSession
{
    /// <summary>保存列表、Boss与阶段的唯一文档。</summary>
    public EmitterDocument Catalog { get; } = new(true);
    // Windows资源路径规范化后忽略大小写，防止相同文件出现两个草稿。
    private readonly Dictionary<string, EmitterDocument> _emitters = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>所有已打开的Emitter文档。</summary>
    public IEnumerable<EmitterDocument> Emitters => _emitters.Values;
    /// <summary>目录或任意Emitter仍有未保存修改及草稿。</summary>
    public bool HasUnsaved => Catalog.Dirty || Catalog.Draft is not null || _emitters.Values.Any(document => document.Dirty || document.Draft is not null);

    /// <summary>取得指定资源的共享编辑文档，首次使用时从磁盘读取。</summary>
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

    /// <summary>创建预览资源快照；每次调用返回独立运行树，之后的文档修改不影响预览。</summary>
    /// <returns>只保存JSON文本的独立创建函数。</returns>
    public Func<string, VBulletEmitter> CaptureEmitters()
    {
        // 已打开文件采用已应用的编辑文本，未打开文件在首次解析时冻结。
        var snapshots = _emitters.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase);
        var drafts = _emitters.Where(pair => pair.Value.Draft is not null).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            // 引用到未应用草稿时明确拒绝，不能静默预览旧值。
            string full = FullPath(path);
            if (drafts.Contains(full)) throw new InvalidOperationException("请先应用Emitter草稿：" + path);
            if (!snapshots.TryGetValue(full, out string? json)) snapshots[full] = json = File.ReadAllText(full);
            return VBulletEmitter.FromJson(json, path);
        };
    }

    /// <summary>预先校验全部待保存文档，然后先保存Emitter、最后保存目录。</summary>
    public void SaveAll()
    {
        if (Catalog.Draft is not null || _emitters.Values.Any(document => document.Draft is not null))
            throw new InvalidOperationException("请先应用所有JSON草稿。");
        if (Catalog.FilePath.Length == 0) throw new InvalidOperationException("请先为Boss目录选择保存位置。");
        // 所有文档有效后才开始写盘；单文件仍使用原子替换。
        Catalog.ValidateCatalog(CaptureEmitters());
        foreach (var document in _emitters.Values) document.Validate();
        foreach (var document in _emitters.Values.Where(document => document.Dirty)) document.Save(document.FilePath);
        Catalog.Save(Catalog.FilePath);
    }

    /// <summary>规范化资源路径，以文件身份共享文档。</summary>
    /// <param name="path">资源路径或操作系统路径。</param>
    /// <returns>完整操作系统路径。</returns>
    private static string FullPath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
}
