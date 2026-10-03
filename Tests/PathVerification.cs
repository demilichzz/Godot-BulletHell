using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>分段路径、参数表达式、弧长排列及现有Creator集成的定向验证。</summary>
public partial class BattleVerification
{
    // 最小直线段供模式替换和错误校验复用，单位为逻辑像素。
    private const string PathLine = """
        {"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":100}]}
        """;

    /// <summary>构造单次生成的最小路径定义。</summary>
    /// <param name="segments">不含外层数组的路径段JSON。</param>
    /// <param name="amount">总节点数，默认5。</param>
    /// <returns>包含零龄生成规则的Creator JSON。</returns>
    private static string PathFixture(string segments, int amount = 5) => $$"""
        {"Core":{"Type":"VPath","Amount":{{amount}}},"PathQueue":[{{segments}}],"Timeline":[{"StartMs":0}]}
        """;

    /// <summary>运行路径定向验证，不触发完整回归。</summary>
    private void VerifyPaths()
    {
        VerifyAimPlayerPaths();
        VerifyPathGeometry();
        VerifyPathCurves();
        VerifyPathValidation();
        VerifyPathQueues();
        VerifyPathRuntimeValidation();
        VerifyDynamicPathBatches();
        VerifyPathIntegration();
        VerifyPathReplay();
        VerifyDynamicPathReplay();
    }

    /// <summary>验证直线、极坐标、多段分配、连接点及位置动作。</summary>
    private void VerifyPathGeometry()
    {
        // 使用同一个隔离世界，每个用例结束后注销本Emitter。
        var battle = CreateBattle(out var world);
        var emitter = StartSpawnFixture(battle, PathFixture(PathLine));
        Check(emitter.Root is VPathCreator && emitter.Root.Core.Type == "VPath", "VPath工厂返回新子类");
        Check(emitter.Root.BaseAttributes.Count == 1 && emitter.Root.BaseAttributes[0].Speed == 0 && emitter.Root.TotalAmount == 5,
            "省略基础项时单项零速且Amount为总数量");
        var expected = new float[] { 0, 25, 50, 75, 100 };
        Check(emitter.Nodes.Select(node => node.WorldPosition.X).SequenceEqual(expected), "直线含两端且均匀排列");
        Check(emitter.Nodes.Select(node => node.BirthIndex).SequenceEqual(Enumerable.Range(0, 5)), "路径出生索引不受位置影响");
        Check(emitter.Root.Batches.Count == 1 && battle.Bullets.ActiveCount == 0, "纯路径节点不占子弹容量");
        emitter.Stop();
        foreach (string segment in new[]
        {
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"PMove","Angle":0,"Dist":100}]}""",
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"PMove","Angle":"PI","Dist":-100}]}"""
        })
        {
            emitter = StartSpawnFixture(battle, PathFixture(segment));
            for (int index = 0; index < expected.Length; index++)
                Check(emitter.Nodes[index].WorldPosition.DistanceTo(new Vector2(expected[index], 0)) < 0.0001, "Polar和负距离与XY等价");
            emitter.Stop();
        }
        // 两段长度100、300；总间隔6，保底各1，剩余4按1:3分配为2、4。
        string unequal = PathLine + "," + """{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":300}]}""";
        emitter = StartSpawnFixture(battle, PathFixture(unequal, 7));
        Check(emitter.Nodes.Select(node => node.WorldPosition.X).SequenceEqual(new float[] { 0, 50, 100, 175, 250, 325, 400 }),
            "段长比例分配间隔且连接点只生成一次");
        emitter.Stop();
        emitter = StartSpawnFixture(battle, PathFixture(unequal, 3));
        Check(emitter.Nodes.Select(node => node.WorldPosition.X).SequenceEqual(new float[] { 0, 100, 400 }), "最小Amount保留全部端点");
        emitter.Stop();
        // 相同余数时前段优先获得额外间隔。
        string equal = unequal.Replace("300", "100");
        emitter = StartSpawnFixture(battle, PathFixture(equal, 4));
        Check(emitter.Nodes.Select(node => node.WorldPosition.X).SequenceEqual(new float[] { 0, 50, 100, 200 }), "相同余数按段声明顺序分配");
        emitter.Stop();
        emitter = StartSpawnFixture(battle, PathFixture(PathLine + "," +
            """{"PathMode":"XY","StartMoveQueue":[{"Type":"XYMove","X":0.0005}],"EndMoveQueue":[{"Type":"XYMove","X":100}]}""", 4));
        Check(emitter.Nodes[2].WorldPosition.X == 100, "容差内连接点采用前段终点");
        emitter.Stop();
        // 显式闭合的首末位置相同，但保留各自出生索引。
        emitter = StartSpawnFixture(battle, PathFixture(unequal.Replace("300", "-100"), 3));
        Check(emitter.Nodes.Count == 3 && emitter.Nodes[0].WorldPosition == emitter.Nodes[2].WorldPosition
            && emitter.Nodes[0].BirthIndex != emitter.Nodes[2].BirthIndex, "闭合路径不合并首末出生索引");
        emitter.Stop();
        // 位置动作在路径偏移之后叠加，不改写缓存的原路径。
        var modified = JsonNode.Parse(PathFixture(PathLine))!;
        modified["BaseAttributes"] = JsonNode.Parse("""[{"RefMoveQueue":[{"Type":"XYMove","X":10,"Y":20}]}]""");
        modified["AddAttributes"] = JsonNode.Parse("""{"RefMoveQueue":[{"Type":"XYMove","X":1,"Y":0}]}""");
        emitter = StartSpawnFixture(battle, modified.ToJsonString());
        Check(emitter.Nodes[4].WorldPosition == new Vector2(114, 20), "路径位置与现有基础位移和逐轮增量叠加");
        emitter.Stop();
        world.Free();
    }

    /// <summary>以独立Bernstein公式和高密度积分验证曲线采样。</summary>
    private void VerifyPathCurves()
    {
        // 贝塞尔横坐标等差，因此可以从生成点X独立恢复参数t。
        var battle = CreateBattle(out var world);
        for (int degree = 2; degree <= 4; degree++)
        {
            var points = new (double X, double Y)[degree + 1];
            for (int index = 0; index <= degree; index++)
                points[index] = (200.0 * index / degree, index == 0 || index == degree ? 0 : index % 2 == 0 ? -120 : 100);
            string controls = string.Join(",", points.Skip(1).Take(degree - 1).Select(point => JsonSerializer.Serialize(new { point.X, point.Y })));
            string segment = $$"""
                {"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":200}],"ControlPoints":[{{controls}}]}
                """;
            var emitter = StartSpawnFixture(battle, PathFixture(segment, 9));
            VerifyIndependentArcSpacing(emitter.Nodes.Select(node => node.WorldPosition).ToArray(), t => BernsteinPoint(points, t), 200);
            Check(((VPathCreator)emitter.Root).PathQueue[0].ControlPoints.Count == degree - 1, "控制点数量决定贝塞尔阶数");
            emitter.Stop();
        }
        // 正弦方程通过独立解析公式进行积分，Samples只影响数值精度。
        const string sine = """
            {"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":200}],"X":"200*t","Y":"60*sin(TAU*t)","Samples":4096}
            """;
        var functionEmitter = StartSpawnFixture(battle, PathFixture(sine, 13));
        VerifyIndependentArcSpacing(functionEmitter.Nodes.Select(node => node.WorldPosition).ToArray(), t => (200 * t, 60 * Math.Sin(Math.Tau * t)), 200);
        Check(functionEmitter.Nodes[^1].WorldPosition == new Vector2(200, 0), "三角函数端点吸附到声明值");
        functionEmitter.Stop();
        // 非默认参数范围形成半圆，期望点由等角度解析计算。
        const string arc = """
            {"PathMode":"Function","StartMoveQueue":[{"Type":"XYMove","Y":-100}],"EndMoveQueue":[{"Type":"XYMove","Y":200}],
             "X":"100*cos(t)","Y":"100*sin(t)","TMin":"-PI/2","TMax":"PI/2"}
            """;
        functionEmitter = StartSpawnFixture(battle, PathFixture(arc));
        for (int index = 0; index < 5; index++)
        {
            double angle = -Math.PI / 2 + index * Math.PI / 4;
            Check(functionEmitter.Nodes[index].WorldPosition.DistanceTo(new Vector2((float)(100 * Math.Cos(angle)), (float)(100 * Math.Sin(angle)))) < 0.002,
                "自定义参数范围的半圆近似弧长等距");
        }
        functionEmitter.Stop();
        // 零速度采样区间不得导致弧长反查除零。
        const string plateau = """
            {"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":100}],
             "X":"100*(t-0.5+abs(t-0.5))","Y":"0"}
            """;
        functionEmitter = StartSpawnFixture(battle, PathFixture(plateau));
        Check(functionEmitter.Nodes.Select(node => node.WorldPosition.X).SequenceEqual(new float[] { 0, 25, 50, 75, 100 }), "弧长表平台正确跳过");
        functionEmitter.Stop();
        // 同一队列混用直线、贝塞尔和参数方程。
        string mixed = PathLine + "," + """
            {"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":100}],"ControlPoints":[{"X":50,"Y":100}]},
            {"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":100}],"X":"200+100*t","Y":"20*sin(TAU*t)"}
            """;
        functionEmitter = StartSpawnFixture(battle, PathFixture(mixed, 12));
        Check(functionEmitter.Nodes.Count == 12 && functionEmitter.Nodes.Count(node => node.WorldPosition == new Vector2(100, 0)) == 1
            && functionEmitter.Nodes.Count(node => node.WorldPosition == new Vector2(200, 0)) == 1, "混合曲线段保留所有连接点且总数固定");
        functionEmitter.Stop();
        world.Free();
    }

    /// <summary>通过Bernstein基函数计算独立贝塞尔参考值。</summary>
    /// <param name="points">包含首末端点的控制多边形，逻辑像素。</param>
    /// <param name="t">[0,1]范围内的参数。</param>
    /// <returns>独立公式求得的双精度坐标。</returns>
    private static (double X, double Y) BernsteinPoint((double X, double Y)[] points, double t)
    {
        // 用组合系数显式求和，不复用生产代码的递归插值。
        double x = 0, y = 0, combination = 1;
        int degree = points.Length - 1;
        for (int index = 0; index <= degree; index++)
        {
            double weight = combination * Math.Pow(t, index) * Math.Pow(1 - t, degree - index);
            x += weight * points[index].X;
            y += weight * points[index].Y;
            combination *= (double)(degree - index) / (index + 1);
        }
        return (x, y);
    }

    /// <summary>独立积分各生成点之间的曲线，检查实际弧长与曲线归属。</summary>
    /// <param name="points">实际生成坐标，横坐标随参数线性增加。</param>
    /// <param name="evaluate">独立参考曲线公式。</param>
    /// <param name="width">整段横向跨度，逻辑像素。</param>
    private void VerifyIndependentArcSpacing(Vector2[] points, Func<double, (double X, double Y)> evaluate, double width)
    {
        // 每对节点之间独立细分4096次，不使用生产弧长表。
        var distances = new List<double>();
        for (int index = 1; index < points.Length; index++)
        {
            double from = points[index - 1].X / width, to = points[index].X / width;
            var previous = evaluate(from);
            double length = 0;
            for (int sample = 1; sample <= 4096; sample++)
            {
                var current = evaluate(from + (to - from) * sample / 4096);
                double dx = current.X - previous.X, dy = current.Y - previous.Y;
                length += Math.Sqrt(dx * dx + dy * dy);
                previous = current;
            }
            distances.Add(length);
            Check(Math.Abs(points[index].Y - previous.Y) < 0.001, "采样点位于参考曲线上");
        }
        Check(distances.Max() - distances.Min() < 0.01, "独立高密度积分验证段内弧长间隔误差小于0.01像素");
    }

    /// <summary>验证表达式白名单及严格JSON字段和边界校验。</summary>
    private void VerifyPathValidation()
    {
        // 使用可变JSON副本逐项破坏有效路径，各例互不影响。
        var invalid = new Action<JsonNode>[]
        {
            json => json["Core"]!["Amount"] = 1,
            json => json["Core"]!["Amount"] = 0,
            json => json["PathQueue"] = new JsonArray(),
            json => json["PathQueue"] = null,
            json => json.AsObject().Remove("PathQueue"),
            json => json["PathQueue"]![0] = null,
            json => json["BaseAttributes"] = new JsonArray(),
            json => json["BaseAttributes"] = null,
            json => json["BaseAttributes"] = JsonNode.Parse("[{},{}]"),
            json => json["Display"] = new JsonObject(),
            json => json["Core"]!["Radius"] = 1,
            json => json["PathQueue"]![0]!["PathMode"] = "Unknown",
            json => json["PathQueue"]![0]!["Angle"] = 1,
            json => json["PathQueue"]![0]!["Start"] = new JsonObject(),
            json => json["PathQueue"]![0]!["End"] = new JsonObject(),
            json => json["PathQueue"]![0]!["StartMoveQueue"] = null,
            json => json["PathQueue"]![0]!["EndMoveQueue"] = null,
            json => json["PathQueue"]![0]!["AxisMode"] = "Absolute",
            json => json["PathQueue"]![0]!["EndMoveQueue"]![0]!["X"] = "1/0",
            json => json["PathQueue"]![0]!["EndMoveQueue"]![0]!["X"] = null,
            json => json["PathQueue"]![0]!["EndMoveQueue"]![0]!["Z"] = 0,
            json => json["PathQueue"]![0]!["EndMoveQueue"]![0]!["Angle"] = 0,
            json => json["PathQueue"]![0]!["EndMoveQueue"]![0]!["RandDiffAttributes"] = new JsonObject(),
            json => json["Timeline"] = JsonNode.Parse("[{\"StartMs\":0,\"IntervalMs\":100}]")
        };
        foreach (var change in invalid)
        {
            var json = JsonNode.Parse(PathFixture(PathLine))!;
            change(json);
            RejectPath(json.ToJsonString());
        }
        foreach (string segment in new[]
        {
            """{"PathMode":"Polar","Start":{"X":0,"Y":0},"Angle":0}""",
            """{"PathMode":"Polar","Start":{"X":0,"Y":0},"Angle":0,"Dist":0}""",
            """{"PathMode":"Polar","Start":{"X":0,"Y":0},"Angle":0,"Dist":1,"End":{"X":1,"Y":0}}""",
            """{"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":100}],"ControlPoints":[]}""",
            """{"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":100}],"ControlPoints":[null]}""",
            """{"PathMode":"Bezier","AxisMode":"Relative","ControlPoints":[{"X":50,"Y":50}]}""",
            """{"PathMode":"XY","StartMoveQueue":[{"Type":"TarMove","Angle":0}]}""",
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"TarMove","Dist":null}]}""",
            """{"PathMode":"XY","EndMoveQueue":[{"Type":"TarMove","X":"SX"}]}"""

        }) RejectPath(PathFixture(segment));
        // 方程错误覆盖语法、定义域、区间、端点和采样中的溢出。
        const string function = """
            {"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":100}],"X":"100*t","Y":"0"}
            """;
        foreach (string expression in new[]
        {
            "", "t+", "2t", "t^2", "sin()", "sin(t,t)", "pow(t)", "pow(t,2,3)", "PI()", "random()", "T", "Math.Sin(t)",
            "1e999",
            new string('(', 66) + "t" + new string(')', 66), new string(' ', 1025)
        })
        {
            var json = JsonNode.Parse(PathFixture(function))!;
            json["PathQueue"]![0]!["X"] = expression;
            RejectPath(json.ToJsonString());
        }
        foreach (var change in new Action<JsonNode>[]
        {
            json => json["AxisMode"] = "Unknown",
            json => json["AxisMode"] = null,
            json => json["X"] = null,
            json => json["X"] = 100,
            json => json["TMin"] = 1,
            json => json["TMax"] = -1,
            json => json["TMax"] = null,
            json => json["ControlPoints"] = new JsonArray(),
            json => json["Samples"] = 1,
            json => json["Samples"] = 65537,
            json => json["Samples"] = "1024",
            json => json["Samples"] = 2.5,
            json => json["Samples"] = null
        })
        {
            var json = JsonNode.Parse(PathFixture(function))!;
            change(json["PathQueue"]![0]!);
            RejectPath(json.ToJsonString());
        }
        RejectPath(PathFixture(PathLine.Replace("\"X\":100", "\"X\":100,\"X\":101")));
        RejectPath(PathFixture(PathLine).Replace("\"VPath\"", "\"VNode\""));
        RejectPath(PathFixture(PathLine).Replace("\"VPath\"", "\"VBullet\""));
        // 全部函数、科学计数法、一元运算及结合顺序使用独立常量结果验证。
        Check(Math.Abs(VPathExpression.Compile("sin(PI/2)+cos(0)+tan(0)+sqrt(9)+abs(-2)+pow(t,3)+1e-2") (2) - 15.01) < 1e-12,
            "路径表达式白名单数学函数正确求值");
        Check(VPathExpression.Compile("10-3-2+8/2/2") (0) == 7 && VPathExpression.Compile("-(t+2)*3") (1) == -9,
            "表达式同级结合顺序和一元运算正确");
        Check(VPathExpression.Compile("pow(2,pow(t,2))") (3) == 512, "嵌套双参数函数正确解析");
        foreach (int samples in new[] { 2, 65536 })
        {
            // 采样区间的合法边界均可加载，直线参数方程仍得到正确长度。
            var json = JsonNode.Parse(PathFixture(function))!;
            json["PathQueue"]![0]!["Samples"] = samples;
            Check(VNodeCreator.FromJson(json.ToJsonString()) is VPathCreator, "Samples合法边界可加载");
        }
        // 普通数值字段仍拒绝路径变量和函数，不扩大旧协议。
        RejectPath(PathFixture(PathLine.Replace("\"X\":100", "\"X\":\"sin(0)\"")));
        Check(VNodeCreator.FromJson(PathFixture(function.Replace("\"Y\":\"0\"", "\"Y\":\"0.0005\""))) is VPathCreator,
            "方程端点容差内允许加载");
    }

    /// <summary>确认无效路径在加载阶段失败并保留来源说明。</summary>
    /// <param name="json">预期无效的Creator JSON。</param>
    private void RejectPath(string json)
    {
        try { VNodeCreator.FromJson(json, "path-invalid.json"); Check(false, "无效路径必须拒绝加载"); }
        catch (JsonException error) { Check(error.Message.Contains("path-invalid.json"), "路径错误包含来源名称"); }
    }

    /// <summary>验证根路径、嵌套路径、父引用、延迟批次及取消规则。</summary>
    private void VerifyPathIntegration()
    {
        // 根路径每个实际节点都绑定同一个子Creator，生成独立子弹批次。
        var battle = CreateBattle(out var world);
        var root = JsonNode.Parse(PathFixture(PathLine))!;
        root["Children"] = JsonNode.Parse("[" + CreatorFixture("PathBullets", bullet: true) + "]");
        var emitter = StartSpawnFixture(battle, root.ToJsonString());
        var bullets = emitter.GetCreator("PathBullets")!;
        Check(bullets.Core.Id == "VN001001" && bullets.Batches.Count == 5 && emitter.Bullets.Count == 5, "路径节点逐个触发子Creator独立批次");
        for (int index = 0; index < 5; index++)
            Check(ReferenceEquals(bullets.Members[index].ParentVNode, emitter.Nodes[index])
                && bullets.Members[index].WorldPosition == emitter.Nodes[index].WorldPosition, "子弹绑定具体路径节点并在其位置出生");
        emitter.ReleaseNode(emitter.Root.Members[2]);
        Check(emitter.Root.Members.Select(node => node.BirthIndex).SequenceEqual(new[] { 0, 1, 3, 4 }), "路径成员死亡不重排出生索引");
        Check(bullets.Members[2].IsAlive && bullets.Members[2].ParentVNode is null && !bullets.Members[2].CanGenerate,
            "路径节点结束保留已生子弹并解除生命周期绑定");
        emitter.Stop();
        Check(emitter.Nodes.Count == 0 && emitter.Bullets.Count == 5, "KeepBullets清理路径节点但保留子弹");
        battle.Bullets.Clear();
        world.Free();

        // 移动的两个实际父VNode共享子路径定义，但批次快照和父引用必须各自独立。
        foreach (string mode in new[] { "Follow", "Snapshot" })
        {
            battle = CreateBattle(out world);
            var path = JsonNode.Parse(PathFixture(PathLine, 3))!;
            path["Core"]!["Name"] = "NestedPath";
            path["Core"]!["CreatePositionMode"] = mode;
            path["Core"]!["LifeTimeMs"] = 1000;
            path["AddAttributes"] = JsonNode.Parse("{\"SpawnDelayMs\":50}");
            path["Timeline"] = JsonNode.Parse("[{\"AtMs\":[0,100]}]");
            path["MemberTimeline"] = JsonNode.Parse("[{\"StartMs\":0,\"Set\":{\"Angle\":1}}]");
            path["Children"] = JsonNode.Parse("""
                [{"Core":{"Type":"VBullet","Name":"NestedBullets","CreatePositionMode":"Follow","LifeTimeMs":1000},
                  "Display":{},"BaseAttributes":[{"Speed":0}],"Timeline":[{"StartMs":0}]}]
                """);
            string parent = $$"""
                {"Core":{"Type":"VNode","Amount":2,"LifeTimeMs":120,"CreatePositionMode":"Snapshot"},
                 "BaseAttributes":[{"Speed":60,"RefMoveQueue":[{"Type":"XYMove","X":0,"Y":0}]}],
                 "AddAttributes":{"RefMoveQueue":[{"Type":"XYMove","X":100,"Y":0}]},
                 "Timeline":[{"StartMs":0}],"Children":[{{path.ToJsonString()}}]}
                """;
            emitter = StartSpawnFixture(battle, parent);
            var nested = emitter.GetCreator("NestedPath")!;
            Check(nested is VPathCreator && nested.Batches.Count == 2 && nested.Members.Count == 2, "嵌套路径为每个父实例建立独立批次");
            Check(nested.Members.All(node => node.Angle == 1 && node.Age == 0), "路径成员保留零龄成员动作");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(nested.Members.Count == 4 && nested.Batches.All(batch => batch.Count == 2), "延迟路径节点在各自原批次出生");
            for (int parentIndex = 0; parentIndex < 2; parentIndex++)
            {
                // 50ms时父节点移动3像素，Snapshot仍使用0ms原点。
                var member = nested.Batches[parentIndex][1];
                float expectedX = parentIndex * 100 + 50 + (mode == "Follow" ? 3 : 0);
                Check(member.WorldPosition.DistanceTo(new Vector2(expectedX, 0)) < 0.001 && member.Age == 0,
                    "Follow按出生时父位置取样，Snapshot保留批次原点");
                Check(ReferenceEquals(member.ParentVNode, emitter.Root.Members[parentIndex]), "延迟成员保持具体父引用");
            }
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(nested.Batches.Count == 4 && nested.Members.Count == 8 && emitter.Bullets.Count == 8, "重复触发不合并父实例或批次");
            Check(nested.Batches[2][0].WorldPosition.X == 6 && nested.Batches[3][0].WorldPosition.X == 106,
                "每次重复批次重新读取父参考位置");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(emitter.Nodes.Count == 0 && nested.Batches.Count == 0 && emitter.Bullets.Count == 8,
                "父到期释放全部路径节点及批次，保留已发子弹");
            Check(emitter.Bullets.All(bullet => bullet.ParentVNode is null && !bullet.CanGenerate), "父到期解除Follow子弹参考且取消后代生成");
            VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
            Check(emitter.Bullets.Count == 8 && nested.Members.Count == 0, "父到期取消第二批尚未出生的路径节点");
            world.Free();
        }
        // 根路径通过Boss参考验证两种空间模式及ClearBullets停止。
        foreach (string mode in new[] { "Follow", "Snapshot" })
        {
            battle = CreateBattle(out world);
            battle.Boss.Stop();
            battle.Player.Attack.Stop();
            battle.Boss.GlobalPosition = new Vector2(100, 100);
            root = JsonNode.Parse(PathFixture(PathLine, 3))!;
            root["Core"]!["CreatePositionMode"] = mode;
            root["AddAttributes"] = JsonNode.Parse("{\"SpawnDelayMs\":50}");
            root["Children"] = JsonNode.Parse("[" + CreatorFixture(bullet: true) + "]");
            emitter = VBulletEmitter.FromJson(EmitterJson(root.ToJsonString(), "ClearBullets", "\"Boss\""));
            emitter.Start(battle.Boss, battle.Bullets);
            battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
            battle.Boss.GlobalPosition = new Vector2(200, 100);
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(emitter.Root.Members[1].WorldPosition == new Vector2(mode == "Follow" ? 250 : 150, 100), "根路径的Boss参考与批次快照正确");
            emitter.Stop();
            VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
            Check(emitter.Nodes.Count == 0 && emitter.Bullets.Count == 0, "ClearBullets清空子弹且停止后不再出生延迟节点");
            world.Free();
        }
    }

    /// <summary>验证加载不抽随机、独立配置缓存及完整初始状态下的重复结果。</summary>
    private void VerifyPathReplay()
    {
        // 路径含位移随机与运动随机，以检查与现有字段抽样顺序一致。
        var json = JsonNode.Parse(PathFixture(PathLine))!;
        json["Core"]!["LifeTimeMs"] = 1000;
        json["BaseAttributes"] = JsonNode.Parse("""[{"Speed":10,"RefMoveQueue":[{"Type":"XYMove","X":0,"Y":0}]}]""");
        json["RandDiffAttributes"] = JsonNode.Parse("""
            {"Batch":{"Speed":4,"RefMoveQueue":[{"Type":"XYMove","X":6,"Y":0}]},
             "Member":{"Speed":2,"RefMoveQueue":[{"Type":"XYMove","X":8,"Y":0}]}}
            """);
        json["AddAttributes"] = JsonNode.Parse("{\"SpawnDelayMs\":10}");
        json["Children"] = JsonNode.Parse("[" + CreatorFixture(bullet: true) + "]");
        string definition = json.ToJsonString();
        VMath.setRandomSeed(912);
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(912);
        var first = (VPathCreator)VNodeCreator.FromJson(definition);
        var second = (VPathCreator)VNodeCreator.FromJson(definition);
        Check(VMath.getRandomDouble(0, 1) == next && !ReferenceEquals(first.PathQueue, second.PathQueue), "加载和缓存不消耗随机且配置相互独立");
        // 与位置修饰完全相同的普通VNode对比下一随机数；路径本身不增加抽样。
        var battle = CreateBattle(out var world);
        VMath.setRandomSeed(912);
        var emitter = StartSpawnFixture(battle, definition);
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        double afterPath = VMath.getRandomDouble(0, 1);
        // 保存运动及位置随机结果，与旧VNode逐颗比对，而非仅比较抽样次数。
        var pathStates = emitter.Root.Members.Select(node => (node.WorldPosition, node.Speed, node.Velocity, node.BirthIndex)).ToArray();
        world.Free();
        json["Core"]!["Type"] = "VNode";
        json.AsObject().Remove("PathQueue");
        battle = CreateBattle(out world);
        VMath.setRandomSeed(912);
        emitter = StartSpawnFixture(battle, json.ToJsonString());
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(VMath.getRandomDouble(0, 1) == afterPath, "路径生成沿用原随机消耗数量");
        for (int index = 0; index < pathStates.Length; index++)
        {
            // 对应节点仅相差缓存的路径偏移，随机运动与位置修饰必须一致。
            var actual = emitter.Root.Members[index];
            var expected = pathStates[index];
            Check(actual.Speed == expected.Speed && actual.Velocity == expected.Velocity
                && (actual.WorldPosition + new Vector2(expected.BirthIndex * 25, 0)).DistanceTo(expected.WorldPosition) < 0.001,
                "路径与旧VNode逐颗随机求值顺序一致");
        }
        world.Free();
        // 每次新建完整战场，逐固定步记录节点与子弹的状态。
        var results = new List<(Vector2 Position, Vector2 Velocity, double Age, int Index)>[2];
        for (int run = 0; run < 2; run++)
        {
            battle = CreateBattle(out world);
            VMath.setRandomSeed(912);
            emitter = StartSpawnFixture(battle, definition);
            results[run] = new();
            for (int step = 0; step < 20; step++)
            {
                battle.StepFixed(Vector2.Zero, false);
                foreach (var node in emitter.Root.EnumerateMembers(true))
                    results[run].Add((node.WorldPosition, node.Velocity, node.Age, node.BirthIndex));
            }
            world.Free();
        }
        Check(results[0].SequenceEqual(results[1]), "完整相同初始状态下路径和子弹逐步重现一致");
    }
}
