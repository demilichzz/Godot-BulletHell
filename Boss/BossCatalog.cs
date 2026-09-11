using Godot;
using System;
using System.Collections.Generic;

/// <summary>保存 Boss 的展示顺序，并按唯一标识查询。</summary>
[GlobalClass]
public partial class BossCatalog : Resource
{
    /// <summary>按选择界面顺序排列的 Boss 配置，允许为空。</summary>
    [Export] public Godot.Collections.Array<BossData> Entries { get; set; } = new();
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
            BossFactory.ValidateProfile(data.PhaseProfile);
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
