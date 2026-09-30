using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>世界目标位移、路径坐标系与批次隔离的定向验证。</summary>
public partial class BattleVerification
{
    /// <summary>验证目标位移边界、共用动作随机顺序和两种函数坐标系。</summary>
    private void VerifyPathQueues()
    {
        // 3:4方向便于使用独立整数期望，距离可超过目标或为负。
        Vector2 source = new(3, 4), target = new(6, 8);
        foreach (var item in new[] { (2.5, new Vector2(4.5f, 6)), (10.0, new Vector2(9, 12)), (-5.0, Vector2.Zero), (0.0, source) })
            Check(VMath.TargetMove(source, target, item.Item1).DistanceTo(item.Item2) < 0.0001, "TargetMove有符号距离与越过目标");
        Check(VMath.TargetMove(source, source, 5) == new Vector2(8, 4), "重合目标按0弧度向右移动");
        foreach (var item in new[]
        {
            (new Vector2(float.NaN, 0), target, 1.0), (source, new Vector2(0, float.PositiveInfinity), 0.0),
            (source, target, double.NaN), (source, target, double.PositiveInfinity), (source, target, double.MaxValue)
        })
        {
            try { VMath.TargetMove(item.Item1, item.Item2, item.Item3); Check(false, "非法目标移动必须失败"); }
            catch (Exception error) when (error is ArgumentException or OverflowException) { Check(true, "非法数值或坐标溢出被拒绝"); }
        }
        // 三类队列连续执行，TarMove目标不叠加世界起点。
        var battle = CreateBattle(out var world);
        const string mixed = """
            {"PathMode":"XY","StartMoveQueue":[{"Type":"XYMove","X":100,"Y":100},
              {"Type":"PMove","Angle":"PI/2","Dist":20},{"Type":"TarMove","X":130,"Y":160,"Dist":50}],
             "EndMoveQueue":[{"Type":"XYMove","X":10},{"Type":"PMove","Dist":10},
              {"Type":"TarMove","X":180,"Y":200,"Dist":50}]}
            """;
        var emitter = StartSpawnFixture(battle, PathFixture(mixed, 2));
        Check(emitter.Nodes[0].WorldPosition.DistanceTo(new Vector2(130, 160)) < 0.001
            && emitter.Nodes[1].WorldPosition.DistanceTo(new Vector2(180, 200)) < 0.001, "起终点混合队列按世界目标顺序移动");
        emitter.Stop();

        // XY零宽度不抽样，PMove仍先角后距，TarMove新增顺序为X、Y、Dist。
        const string randomMoves = """
            {"Core":{"Type":"VNode","Amount":2},"BaseAttributes":[{"RefMoveQueue":[
              {"Type":"XYMove","X":100,"Y":100},{"Type":"PMove","Dist":10},
              {"Type":"TarMove","X":150,"Y":160,"Dist":30}]}],
             "AddAttributes":{"RefMoveQueue":[{"Type":"XYMove"},{"Type":"PMove"},{"Type":"TarMove","X":10,"Y":20,"Dist":5}]},
             "RandDiffAttributes":{
               "Batch":{"RefMoveQueue":[{"Type":"XYMove"},{"Type":"PMove","Angle":0.4},{"Type":"TarMove","X":8,"Y":10,"Dist":4}]},
               "Member":{"RefMoveQueue":[{"Type":"XYMove"},{"Type":"PMove","Angle":0.4},{"Type":"TarMove","X":8,"Y":10,"Dist":4}]}},
             "Timeline":[{"StartMs":0}]}
            """;
        VMath.setRandomSeed(481);
        // 独立逐字段抽样并用向量归一化计算期望，不调用生产目标位移函数。
        var batchRandom = new[] { VMath.getRandomDouble(-0.2, 0.2), VMath.getRandomDouble(-4, 4), VMath.getRandomDouble(-5, 5), VMath.getRandomDouble(-2, 2) };
        var expected = new Vector2[2];
        for (int index = 0; index < expected.Length; index++)
        {
            var memberRandom = new[] { VMath.getRandomDouble(-0.2, 0.2), VMath.getRandomDouble(-4, 4), VMath.getRandomDouble(-5, 5), VMath.getRandomDouble(-2, 2) };
            double angle = batchRandom[0] + memberRandom[0];
            Vector2 start = new((float)(100 + 10 * Math.Cos(angle)), (float)(100 + 10 * Math.Sin(angle)));
            Vector2 aim = new((float)(150 + 10 * index + batchRandom[1] + memberRandom[1]), (float)(160 + 20 * index + batchRandom[2] + memberRandom[2]));
            expected[index] = start + (aim - start).Normalized() * (float)(30 + 5 * index + batchRandom[3] + memberRandom[3]);
        }
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(481);
        emitter = StartSpawnFixture(battle, randomMoves);
        for (int index = 0; index < expected.Length; index++)
            Check(emitter.Nodes[index].WorldPosition.DistanceTo(expected[index]) < 0.001, "共用TarMove增量、Batch和Member随机逐字段求值");
        Check(VMath.getRandomDouble(0, 1) == next, "混合动作保持既有抽样顺序且零宽不消耗随机");
        emitter.Stop();

        // 世界端点(100,100)->(100,200)，Relative正Y向左；Absolute直接使用世界X/Y。
        foreach (string axis in new[] { "Absolute", "Relative" })
        {
            string segment = $$"""
                {"PathMode":"Function","AxisMode":"{{axis}}",
                 "StartMoveQueue":[{"Type":"XYMove","X":100,"Y":100}],"EndMoveQueue":[{"Type":"XYMove","Y":100}],
                 "X":"{{(axis == "Relative" ? "L*t" : "SX+(EX-SX)*t+20*sin(PI*t)")}}",
                 "Y":"{{(axis == "Relative" ? "20*sin(PI*t)" : "SY+(EY-SY)*t")}}"}
                """;
            emitter = StartSpawnFixture(battle, PathFixture(segment, 3));
            Check(emitter.Nodes[0].WorldPosition == new Vector2(100, 100) && emitter.Nodes[2].WorldPosition == new Vector2(100, 200),
                "两种轴模式匹配位移队列确定的世界端点");
            Check(emitter.Nodes[1].WorldPosition.DistanceTo(new Vector2(axis == "Relative" ? 80 : 120, 150)) < 0.002,
                "Relative顺时针正交轴与Absolute世界坐标");
            emitter.Stop();
        }
        // 默认Absolute必须直接返回世界位置；非零父点不能再加到表达式结果上。
        emitter = StartSpawnFixture(battle, """
            {"Core":{"Type":"VNode"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":300,"Y":250}]}],
             "Timeline":[{"StartMs":0}],"Children":[{"Core":{"Type":"VPath","Amount":3},
               "PathQueue":[{"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":100}],
                 "TMin":2,"TMax":4,"X":"SX+(EX-SX)*(t-2)/2","Y":"SY+(EY-SY)*(t-2)/2"}],
               "Timeline":[{"StartMs":0}]}]}
            """);
        Check(emitter.Root.Children[0].Members.Select(node => node.WorldPosition).SequenceEqual(new[]
            { new Vector2(300, 250), new Vector2(350, 250), new Vector2(400, 250) }),
            "默认Absolute使用世界端点和实际参数区间，不重复叠加父位置");
        emitter.Stop();
        // 控制点相对本段参考点，而非经StartMoveQueue移动后的起点。
        emitter = StartSpawnFixture(battle, PathFixture("""
            {"PathMode":"Bezier","StartMoveQueue":[{"Type":"XYMove","X":100}],
             "EndMoveQueue":[{"Type":"XYMove","X":100}],"ControlPoints":[{"X":150,"Y":100}]}
            """, 3));
        Check(emitter.Nodes[1].WorldPosition.DistanceTo(new Vector2(150, 50)) < 0.002, "Bezier控制点保留段参考点的屏幕坐标语义");
        emitter.Stop();
        // 路径上下文变量可同时读取；实际t不被归一到默认区间。
        var context = new VPathFunctionContext { T = 2, L = 100, SX = 10, SY = 20, EX = 70, EY = 100 };
        Check(VPathExpression.CompilePath("t+L+SX+SY+EX+EY")(context) == 302, "六个路径变量均从当前世界上下文读取");
        world.Free();
    }

    /// <summary>验证动态几何失败原子性及配置边界，失败不遗留成员或延迟动作。</summary>
    private void VerifyPathRuntimeValidation()
    {
        // 运行时才能确定的端点、长度和定义域错误，必须在批次出生前报告。
        var battle = CreateBattle(out var world);
        var emitter = StartSpawnFixture(battle, PathFixture(PathLine));
        int initialNodes = emitter.Nodes.Count;
        foreach (string segment in new[]
        {
            """{"PathMode":"XY"}""",
            """{"PathMode":"XY","StartMoveQueue":[],"EndMoveQueue":[]}""",
            PathLine + """,{"PathMode":"XY","StartMoveQueue":[{"Type":"XYMove","X":0.002}],"EndMoveQueue":[{"Type":"XYMove","X":100}]}""",
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":"1e100"}]}""",
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"TarMove","X":"1e100","Dist":1}]}""",
            """{"PathMode":"Function","AxisMode":"Relative","X":"0","Y":"0"}"""
        }) RejectRuntimePath(battle, emitter, PathFixture(segment), initialNodes);
        foreach (string expression in new[]
        {
            "sqrt(-1)", "pow(-1,0.5)", "1/0", "100*t+t*(1-t)/(t-0.5)", "100*t+1e40*t*(1-t)", "100*t+1"
        })
        {
            string segment = $$"""
                {"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":100}],"X":"{{expression}}","Y":"0"}
                """;
            RejectRuntimePath(battle, emitter, PathFixture(segment), initialNodes);
        }
        // 非空的第一段也不先生成：第二段错误须撤销整批出生计划。
        RejectRuntimePath(battle, emitter, PathFixture(PathLine + """,{"PathMode":"XY"}"""), initialNodes, 1);
        VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
        Check(emitter.Nodes.Count == initialNodes, "几何失败后没有延迟成员漏出");
        emitter.Stop();
        // 后段省略StartMoveQueue继承上一终点；空End可用于首末重合但长度非零的Bezier。
        emitter = StartSpawnFixture(battle, PathFixture("""
            {"PathMode":"Bezier","StartMoveQueue":[],"EndMoveQueue":[],"ControlPoints":[{"X":100,"Y":0}]}
            """, 3));
        Check(emitter.Nodes[0].WorldPosition == Vector2.Zero && emitter.Nodes[1].WorldPosition == new Vector2(50, 0)
            && emitter.Nodes[2].WorldPosition == Vector2.Zero, "空队列的闭合Bezier仍按实际曲线长度采样");
        emitter.Stop();
        world.Free();
    }

    /// <summary>直接触发无效几何批次并验证不会产生部分成员或消费随机。</summary>
    /// <param name="battle">当前隔离战斗。</param>
    /// <param name="emitter">已经启动的容器。</param>
    /// <param name="json">含运行时错误的路径定义。</param>
    /// <param name="expectedNodes">失败前容器中的节点数。</param>
    /// <param name="segmentIndex">预期失败的零基段索引，默认-1表示只检查段位置存在。</param>
    private void RejectRuntimePath(BattleManager battle, VBulletEmitter emitter, string json, int expectedNodes, int segmentIndex = -1)
    {
        // 加载只编译表达式；添加延迟以检查失败不会先登记后续出生。
        var data = JsonNode.Parse(json)!;
        data["AddAttributes"] = JsonNode.Parse("""{"SpawnDelayMs":50}""");
        var creator = VNodeCreator.FromJson(data.ToJsonString());
        int actions = battle.Timers.TimelineActionCount;
        VMath.setRandomSeed(491);
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(491);
        try { creator.Emit(emitter, battle.Bullets, null, null, Vector2.Zero); Check(false, "动态无效路径必须拒绝生成"); }
        catch (JsonException error)
        {
            Check(error.Message.Contains("VN001") && error.Message.Contains(segmentIndex < 0 ? "PathQueue[" : $"PathQueue[{segmentIndex}]"),
                "动态路径错误包含Creator及路径段");
        }
        Check(emitter.Nodes.Count == expectedNodes && creator.Batches.Count == 0 && battle.Timers.TimelineActionCount == actions,
            "整批几何校验失败不留下节点或延迟出生");
        Check(VMath.getRandomDouble(0, 1) == next, "失败的路径采样不消耗随机");
    }

    /// <summary>验证世界瞄准按触发位置固定，多个父节点、重叠批次和延迟成员分别使用独立缓存。</summary>
    private void VerifyDynamicPathBatches()
    {
        // 两个父点沿X移动，每个在0和50ms触发，路径节点在0、100、200ms出生。
        foreach (string mode in new[] { "Follow", "Snapshot" })
        {
            var battle = CreateBattle(out var world);
            string json = $$"""
                {"Core":{"Type":"VNode","Amount":2,"LifeTimeMs":1000},
                 "BaseAttributes":[{"Speed":60,"RefMoveQueue":[{"Type":"XYMove","X":100,"Y":100}]}],
                 "AddAttributes":{"RefMoveQueue":[{"Type":"XYMove","X":100}]},
                 "Timeline":[{"StartMs":0}],
                 "Children":[{"Core":{"Type":"VPath","Name":"Dynamic","Amount":3,"CreatePositionMode":"{{mode}}","LifeTimeMs":500},
                   "PathQueue":[{"PathMode":"Function","AxisMode":"Relative",
                     "EndMoveQueue":[{"Type":"TarMove","X":100,"Y":200,"Dist":100}],
                     "X":"L*t","Y":"20*sin(PI*t)"}],
                   "AddAttributes":{"SpawnDelayMs":100},"Timeline":[{"AtMs":[0,50]}]}]}
                """;
            var emitter = StartSpawnFixture(battle, json, "ClearBullets");
            var path = emitter.GetCreator("Dynamic")!;
            VerificationClock.BattleSeconds(battle, 0.25, Vector2.Zero, false);
            Check(path.Batches.Count == 4 && path.Members.Count == 12, "重叠路径批次各自保留全部延迟成员");
            for (int batchIndex = 0; batchIndex < 4; batchIndex++)
            {
                // 独立几何参考：触发点和固定世界目标决定方向，Follow仅补父平移。
                float atTrigger = batchIndex / 2 * 3;
                Vector2 start = new(100 + batchIndex % 2 * 100 + atTrigger, 100);
                Vector2 direction = (new Vector2(100, 200) - start).Normalized();
                Vector2 translation = mode == "Follow" ? new Vector2(15 - atTrigger, 0) : Vector2.Zero;
                Vector2 middle = start + direction * 50 + new Vector2(-direction.Y, direction.X) * 20;
                Check(path.Batches[batchIndex][0].WorldPosition.DistanceTo(start + translation) < 0.002
                    && path.Batches[batchIndex][1].WorldPosition.DistanceTo(middle + translation) < 0.002
                    && path.Batches[batchIndex][2].WorldPosition.DistanceTo(start + direction * 100 + translation) < 0.002,
                    "世界瞄准和形状在各批触发时固定，Follow只继承平移");
            }
            emitter.Stop();
            VerificationClock.BattleSeconds(battle, 0.3, Vector2.Zero, false);
            Check(path.Members.Count == 0 && path.Batches.Count == 0, "清场释放动态路径及批次缓存引用");
            world.Free();
        }
    }

    /// <summary>从相同完整世界和随机种子启动Boss2配置，验证动态端点、子弹与后续随机序列重现。</summary>
    private void VerifyDynamicPathReplay()
    {
        // 按固定步记录真实示例的全部Creator成员，覆盖三个重叠的随机路径批次。
        var runs = new System.Collections.Generic.List<(Vector2 Position, double Age, int Index)>[2];
        var next = new double[2];
        for (int run = 0; run < 2; run++)
        {
            var battle = CreateBattle(out var world);
            battle.Boss.Stop();
            battle.Player.Attack.Stop();
            VMath.setRandomSeed(719);
            var emitter = VBulletEmitter.Load("res://Data/Emitters/B02P01_Emitter01.json");
            emitter.Start(battle.Boss, battle.Bullets);
            runs[run] = new();
            for (int step = 0; step < 180; step++)
            {
                battle.StepFixed(Vector2.Zero, false);
                foreach (var node in emitter.Root.EnumerateMembers(true))
                    runs[run].Add((node.WorldPosition, node.Age, node.BirthIndex));
            }
            Check(emitter.Root.Children[0].Batches.Count == 3, "重现测试覆盖三批动态随机路径");
            next[run] = VMath.getRandomDouble(0, 1);
            world.Free();
        }
        Check(runs[0].SequenceEqual(runs[1]) && next[0] == next[1], "相同完整状态下动态路径和后续随机序列逐步一致");
    }
}
