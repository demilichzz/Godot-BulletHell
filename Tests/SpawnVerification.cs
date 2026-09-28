using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>基础列表、延迟出生、毫秒寿命和取消行为的定向验证。</summary>
public partial class BattleVerification
{
    // 基础列表两项、两轮，四颗在0、50、100、150ms出生。
    private const string SpawnFixture = """
    {"Core":{"Type":"VBullet","Amount":2,"LifeTimeMs":1000,"CreatePositionMode":"Snapshot"},
     "Display":{},"BaseAttributes":[
       {"Speed":0,"SpawnDelayMs":0,"RefMoveQueue":[{"Type":"XYMove","X":10,"Y":0}]},
       {"Speed":0,"SpawnDelayMs":50,"RefMoveQueue":[{"Type":"XYMove","X":20,"Y":0}]}],
     "AddAttributes":{"SpawnDelayMs":100,"RefMoveQueue":[{"Type":"XYMove","X":100,"Y":0}]},
     "Timeline":[{"StartMs":0}],"MemberTimeline":[{"StartMs":50,"Set":{"Speed":0,"Angle":1}}]}
    """;

    /// <summary>关闭非目标对象行为，运行初始化派发。</summary>
    /// <param name="battle">隔离战斗。</param>
    /// <param name="json">完整Creator定义。</param>
    /// <param name="stop">停止时的子弹策略。</param>
    /// <returns>已经触发零时刻规则的发射器。</returns>
    private VBulletEmitter StartSpawnFixture(BattleManager battle, string json, string stop = "KeepBullets")
    {
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var emitter = VBulletEmitter.FromJson(EmitterJson(json, stop));
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        return emitter;
    }

    /// <summary>验证实际出生时刻、批次身份、父引用、容量和随机。</summary>
    private void VerifySpawnLists()
    {
        // 逐颗年龄从真正出生开始，固定增量按轮次而非平铺索引。
        var battle = CreateBattle(out var world);
        var emitter = StartSpawnFixture(battle, SpawnFixture);
        var creator = emitter.Root;
        Check(creator.TotalAmount == 4 && creator.Members.Count == 1, "基础列表乘轮次，首批不提前创建延迟对象");
        Check(creator.Members[0].WorldPosition.X == 10 && creator.Members[0].Age == 0, "首颗零龄位置");
        for (int step = 1; step <= 9; step++)
        {
            battle.StepFixed(Vector2.Zero, false);
            Check(creator.Members.Count == 1 + step / 3, "每三固定步出生一颗");
            if (step % 3 == 0) Check(creator.Members[^1].Age == 0, "当步新生不提前计龄");
        }
        Check(creator.Batches.Count == 1 && creator.Members.Select(n => n.BirthIndex).SequenceEqual(new[] {0,1,2,3}), "全部轮次属于同一批且索引固定");
        Check(creator.Members.Select(n => n.WorldPosition.X).SequenceEqual(new float[] {10,20,110,120}), "逐轮对应项固定增量");
        Check(creator.Members[2].Angle == 1 && creator.Members[3].Angle == 0, "成员动作按各自出生计时");
        emitter.Stop();
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(creator.Members[3].Angle == 1, "KeepBullets保留已生对象成员动作");
        world.Free();

        // 同一批先暂时清空、另一批先有成员，恢复时仍按建立序展示。
        battle = CreateBattle(out world);
        var recovery = JsonNode.Parse(SpawnFixture)!;
        recovery["Core"]!["Amount"] = 1;
        recovery["BaseAttributes"]![1]!["SpawnDelayMs"] = 100;
        recovery["Timeline"] = JsonNode.Parse("[{\"AtMs\":[0,50]}]");
        emitter = StartSpawnFixture(battle, recovery.ToJsonString());
        creator = emitter.Root;
        var originalView = creator.Batches[0];
        battle.Bullets.Release((VBullet)creator.Members[0]);
        Check(creator.Batches.Count == 0, "未生对象不以空行暴露");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        var laterView = creator.Batches[0];
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(creator.Batches.Count == 2 && ReferenceEquals(creator.Batches[0], originalView)
            && ReferenceEquals(creator.Batches[1], laterView) && creator.Batches[0][0].BirthIndex == 1, "空批次恢复同一视图且早批次仍在前");
        emitter.Stop();
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(creator.Members.Count == 2, "停止取消尚未出生的成员");
        world.Free();

        // 先出生索引可以晚于后出生索引，索引身份不能由集合下标决定。
        battle = CreateBattle(out world);
        var inverted = JsonNode.Parse(SpawnFixture)!;
        inverted["Core"]!["Amount"] = 1;
        inverted["BaseAttributes"]![0]!["SpawnDelayMs"] = 100;
        inverted["BaseAttributes"]![1]!["SpawnDelayMs"] = 0;
        emitter = StartSpawnFixture(battle, inverted.ToJsonString());
        Check(emitter.Root.Members[0].BirthIndex == 1, "交错延迟不重排固定索引");
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(emitter.Root.Members.Select(n => n.BirthIndex).SequenceEqual(new[] {1,0}), "存活成员列表按实际出生顺序");
        world.Free();

        VerifySpawnReferences();
        VerifySpawnCapacityAndRandom();
        VerifySpawnValidation();
    }

    /// <summary>移动父对象的Snapshot取样、Follow、父死亡及两种停止策略。</summary>
    private void VerifySpawnReferences()
    {
        foreach (string mode in new[] { "Snapshot", "Follow" })
        {
            // 子节点的首颗也延迟，Snapshot必须在触发而非首颗出生时取样。
            var child = JsonNode.Parse(SpawnFixture)!;
            child["Core"]!["Amount"] = 1;
            child["Core"]!["CreatePositionMode"] = mode;
            child["BaseAttributes"]![0]!["SpawnDelayMs"] = 50;
            child["BaseAttributes"]![1]!["SpawnDelayMs"] = 100;
            var pending = child["BaseAttributes"]![1]!.DeepClone();
            pending["SpawnDelayMs"] = 200;
            child["BaseAttributes"]!.AsArray().Add(pending);
            var root = JsonNode.Parse("""
            {"Core":{"Type":"VNode","LifeTimeMs":150},"BaseAttributes":[{"Speed":60,
             "RefMoveQueue":[{"Type":"XYMove","X":10000}]}],"Timeline":[{"StartMs":0}]}
            """)!;
            root["Children"] = new JsonArray(child);
            var battle = CreateBattle(out var world);
            var emitter = StartSpawnFixture(battle, root.ToJsonString());
            var creator = emitter.Root.Children[0];
            Check(creator.Members.Count == 0, "延迟出生不提前创建子弹");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(Math.Abs(creator.Members[0].WorldPosition.X - (mode == "Snapshot" ? 10010 : 10013)) < 0.001, "首颗延迟Snapshot原点与Follow当前位置");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(Math.Abs(creator.Members[1].WorldPosition.X - (mode == "Snapshot" ? 10020 : 10026)) < 0.001, "整批Snapshot固定而Follow逐颗取位置");
            Check(Math.Abs(creator.Members[0].WorldPosition.X - (mode == "Snapshot" ? 10010 : 10016)) < 0.001, "Follow已生对象继续平移跟随");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            var position = creator.Members[0].WorldPosition;
            Check(emitter.Root.Members.Count == 0 && creator.Members.Count == 2 && !creator.Members[0].IsFollowing, "父到期保留已生子弹并脱离参考");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(creator.Members[0].WorldPosition == position, "父消失后不继承父速度");
            world.Free();
        }
        foreach (string mode in new[] { "KeepBullets", "ClearBullets" })
        {
            var battle = CreateBattle(out var world);
            var emitter = StartSpawnFixture(battle, SpawnFixture, mode);
            emitter.Stop();
            VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
            Check(emitter.Root.Members.Count == (mode == "KeepBullets" ? 1 : 0), "停止策略取消待生且处理已生对象");
            Check(emitter.Timeline is null && emitter.Root.Members.All(node => node.Timeline!.ActionCount == 0), "停止后已完成或取消的任务没有残留");
            world.Free();
        }
        // 子弹父节点停止生成，但自己的成员动作仍然执行。
        var parentJson = JsonNode.Parse(SpawnFixture)!;
        parentJson["Core"]!["Amount"] = 1;
        parentJson["BaseAttributes"] = JsonNode.Parse("[{\"Speed\":0}]");
        parentJson.AsObject().Remove("AddAttributes");
        parentJson["Children"] = new JsonArray(JsonNode.Parse(SpawnFixture));
        var parentBattle = CreateBattle(out var parentWorld);
        var parentEmitter = StartSpawnFixture(parentBattle, parentJson.ToJsonString());
        parentEmitter.Stop();
        VerificationClock.BattleSeconds(parentBattle, 0.2, Vector2.Zero, false);
        Check(parentEmitter.Root.Children[0].Members.Count == 1 && parentEmitter.Root.Members[0].Angle == 1,
            "VBullet父的延迟生成取消不影响其成员参数动作");
        parentWorld.Free();
    }

    /// <summary>满额期间不抽样、容量恢复保留原索引，以及批次共享随机。</summary>
    private void VerifySpawnCapacityAndRandom()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop(); battle.Player.Attack.Stop();
        for (int i = 0; i < BattleConfig.MaxBullets; i++)
            battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.ScaleSet) with { Position = new Vector2(-10000,-10000), Speed = 0 });
        var json = JsonNode.Parse(SpawnFixture)!;
        json["RandDiffAttributes"] = JsonNode.Parse("""
        {"Batch":{"Angle":0.2},"Member":{"Speed":20}}
        """);
        VMath.setRandomSeed(915);
        double untouched = VMath.getRandomDouble(0,1);
        VMath.setRandomSeed(915);
        var emitter = StartSpawnFixture(battle, json.ToJsonString());
        Check(emitter.Root.Members.Count == 0 && VMath.getRandomDouble(0,1) == untouched, "批次触发时满额仍不抽样");
        // 释放两颗，随后两次出生使用一次共享角度和两次独立速度。
        battle.Bullets.Release(battle.Bullets.ActiveBullets[0]);
        battle.Bullets.Release(battle.Bullets.ActiveBullets[0]);
        VMath.setRandomSeed(916);
        double angle = VMath.getRandomDiff(0.2, RandomDiffMode.Center);
        double speed1 = VMath.getRandomDiff(20, RandomDiffMode.Center);
        double speed2 = VMath.getRandomDiff(20, RandomDiffMode.Center);
        double next = VMath.getRandomDouble(0,1);
        VMath.setRandomSeed(916);
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(emitter.Root.Members.Select(n => n.BirthIndex).SequenceEqual(new[] {1,2}), "满额跳过首颗，后续容量恢复不重排索引");
        var first = emitter.Root.Members[0]; var second = emitter.Root.Members[1];
        // 首颗已执行50ms成员动作，第二颗仍保留共享角度。
        Check(first.Angle == 1 && first.Speed == 0 && second.Angle == VMath.StandardizationAngle(angle) && second.Speed == speed2,
            "共享值一次抽样且独立偏移不累计");
        Check(VMath.getRandomDouble(0,1) == next, "只为实际生成对象消耗随机");
        Check(emitter.Root.Batches.Count == 1, "首次有容量才公开批次");
        battle.Bullets.Clear();
        VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
        Check(emitter.Root.Members.Count == 0 && battle.Bullets.ActiveCount == 0, "清场取消待生任务");
        world.Free();
    }

    /// <summary>严格新格式、整数时间、溢出与毫秒寿命边界。</summary>
    private void VerifySpawnValidation()
    {
        foreach (string invalid in new[]
        {
            SpawnFixture.Replace("\"LifeTimeMs\":1000", "\"LifeTimeS\":1"),
            SpawnFixture.Replace("\"LifeTimeMs\":1000", "\"LifeTimeMs\":1.5"),
            SpawnFixture.Replace("\"LifeTimeMs\":1000", "\"LifeTimeMs\":\"1000/2\""),
            SpawnFixture.Replace("\"LifeTimeMs\":1000", "\"LifeTimeMs\":9223372036854775807"),
            SpawnFixture.Replace("\"SpawnDelayMs\":50", "\"SpawnDelayMs\":-1"),
            SpawnFixture.Replace("\"SpawnDelayMs\":50", "\"SpawnDelayMs\":\"50\""),
            SpawnFixture.Replace("\"SpawnDelayMs\":100", "\"SpawnDelayMs\":9223372036854775807"),
            SpawnFixture.Replace("\"Amount\":2", "\"Amount\":2147483647"),
            SpawnFixture.Replace("\"Display\":{}", "\"Display\":{},\"RandDiffAttributes\":{\"Batch\":{\"SpawnDelayMs\":0}}"),
            SpawnFixture.Replace("\"Display\":{}", "\"Display\":{},\"RandDiffAttributes\":{\"Member\":{\"Speed\":-1}}"),
            SpawnFixture.Replace("\"Display\":{}", "\"Display\":{},\"PositionAttributes\":{}"),
            SpawnFixture.Replace("\"Display\":{}", "\"Display\":{},\"RandDiffAttributes\":{\"Batch\":{\"RefMoveQueue\":[{\"Type\":\"PMove\"}]}}")
        })
        {
            try { VBulletCreator.FromJson(invalid); Check(false,"新格式非法值必须拒绝"); }
            catch (JsonException) { _checks++; }
        }
        var battle = CreateBattle(out var world);
        var emitter = StartSpawnFixture(battle, SpawnFixture.Replace("\"LifeTimeMs\":1000", "\"LifeTimeMs\":50"));
        var first = emitter.Root.Members[0];
        VerificationClock.BattleSeconds(battle, 2.0/60, Vector2.Zero, false);
        Check(first.IsAlive, "50ms寿命在第二步仍存活");
        battle.StepFixed(Vector2.Zero,false);
        Check(!first.IsAlive && emitter.Root.Members.Count == 1 && emitter.Root.Members[0].Age == 0, "50ms边界旧成员释放，新成员零龄出生");
        var newborn = emitter.Root.Members[0];
        battle.StepFixed(Vector2.Zero,false);
        newborn.ApplyParameters(new ParameterActionAttribute { LifeTimeMs = 1 });
        Check(newborn.IsAlive, "缩短寿命不在动作内立即注销");
        battle.StepFixed(Vector2.Zero,false);
        Check(!newborn.IsAlive, "下一固定步检查缩短后的总寿命");
        world.Free();
    }
}
