using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>验证路径目录、独立Boss文件、顺序及严格错误定位。</summary>
public partial class BattleVerification
{
    /// <summary>验证目录迁移后的正式加载和错误输入，不运行其他组。</summary>
    private void VerifyBossCatalog()
    {
        // 正式数据的路径、配置及顺序基线。
        string json = JsonData.ReadFile(BossCatalog.DefaultPath);
        var paths = BossCatalog.ReadPaths(json);
        var catalog = BossCatalog.FromJson(json);
        Check(catalog.Entries.Count == 23 && catalog.Entries.Sum(boss => boss.PhaseCount) == 67, "保留23个Boss与67阶段");
        Check(BossData.Load(paths[0]).Id == "Boss_01", "独立Boss文件可以直接加载");
        Check(catalog.Get("Boss_01") == catalog.Entries[0] && catalog.Find("boss_01") is null, "稳定ID区分大小写");
        Check(BossCatalog.FromJson("{\"Bosses\":[]}").Entries.Count == 0, "空目录合法");
        var reordered = new JsonObject { ["Bosses"] = new JsonArray(paths[1], paths[0]) };
        var moved = BossCatalog.FromJson(reordered.ToJsonString());
        Check(moved.Entries[0].Id == "Boss_02" && moved.Get("Boss_01").PhaseCount == 3, "顺序来自路径数组");
        VMath.setRandomSeed(81); double next = VMath.getRandomDouble(0, 1); VMath.setRandomSeed(81);
        BossCatalog.FromJson(reordered.ToJsonString());
        Check(VMath.getRandomDouble(0, 1) == next, "加载不消耗随机");
        // 路径列表严格拒绝旧对象与重复身份；错误保留目录位置。
        RejectCatalog("{}", "Bosses");
        RejectCatalog("{\"Bosses\":null}", "Bosses");
        RejectCatalog("{\"Bosses\":{}}", "Bosses");
        RejectCatalog("{\"Bosses\":[],\"Unknown\":[]}", "Unknown");
        RejectCatalog("{\"Bosses\":[],\"Bosses\":[]}", "重复字段");
        RejectCatalog("{\"Bosses\":[null]}", "Bosses[0]");
        RejectCatalog("{\"Bosses\":[{}]}", "Bosses[0]");
        foreach (string invalid in new[] { "B01.json", "res://missing-boss.json", "res://../outside.json", "res://Data/Emitters/B01P01_Emitter01.json" })
            RejectCatalog(new JsonObject { ["Bosses"] = new JsonArray(invalid) }.ToJsonString(), "Bosses[0]");
        RejectCatalog(new JsonObject { ["Bosses"] = new JsonArray(paths[0], "res://Data/Bosses/../Bosses/B01.json") }.ToJsonString(), "重复Boss文件");
        // 注入冻结文本只替换文件读取，仍经过全部正式Boss和阶段校验。
        string first = JsonData.ReadFile(paths[0]);
        RejectCatalog(reordered.ToJsonString(), "重复Boss ID", _ => first);
        var invalidBoss = JsonNode.Parse(first)!.AsObject(); invalidBoss["Phases"] = new JsonArray();
        RejectCatalog(new JsonObject { ["Bosses"] = new JsonArray(paths[0]) }.ToJsonString(), paths[0], _ => invalidBoss.ToJsonString());
        invalidBoss = JsonNode.Parse(first)!.AsObject(); invalidBoss["Phases"]![0]!["Emitters"] = new JsonArray("res://missing-emitter.json");
        RejectCatalog(new JsonObject { ["Bosses"] = new JsonArray(paths[0]) }.ToJsonString(), "Phases[0]", _ => invalidBoss.ToJsonString());
        Check(catalog.Get("Boss_01").PhaseCount == 3, "失败加载不改变已有目录");
    }

    /// <summary>确认加载失败并包含指定上下文。</summary>
    /// <param name="json">非法目录。</param>
    /// <param name="context">预期定位内容。</param>
    /// <param name="readBoss">可选独立Boss文本来源。</param>
    private void RejectCatalog(string json, string context, Func<string, string>? readBoss = null)
    {
        try { BossCatalog.FromJson(json, "目录验证", readBoss: readBoss); }
        catch (JsonException error)
        {
            Check(error.Message.Contains(context, StringComparison.Ordinal), "目录错误定位：" + context); return;
        }
        throw new Exception("未拒绝无效目录：" + context);
    }
}
