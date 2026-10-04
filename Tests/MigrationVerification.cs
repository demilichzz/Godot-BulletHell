using Godot;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>锁定目录迁移前的数据与B01固定步行为，不记录将被替换的类名或树身份。</summary>
public partial class BattleVerification
{
    /// <summary>对照迁移前冻结的目录顺序、完整配置及两条战斗轨迹。</summary>
    private void VerifyMigrationBaseline()
    {
        // 配置摘要覆盖全部Boss字段、阶段顺序和Emitter引用，不依赖JSON排版。
        var catalog = BossCatalog.Load();
        catalog.Validate();
        var entries = new JsonArray();
        using var document = JsonDocument.Parse(JsonData.ReadFile(BossCatalog.DefaultPath));
        foreach (var reference in document.RootElement.GetProperty("Bosses").EnumerateArray())
        {
            using var bossFile = JsonDocument.Parse(JsonData.ReadFile(reference.GetString()!));
            var boss = bossFile.RootElement;
            entries.Add(new JsonObject
            {
                ["Id"] = boss.GetProperty("Core").GetProperty("Id").GetString(),
                ["Hash"] = CanonicalHash(boss)
            });
        }
        // 两条轨迹分别覆盖圆周移动和阶段切换、回退、重开。
        var actual = new JsonObject
        {
            ["Catalog"] = entries,
            ["B01Phase01"] = CaptureMigrationBattle(false),
            ["B01Lifecycle"] = CaptureMigrationBattle(true)
        };
        var expected = JsonNode.Parse(JsonData.ReadFile("res://Tests/Fixtures/MigrationBaseline.json"))!.AsObject();
        Check(JsonNode.DeepEquals(actual["Catalog"], expected["Catalog"]), "全部Boss配置与展示顺序保持迁移前基线");
        Check(JsonNode.DeepEquals(actual["B01Phase01"], expected["B01Phase01"]), "B01首阶段720步的运动、弹幕和随机状态保持基线");
        Check(JsonNode.DeepEquals(actual["B01Lifecycle"], expected["B01Lifecycle"]), "阶段切换、回退与重开保持基线");
    }

    /// <summary>按属性名排序计算配置摘要，数组声明顺序保持不变。</summary>
    /// <param name="element">当前配置对象。</param>
    /// <returns>完整配置的SHA256十六进制摘要。</returns>
    private static string CanonicalHash(JsonElement element)
    {
        // UTF-8输出不包含缩进与平台换行。
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, element);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>递归保持数组顺序并规范对象键顺序，表达式字符串不求值。</summary>
    /// <param name="writer">本次摘要的JSON输出流。</param>
    /// <param name="element">待写入的值。</param>
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            // 同一对象的键排序仅用于比较，不改变业务数据。
            foreach (var property in element.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    /// <summary>记录12秒真实60Hz战斗；不比较Emitter数量、Creator ID或管理器内部任务数。</summary>
    /// <param name="lifecycle">为真时加入阶段切换、回退和整场重开。</param>
    /// <returns>逐步状态与最后默认随机流下一值的二进制摘要。</returns>
    private string CaptureMigrationBattle(bool lifecycle)
    {
        // 每条轨迹都有全新的世界、随机初态与玩家保护状态。
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        try
        {
            // 从零开始的固定输入步号，720步跨越两次圆周选点。
            for (int frame = 0; frame < 720; frame++)
            {
                if (lifecycle && frame is 180 or 360) battle.Boss.TakeDamage(100);
                if (lifecycle && frame == 660) { battle.Restart(); battle.Player.Attack.Stop(); }
                battle.StepFixed(frame / 120 % 2 == 0 ? Vector2.Left : Vector2.Right,
                    frame % 90 == 0, lifecycle && frame == 540, lifecycle && frame == 600);
                writer.Write(battle.Timers.NowUnits);
                writer.Write((int)battle.State);
                WriteVector(writer, battle.Player.Position);
                writer.Write(battle.Player.Health.Hp);
                WriteVector(writer, battle.Boss.Position);
                WriteVector(writer, battle.Boss.CurrentPhase?.MoveTarget ?? Vector2.Zero);
                writer.Write(battle.Boss.PhaseIndex);
                writer.Write(battle.Boss.Hp);
                writer.Write(battle.Boss.PhaseHp);
                writer.Write(battle.Bullets.ActiveCount);
                // 列表顺序影响容量和碰撞处理，须连同数值一并冻结。
                foreach (var bullet in battle.Bullets.ActiveBullets)
                {
                    WriteVector(writer, bullet.WorldPosition);
                    WriteVector(writer, bullet.SpawnPosition);
                    WriteVector(writer, bullet.Velocity);
                    writer.Write(bullet.Angle); writer.Write(bullet.Speed);
                    writer.Write(bullet.AAngle); writer.Write(bullet.ASpeed);
                    writer.Write(bullet.Age); writer.Write(bullet.LifeTimeMs ?? -1);
                    writer.Write(bullet.Radius); writer.Write((int)bullet.Team); writer.Write(bullet.Damage);
                    // B01使用贴图子弹，包含图集帧以覆盖两组颜色差异。
                    writer.Write(bullet.GetNode<Sprite2D>("Sprite").Frame);
                }
            }
            writer.Write(VMath.getRandomInt(int.MinValue, int.MaxValue));
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        }
        finally { world.Free(); }
    }

    /// <summary>写入Vector2原始单精度分量，不经过显示字符串舍入。</summary>
    /// <param name="writer">当前轨迹输出流。</param>
    /// <param name="value">世界像素坐标或像素每秒速度。</param>
    private static void WriteVector(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X); writer.Write(value.Y);
    }
}
