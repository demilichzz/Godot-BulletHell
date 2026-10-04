using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>从一个JSON加载有序Boss列表，Boss内嵌阶段，Emitter仍引用独立文件。</summary>
public sealed class BossCatalog
{
    /// <summary>正式目录的唯一资源路径。</summary>
    public const string DefaultPath = "res://Data/BossCatalog.json";
    /// <summary>选择界面与编辑器使用的有序只读目录，允许为空。</summary>
    public IReadOnlyList<BossData> Entries { get; }

    /// <summary>冻结已构造的目录顺序并验证唯一标识。</summary>
    /// <param name="entries">可选Boss数据；空值建立空目录。</param>
    public BossCatalog(IEnumerable<BossData>? entries = null)
    {
        Entries = Array.AsReadOnly(entries?.ToArray() ?? Array.Empty<BossData>());
        Validate();
    }

    /// <summary>读取完整目录；只有全部配置成功才返回新目录。</summary>
    /// <param name="path">Godot或操作系统路径，默认正式目录。</param>
    /// <returns>已校验且不含战斗实例的目录。</returns>
    public static BossCatalog Load(string path = DefaultPath) => FromJson(JsonData.ReadFile(path), path);

    /// <summary>解析Bosses有序数组，拒绝旧路径列表、未知字段及重复ID。</summary>
    /// <param name="json">内嵌Boss与阶段的目录JSON。</param>
    /// <param name="sourceName">用于错误定位的来源。</param>
    /// <returns>完整有效目录；读取不消耗业务随机。</returns>
    /// <param name="loadEmitter">可选预览资源入口。</param>
    public static BossCatalog FromJson(string json, string sourceName = "内存Boss目录", Func<string, VBulletEmitter>? loadEmitter = null)
        => JsonData.Parse(json, sourceName, root =>
        {
            JsonData.CheckFields(root, new[] { "Bosses" });
            // 局部队列在全部配置通过后一次发布，避免半份目录。
            var bosses = JsonData.Required(root, "Bosses");
            if (bosses.ValueKind != JsonValueKind.Array) throw new JsonException("Bosses必须为有序数组。");
            var entries = new List<BossData>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in bosses.EnumerateArray())
            {
                try
                {
                    // 元素读取共用Boss校验，目录额外检查唯一身份。
                    var data = BossData.Read(element, loadEmitter);
                    if (!identifiers.Add(data.Id)) throw new JsonException("重复Boss ID：" + data.Id);
                    entries.Add(data);
                }
                catch (Exception error) when (error is JsonException or ArgumentException or System.IO.IOException)
                { throw new JsonException($"Bosses[{entries.Count}]: {error.Message}", error); }
            }
            return new BossCatalog(entries);
        });

    /// <summary>验证全部配置和区分大小写的ID唯一性。</summary>
    public void Validate()
    {
        // 目录外部构造入口也遵守相同身份约束。
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var data in Entries)
        {
            if (data is null) throw new ArgumentException("Boss目录包含空配置。");
            data.Validate();
            if (!identifiers.Add(data.Id)) throw new ArgumentException("重复Boss ID：" + data.Id);
            if (data.Phases.Count == 0) BossFactory.ValidateProfile(data.PhaseProfile);
        }
    }

    /// <summary>按稳定ID查找Boss，缺失时返回空。</summary>
    /// <param name="id">区分大小写的完整Boss标识。</param>
    /// <returns>匹配的数据或空值。</returns>
    public BossData? Find(string id) => Entries.FirstOrDefault(data => data.Id == id);

    /// <summary>按稳定ID取得Boss，缺失时提供明确错误。</summary>
    /// <param name="id">完整Boss标识。</param>
    /// <returns>已存在的Boss数据。</returns>
    public BossData Get(string id) => Find(id) ?? throw new ArgumentException("Boss不存在：" + id, nameof(id));
}
