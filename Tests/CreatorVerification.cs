using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

/// <summary>单根Creator结构、内部身份及本地派发的定向补充验证。</summary>
public partial class BattleVerification
{
    /// <summary>构造单个当前格式Creator，供树结构用例组合。</summary>
    /// <param name="name">可选名称，按JSON规则转义。</param>
    /// <param name="children">子对象JSON数组。</param>
    /// <param name="bullet">是否为子弹Creator。</param>
    /// <returns>不含Version或Id的完整定义。</returns>
    private static string CreatorFixture(string? name = null, string children = "[]", bool bullet = false)
        => $$"""
        { "Core": { "Type": "{{(bullet ? "VBullet" : "VNode")}}", "Name": {{JsonSerializer.Serialize(name)}} },
          {{(bullet ? "\"Display\": {}," : "")}}
          "BaseAttributes": [{ "Speed": 0 }],
          "Timeline": [{ "StartMs": 0 }], "Children": {{children}} }
        """;

    /// <summary>检查自动编号、跨类别重名、隔离、多批次和本地派发顺序。</summary>
    private void VerifyCreatorIdentityAndDispatch()
    {
        // 根名称故意与第一个孩子ID重名；第三个孩子包含第二层路径。
        string third = CreatorFixture("Third", "[" + CreatorFixture() + "," + CreatorFixture("Deep") + "]");
        string root = CreatorFixture("VN001001", "[" + CreatorFixture("First") + "," + CreatorFixture() + "," + third + "]");
        var emitter = VBulletEmitter.FromJson(EmitterJson(root));
        Check(emitter.Root.Core.Id == "VN001" && emitter.GetCreator("Deep")!.Core.Id == "VN001003002", "路径ID按三位序号逐层拼接");
        Check(ReferenceEquals(emitter.GetCreator("VN001001"), emitter.Root.Children[0]), "ID和Name重名时ID优先");
        Check(ReferenceEquals(emitter.GetCreator("First"), emitter.GetCreator("VN001001")), "名称和ID查找返回同一个Creator");
        Check(emitter.GetCreator("first") is null && emitter.GetCreator("") is null && emitter.GetCreator("missing") is null,
            "查找区分大小写且未匹配返回null");
        Check(!JsonSerializer.Serialize(emitter.Root.Core).Contains("\"Id\""), "内部ID不参与序列化");
        var isolated = VBulletEmitter.FromJson(EmitterJson(root));
        Check(!ReferenceEquals(emitter.Root, isolated.Root) && !ReferenceEquals(emitter.GetCreator("Deep"), isolated.GetCreator("Deep")), "重复加载的树和索引隔离");
        foreach (string invalid in new[]
        {
            CreatorFixture("same", "[" + CreatorFixture("same") + "]"),
            CreatorFixture(children: "[" + string.Join(",", Enumerable.Repeat(CreatorFixture(), 1000)) + "]")
        })
        {
            try { VBulletEmitter.FromJson(EmitterJson(invalid)); Check(false, "重复Name或超长子序号须拒绝"); }
            catch (JsonException) { _checks++; }
        }
        Check(VBulletEmitter.FromJson(EmitterJson(CreatorFixture(" ", "[" + CreatorFixture("") + "," + CreatorFixture() + "]"))).Root.Children.Count == 2,
            "未命名节点允许重复");

        // 树前序决定跨对象派发顺序，同一对象仍按到期时间排序。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        var order = new List<string>();
        emitter.Root.Members[0].Timeline!.At(10, () => order.Add("root10"));
        emitter.Root.Members[0].Timeline!.At(5, () => order.Add("root5"));
        emitter.GetCreator("First")!.Members[0].Timeline!.At(1, () => order.Add("child1"));
        battle.StepFixed(Vector2.Zero, false);
        Check(order.SequenceEqual(new[] { "root5", "root10", "child1" }), "局部按时刻，跨对象按树顺序派发");
        Check(emitter.GetCreator("Deep")!.Members[0].Age == 1.0 / 60, "零龄链不按深度延迟出生");
        battle.Bullets.Clear();

        // 子弹根可有子节点；实际父子弹死亡不能误删后代子弹。
        string grandchild = CreatorFixture("Kept", "[" + CreatorFixture("Future").Replace("\"StartMs\": 0", "\"StartMs\": 100") + "]", true);
        var bulletParent = VBulletEmitter.FromJson(EmitterJson(CreatorFixture("ParentBullet", "[" + CreatorFixture("Pure", "[" + grandchild + "]") + "]", true)));
        bulletParent.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VBullet parent = (VBullet)bulletParent.Root.Members[0];
        VBullet kept = (VBullet)bulletParent.GetCreator("Kept")!.Members[0];
        battle.Bullets.Release(parent);
        Check(bulletParent.Nodes.Count == 0 && kept.IsAlive && !kept.CanGenerate && kept.ParentVNode is null,
            "子弹父节点释放纯后代并取消后代生成，已发子弹继续存活");
        VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
        Check(bulletParent.Nodes.Count == 0 && kept.Age > 0, "被取消的未来生成不会恢复");
        battle.Bullets.Clear();

        // 步末出生仅执行0ms动作，下步开始运动；树成员不会被Manager重复推进。
        string birth = CreatorFixture("New", bullet: true).Replace("\"StartMs\": 0", "\"StartMs\": 1")
            .Replace("\"Children\":", "\"MemberTimeline\": [{\"StartMs\":0,\"Set\":{\"Speed\":120}}],\"Children\":");
        var newborn = VBulletEmitter.FromJson(EmitterJson(birth));
        newborn.Start(battle.Boss, battle.Bullets);
        battle.StepFixed(Vector2.Zero, false);
        VBullet actual = newborn.Bullets.Single();
        Check(actual.Age == 0 && actual.WorldPosition == Vector2.Zero && actual.Speed == 120, "出生步只执行零龄动作，无提前运动");
        battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(actual.Age - 1.0 / 60) < 1e-9 && actual.WorldPosition.X == 2, "下一步仅运动一次");
        battle.Bullets.Clear();

        // 重复触发创建新批次；任意中间成员死亡不改变出生索引。
        string repeat = CreatorFixture("Batches", bullet: true).Replace("\"Type\": \"VBullet\"", "\"Type\": \"VBullet\",\"Amount\":3")
            .Replace("\"StartMs\": 0", "\"AtMs\": [0,100]");
        var batches = VBulletEmitter.FromJson(EmitterJson(repeat));
        batches.Start(battle.Boss, battle.Bullets);
        VerificationClock.EmitterSeconds(battle, 0.1, batches);
        Check(batches.Root.Batches.Count == 2 && batches.Root.Batches.All(batch => batch.Count == 3), "每次触发增加独立批次");
        var firstBatch = batches.Root.Batches[0];
        VNode thirdMember = firstBatch[2];
        battle.Bullets.Release((VBullet)firstBatch[1]);
        Check(firstBatch.Count == 2 && ReferenceEquals(firstBatch[1], thirdMember) && thirdMember.BirthIndex == 2, "中间成员删除保序且出生索引稳定");
        batches.Root.SetSpeed(90);
        Check(batches.Root.Members.All(node => node.Speed == 90), "批量操作覆盖所有存活批次");
        batches.Stop();
        battle.StepFixed(Vector2.Zero, false);
        Check(thirdMember.WorldPosition.X > 0, "停止树仍推进遗留子弹");
        battle.Bullets.Clear();
        Check(batches.Root.Batches.Count == 0 && batches.GetCreator("VN001") is null, "清场回收批次和索引");

        // 派发中清场不能继续执行其他本地动作或出生链。
        var cancel = VBulletEmitter.FromJson(EmitterJson(CreatorFixture(bullet: true)));
        cancel.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        int calls = 0;
        cancel.Bullets[0].Timeline!.At(1, () => battle.Bullets.Clear());
        cancel.Bullets[0].Timeline!.At(1, () => calls++);
        battle.StepFixed(Vector2.Zero, false);
        Check(calls == 0 && battle.Bullets.ActiveCount == 0, "局部派发中清场立即取消后续动作");
        world.Free();

        // 树内玩家弹碰撞可在运动遍历中切阶段，追加的Emitter不能当步提前计龄。
        var switching = CreateBattle(out var switchingWorld);
        switching.Player.Attack.Stop();
        string attack = EmitterJson(CreatorFixture(bullet: true), reference: "\"Boss\"")
            .Replace("\"Enemy\"", "\"Player\"").Replace("\"Damage\": 3", "\"Damage\": 100");
        var attackEmitter = VBulletEmitter.FromJson(attack);
        attackEmitter.Start(switching.Boss, switching.Bullets);
        switching.StepFixed(Vector2.Zero, false);
        Check(switching.Boss.CurrentPhase is B01_Phase02, "树内子弹碰撞期间允许切换阶段并追加运行树");
        Check(switching.Boss.CurrentPhase!.Emitters.All(item => item.Timeline!.ElapsedUnits == 0), "运动阶段新建Emitter从零龄开始");
        switchingWorld.Free();
    }
}
