using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>验证单文件目录的顺序、稳定身份、严格嵌套校验和失败隔离。</summary>
public partial class BattleVerification
{
    /// <summary>检查目录格式与数据身份，不执行其他专项组。</summary>
    private void VerifyBossCatalog()
    {
        // 当前生产文件供重排、复制和错误输入构造独立快照。
        var root = JsonNode.Parse(JsonData.ReadFile(BossCatalog.DefaultPath))!.AsObject();
        var source = root["Bosses"]!.AsArray();
        var catalog = BossCatalog.FromJson(root.ToJsonString());
        Check(catalog.Entries.Count == 23 && catalog.Entries.Sum(boss => boss.PhaseCount) == 67, "目录保留23个Boss和67个阶段");
        Check(catalog.Get("Boss_01") == catalog.Entries[0] && catalog.Find("boss_01") is null, "稳定ID区分大小写");
        Check(BossCatalog.FromJson("{\"Bosses\":[]}").Entries.Count == 0, "空目录合法");
        // 调整数组顺序不修改Boss身份，原目录快照不受影响。
        var reordered = new JsonObject { ["Bosses"] = new JsonArray(source[1]!.DeepClone(), source[0]!.DeepClone()) };
        var moved = BossCatalog.FromJson(reordered.ToJsonString());
        Check(moved.Entries[0].Id == "Boss_02" && moved.Get("Boss_01").PhaseCount == 3, "目录顺序来自数组且ID不随位置变化");
        Check(catalog.Entries[0].Id == "Boss_01", "加载新目录不污染已加载目录");
        // 加载配置不得消耗默认流或独立业务随机状态。
        VMath.setRandomSeed(81);
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(81);
        BossCatalog.FromJson(reordered.ToJsonString());
        Check(VMath.getRandomDouble(0, 1) == next, "目录加载不消耗业务随机");
        // 逐类错误须包含对应上下文，且不能返回半份目录。
        RejectCatalog("{}", "Bosses");
        RejectCatalog("{\"Bosses\":null}", "Bosses");
        RejectCatalog("{\"Bosses\":{}}", "Bosses");
        RejectCatalog("{\"Bosses\":[],\"JsonFiles\":[]}", "JsonFiles");
        RejectCatalog("{\"Bosses\":[],\"Bosses\":[]}", "重复字段");
        RejectCatalog("{\"Bosses\":[null]}", "Bosses[0]");
        var duplicate = new JsonObject { ["Bosses"] = new JsonArray(source[0]!.DeepClone(), source[0]!.DeepClone()) };
        RejectCatalog(duplicate.ToJsonString(), "Bosses[1]");
        var invalid = reordered.DeepClone().AsObject();
        invalid["Bosses"]![0]!["Phases"] = new JsonArray();
        RejectCatalog(invalid.ToJsonString(), "Bosses[0]");
        invalid = reordered.DeepClone().AsObject();
        invalid["Bosses"]![1]!["Phases"]![0]!["Emitters"] = new JsonArray("res://missing-emitter.json");
        RejectCatalog(invalid.ToJsonString(), "Phases[0]");
        invalid = reordered.DeepClone().AsObject();
        invalid["Bosses"]![0]!["Core"]!["Id"] = "";
        RejectCatalog(invalid.ToJsonString(), "Bosses[0]");
        Check(catalog.Get("Boss_01").PhaseCount == 3, "错误加载后原目录仍可使用");
    }

    /// <summary>确认目录错误被拒绝且包含可定位的字段或数组下标。</summary>
    /// <param name="json">预期失败的目录JSON。</param>
    /// <param name="context">错误消息必须包含的定位文本。</param>
    private void RejectCatalog(string json, string context)
    {
        try { BossCatalog.FromJson(json, "目录验证"); }
        catch (JsonException error)
        {
            Check(error.Message.Contains(context, StringComparison.Ordinal), "目录错误定位：" + context);
            return;
        }
        throw new Exception("未拒绝无效目录：" + context);
    }
}
