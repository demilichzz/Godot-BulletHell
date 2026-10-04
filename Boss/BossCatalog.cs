using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Godot;
using System.Text.Json;

/// <summary>从有序路径列表加载独立Boss文件，阶段继续引用独立Emitter。</summary>
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

    /// <summary>读取严格的Boss路径列表；不加载目标文件，供编辑器显示失效引用。</summary>
    /// <param name="json">仅含Bosses字符串数组的目录。</param>
    /// <param name="sourceName">目录来源。</param>
    /// <returns>原声明顺序的资源路径。</returns>
    public static IReadOnlyList<string> ReadPaths(string json, string sourceName = "内存Boss目录")
        => JsonData.Parse(json, sourceName, root =>
        {
            JsonData.CheckFields(root, new[] { "Bosses" });
            var bosses = JsonData.Required(root, "Bosses");
            if (bosses.ValueKind != JsonValueKind.Array) throw new JsonException("Bosses必须为路径数组。");
            var paths = new List<string>(); var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in bosses.EnumerateArray())
            {
                try
                {
                    string path = JsonData.Read<string>(value);
                    if (!identities.Add(PathIdentity(path))) throw new JsonException("重复Boss文件引用：" + path);
                    paths.Add(path);
                }
                catch (Exception error) when (error is JsonException or ArgumentException)
                { throw new JsonException($"Bosses[{paths.Count}] ({value.GetRawText()}): {error.Message}", error); }
            }
            return Array.AsReadOnly(paths.ToArray());
        });

    /// <summary>验证项目内JSON引用并取得规范化文件身份。</summary>
    /// <param name="path">必须为res://下的JSON路径。</param>
    /// <returns>用于文件去重的绝对路径。</returns>
    internal static string PathIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal)
            || !path.EndsWith(".json", StringComparison.Ordinal)) throw new JsonException("Boss必须引用res://下的独立JSON文件。");
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new JsonException("Boss文件不能位于项目之外。");
        return full;
    }

    /// <summary>解析目录并加载独立Boss，保持顺序、身份及错误上下文。</summary>
    /// <param name="json">有序路径目录。</param>
    /// <param name="sourceName">目录来源。</param>
    /// <param name="loadEmitter">可选独立Emitter创建入口。</param>
    /// <param name="readBoss">可选冻结Boss文本读取入口；省略时读取磁盘。</param>
    /// <returns>完整有效目录。</returns>
    public static BossCatalog FromJson(string json, string sourceName = "内存Boss目录", Func<string, VBulletEmitter>? loadEmitter = null, Func<string, string>? readBoss = null)
        => ReadJson(json, sourceName, loadEmitter, null, readBoss);

    /// <summary>校验冻结目录与引用，不保存校验回调为运行工厂。</summary>
    /// <param name="json">完整目录。</param>
    /// <param name="sourceName">诊断来源。</param>
    /// <param name="validateEmitter">纯Emitter检查。</param>
    /// <param name="readBoss">本次冻结的Boss文本；为空时读取磁盘。</param>
    internal static void CheckJson(string json, string sourceName, Action<string> validateEmitter, Func<string, string>? readBoss = null)
        => ReadJson(json, sourceName, null, validateEmitter, readBoss);

    /// <summary>共用目录读取，单个目标失败时不返回部分目录。</summary>
    /// <param name="json">目录文本。</param>
    /// <param name="sourceName">目录来源。</param>
    /// <param name="loadEmitter">运行实例创建入口。</param>
    /// <param name="validateEmitter">纯引用检查入口。</param>
    /// <param name="readBoss">可选冻结Boss文本。</param>
    /// <returns>完整有效目录。</returns>
    private static BossCatalog ReadJson(string json, string sourceName, Func<string, VBulletEmitter>? loadEmitter, Action<string>? validateEmitter, Func<string, string>? readBoss)
    {
        var entries = new List<BossData>(); var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in ReadPaths(json, sourceName))
        {
            try
            {
                var data = readBoss is null && loadEmitter is null && validateEmitter is null ? BossData.Load(path)
                    : JsonData.Parse((readBoss ?? JsonData.ReadFile)(path), path, root => BossData.Read(root, loadEmitter, validateEmitter));
                if (!identifiers.Add(data.Id)) throw new JsonException("Core.Id: 重复Boss ID：" + data.Id);
                entries.Add(data);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
            { throw new JsonException($"{sourceName}: Bosses[{entries.Count}] ({path}): {error.Message}", error); }
        }
        return new BossCatalog(entries);
    }

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
            if (data.Phases.Count == 0) throw new ArgumentException("Boss必须包含数据阶段：" + data.Id);
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
