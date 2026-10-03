using Godot;
using System;
using System.Collections.Generic;

/// <summary>保存 Boss 的展示顺序，并按唯一标识查询。</summary>
[GlobalClass]
public partial class BossCatalog : Resource
{
    /// <summary>按选择界面顺序排列的 Boss 配置，允许为空。</summary>
    [Export] public Godot.Collections.Array<string> JsonFiles { get; set; } = new();
    // 目录只加载一次；构造成功后原子发布，失败不留下半份目录。
    private Godot.Collections.Array<BossData> _entries = new();
    private bool _loaded;
    /// <summary>按目录顺序加载的配置；代码可注入隔离测试目录。</summary>
    [Export] public Godot.Collections.Array<BossData> Entries
    {
        get
        {
            if (!_loaded && JsonFiles.Count > 0)
            {
                // 临时队列保证引用错误不会污染当前目录。
                var loaded = new Godot.Collections.Array<BossData>();
                foreach (string path in JsonFiles) loaded.Add(BossData.Load(path));
                _entries = loaded;
                _loaded = true;
            }
            return _entries;
        }
        set { _entries = value; _loaded = true; }
    }
    /// <summary>验证全部配置和标识唯一性。</summary>
    public void Validate()
    {
        // 已出现的标识集合用于检查重复配置。
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var data in Entries)
        {
            if (data is null) throw new ArgumentException("Boss 目录包含空配置。");
            data.Validate();
            if (!identifiers.Add(data.Id)) throw new ArgumentException($"重复的 Boss ID：{data.Id}");
            if (data.Phases.Count == 0) BossFactory.ValidateProfile(data.PhaseProfile);
        }
    }
    /// <summary>查找指定 Boss，不存在时返回空。</summary>
    /// <param name="id">区分大小写的 Boss 唯一标识。</param>
    /// <returns>匹配的静态配置，未找到时为空。</returns>
    public BossData? Find(string id)
    {
        // 顺序查找，目录通常只包含少量 Boss。
        foreach (var data in Entries) if (data.Id == id) return data;
        return null;
    }
}
