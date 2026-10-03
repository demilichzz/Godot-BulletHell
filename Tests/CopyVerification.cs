using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>同级配置复制、严格覆盖、名称及独立运行状态的定向验证。</summary>
public partial class BattleVerification
{
    /// <summary>运行复制配置与实际生成的等价验证。</summary>
    private void VerifyCopies()
    {
        // 全部类型共用展开入口，来源子树带名称用于检查独立复制。
        foreach (string type in new[] { "VNode", "VBullet", "VPath", "VLaser" })
        {
            var source = JsonNode.Parse("""
                {"Core":{"Type":"VNode","Name":"Source","Amount":2},"BaseAttributes":[{"Speed":80},{"Speed":90}],
                 "AddAttributes":{"Angle":0.2},"Timeline":[{"StartMs":0}],
                 "Children":[{"Core":{"Type":"VNode","Name":"Leaf"},"BaseAttributes":[{}],"Timeline":[{"StartMs":0}]}]}
                """)!;
            source["Core"]!["Type"] = type;
            if (type == "VBullet") source["Display"] = new JsonObject();
            if (type == "VPath")
            {
                source.AsObject().Remove("BaseAttributes");
                source["PathQueue"] = JsonNode.Parse("""[{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":100}]}]""");
            }
            if (type == "VLaser")
            {
                source["BaseAttributes"] = JsonNode.Parse("[{},{}]");
                source["Laser"] = JsonNode.Parse("""{"Length":100}""");
            }
            var root = CopyRoot(source, JsonNode.Parse("""
                {"Core":{"CopySource":"Source","Name":"Mirror"},"AddAttributes":{"Angle":-0.2}}
                """)!);
            var parsed = VNodeCreator.FromJson(root.ToJsonString());
            var original = parsed.Children[0];
            var clone = parsed.Children[1];
            Check(original.GetType() == clone.GetType() && clone.Core.Type == type, "复制继承四种Creator类型：" + type);
            Check(original.Core.Name == "Source" && clone.Core.Name == "Source_copy_Mirror"
                && clone.Children[0].Core.Name == "Leaf_copy_Mirror", "根及命名后代追加同一后缀");
            Check(original.Core.Id == "VN001001" && clone.Core.Id == "VN001002"
                && clone.Children[0].Core.Id == "VN001002001", "副本按所在树位置生成Id");
            Check(original.AddAttributes.Angle == 0.2 && clone.AddAttributes.Angle == -0.2, "副本覆盖不改来源");
            Check(!ReferenceEquals(original, clone) && !ReferenceEquals(original.Core, clone.Core)
                && !ReferenceEquals(original.Children[0], clone.Children[0]) && !ReferenceEquals(original.Batches, clone.Batches),
                "副本配置、Children及运行批次独立");
        }

        // 基础项按下标覆盖，嵌套对象合并，其他数组替换。
        var definition = CopyRoot(JsonNode.Parse("""
            {"Core":{"Type":"VBullet","Name":"Base","Amount":2},"Display":{"TextureName":"Dot","TextureIndex":2},
             "BaseAttributes":[{"Angle":0.1,"Speed":80,"RefMoveQueue":[{"Type":"XYMove","X":400,"Y":100}]},{"Speed":90}],
             "AddAttributes":{"Angle":0.2,"Speed":3},"RandDiffAttributes":{"Batch":{"Angle":0.1,"Speed":2},"Member":{"Speed":4}},
             "Timeline":[{"StartMs":0}],"MemberTimeline":[{"StartMs":50,"Set":{"Speed":0}}],
             "Children":[{"Core":{"Type":"VNode","Name":"Inner"},"BaseAttributes":[{}]}]}
            """)!, JsonNode.Parse("""
            {"Core":{"CopySource":"Base","Name":"Patch"},"Display":{"TextureIndex":0},
             "BaseAttributes":[{"Angle":-0.1,"RefMoveQueue":[]}],
             "RandDiffAttributes":{"Batch":{"Angle":0}},"Timeline":[],"MemberTimeline":[],"Children":[]}
            """)!);
        var tree = VNodeCreator.FromJson(definition.ToJsonString());
        var copy = (VBulletCreator)tree.Children[1];
        Check(copy.BaseAttributes.Count == 2 && copy.BaseAttributes[0].Speed == 80
            && copy.BaseAttributes[1].Speed == 90 && copy.BaseAttributes[0].Angle == -0.1, "基础列表按下标保留字段与尾项");
        Check(copy.BaseAttributes[0].RefMoveQueue.Count == 0 && tree.Children[0].BaseAttributes[0].RefMoveQueue.Count == 1,
            "嵌套位移数组整体替换，可显式清空");
        Check(copy.Display.TextureName == "Dot" && copy.Display.TextureIndex == 0
            && copy.RandDiffAttributes.Batch.Speed == 2 && copy.RandDiffAttributes.Member.Speed == 4
            && copy.RandDiffAttributes.Batch.Angle == 0, "对象递归合并且保留未覆盖随机组");
        Check(copy.Timeline.Count == 0 && copy.MemberTimeline.Count == 0 && copy.Children.Count == 0, "时间线和子树数组整体清空");
        definition["Children"]![1]!["BaseAttributes"] = new JsonArray();
        tree = VNodeCreator.FromJson(definition.ToJsonString());
        Check(tree.Children[1].BaseAttributes.Count == 2 && tree.Children[1].BaseAttributes[0].Angle == 0.1,
            "空基础覆盖数组不删减源列表");
        definition["Children"]![1]!["BaseAttributes"] = JsonNode.Parse("[{},{},{\"Speed\":100}]");
        Check(VNodeCreator.FromJson(definition.ToJsonString()).Children[1].BaseAttributes.Count == 3, "额外基础项追加并使用正常默认值");

        // 原始源内部已有复制时，先完成局部展开，再整体复制且不重新执行复制指令。
        var nested = CopyRoot(JsonNode.Parse("""
            {"Core":{"Type":"VNode","Name":"Outer"},"BaseAttributes":[{}],"Children":[
             {"Core":{"Type":"VNode","Name":"Seed"},"BaseAttributes":[{}]},
             {"Core":{"CopySource":"Seed","Name":"Pair"}}]}
            """)!, JsonNode.Parse("""{"Core":{"CopySource":"Outer","Name":"Second"}}""")!);
        tree = VNodeCreator.FromJson(nested.ToJsonString());
        Check(tree.Children[1].Children[1].Core.Name == "Seed_copy_Pair_copy_Second",
            "继承子树中的复制先展开再重命名，不重复解析");
        nested["Children"]![1]!["Children"] = JsonNode.Parse("""
            [{"Core":{"Type":"VNode","Name":"Replacement"},"BaseAttributes":[{}]},
             {"Core":{"CopySource":"Replacement","Name":"Twin"}}]
            """);
        tree = VNodeCreator.FromJson(nested.ToJsonString());
        Check(tree.Children[1].Children[0].Core.Name == "Replacement"
            && tree.Children[1].Children[1].Core.Name == "Replacement_copy_Twin", "显式替换Children在独立同级范围解析，不追加外层后缀");

        // 同一来源可复制多份；原始VNode可显式将可空寿命覆盖为null。
        var multiple = CopyRoot(JsonNode.Parse("""
            {"Core":{"Type":"VNode","Name":"Shared","LifeTimeMs":100},"BaseAttributes":[{}],
             "Children":[{"Core":{"Type":"VNode"},"BaseAttributes":[{}]}]}
            """)!, JsonNode.Parse("""{"Core":{"CopySource":"Shared","Name":"One","LifeTimeMs":null}}""")!);
        multiple["Children"]!.AsArray().Add(JsonNode.Parse("""{"Core":{"CopySource":"Shared","Name":"Two","Type":"VNode"}}"""));
        tree = VNodeCreator.FromJson(multiple.ToJsonString());
        Check(tree.Children[0].Core.LifeTimeMs == 100 && tree.Children[1].Core.LifeTimeMs is null
            && tree.Children[2].Core.LifeTimeMs == 100, "多副本覆盖独立且null按原字段规则保留");
        Check(tree.Children[2].Core.Name == "Shared_copy_Two" && tree.Children[2].Children[0].Core.Name is null,
            "显式相同类型允许复制，未命名后代不产生名称");
        VerifyCopyFailures();
        Check(CaptureCopy(false).SequenceEqual(CaptureCopy(true)), "复制配置与手工展开配置在相同完整初态下逐步等价");
    }

    /// <summary>为两个同级Creator建立可独立加载的根节点。</summary>
    /// <param name="source">原始来源定义。</param>
    /// <param name="copy">复制或等价的显式定义。</param>
    /// <returns>零时刻激活的根Creator JSON对象。</returns>
    private static JsonObject CopyRoot(JsonNode source, JsonNode copy)
        => new()
        {
            ["Core"] = new JsonObject { ["Type"] = "VNode" },
            ["BaseAttributes"] = new JsonArray(new JsonObject()),
            ["Timeline"] = JsonNode.Parse("""[{"StartMs":0}]"""),
            ["Children"] = new JsonArray(source, copy)
        };

    /// <summary>验证引用边界、身份限制、数组及未知字段不会绕过原有校验。</summary>
    private void VerifyCopyFailures()
    {
        // 每个变体只改变一个关键约束，便于定位异常路径。
        foreach (string patch in new[]
        {
            """{"Core":{"CopySource":"Absent","Name":"X"}}""",
            """{"Core":{"CopySource":"Source"}}""",
            """{"Core":{"CopySource":null,"Name":"X"}}""",
            """{"Core":{"CopySource":3,"Name":"X"}}""",
            """{"Core":{"CopySource":"Source","Name":" "}}""",
            """{"Core":{"CopySource":"Source","Name":"X","Type":"VBullet"}}""",
            """{"Core":{"CopySource":"Source","Name":"X","Id":"bad"}}""",
            """{"Core":{"CopySource":"Source","Name":"X"},"Unknown":0}""",
            """{"Core":{"CopySource":"Source","Name":"X"},"BaseAttributes":[null]}""",
            """{"Core":{"CopySource":"Source","Name":"X"},"BaseAttributes":null}""",
            """{"Core":{"CopySource":"Source","Name":"X"},"Children":null}"""
        })
        {
            var data = CopyRoot(JsonNode.Parse("""{"Core":{"Type":"VNode","Name":"Source"},"BaseAttributes":[{}]}""")!,
                JsonNode.Parse(patch)!);
            RejectCopy(data.ToJsonString());
        }
        foreach (string data in new[]
        {
            """{"Core":{"Type":"VNode","CopySource":"Source","Name":"X"},"BaseAttributes":[{}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"CopySource":"Later","Name":"X"}},{"Core":{"Type":"VNode","Name":"Later"},"BaseAttributes":[{}]}]}""",
            """{"Core":{"Type":"VNode","Name":"Parent"},"BaseAttributes":[{}],"Children":[{"Core":{"CopySource":"Parent","Name":"X"}}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"A"},"BaseAttributes":[{}]},{"Core":{"CopySource":"A","Name":"X"}},{"Core":{"CopySource":"A_copy_X","Name":"Y"}}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"A"},"BaseAttributes":[{}]},{"Core":{"CopySource":"A","Name":"X"}},{"Core":{"CopySource":"A","Name":"X"}}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"A"},"BaseAttributes":[{}]},{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"CopySource":"A","Name":"X"}}]}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"CopySource":"Self","Name":"Self"}}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"A"},"BaseAttributes":[{}]},{"Core":{"CopySource":"A","Name":"X","Name":"Y"}}]}""",
            """{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"A"},"BaseAttributes":[{}],"Children":[{"Core":{"Type":"VNode","Name":"Child"},"BaseAttributes":[{}]}]},{"Core":{"CopySource":"A","Name":"X"}},{"Core":{"Type":"VNode","Name":"Child_copy_X"},"BaseAttributes":[{}]}]}"""
        }) RejectCopy(data);
        // 展开后的类型校验也必须指出副本来源，不能只留下最终配置路径。
        var invalidAmount = CopyRoot(
            JsonNode.Parse("""{"Core":{"Type":"VNode","Name":"Original"},"BaseAttributes":[{}]}""")!,
            JsonNode.Parse("""{"Core":{"CopySource":"Original","Name":"Bad","Amount":0}}""")!);
        try { VNodeCreator.FromJson(invalidAmount.ToJsonString(), "copy-fields.json"); Check(false, "无效副本数量须报错"); }
        catch (JsonException error)
        {
            Check(error.Message.Contains("copy-fields.json") && error.Message.Contains("$.Children[1]")
                && error.Message.Contains("CopySource 'Original'"), "字段错误保留文件、声明路径和复制来源");
        }
        // 不能跨两次加载寻找来源。
        VNodeCreator.FromJson("""{"Core":{"Type":"VNode","Name":"Elsewhere"},"BaseAttributes":[{}]}""");
        RejectCopy("""{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Children":[{"Core":{"CopySource":"Elsewhere","Name":"X"}}]}""");
    }

    /// <summary>检查加载失败且保留来源和JSON路径。</summary>
    /// <param name="json">应被拒绝的Creator定义。</param>
    private void RejectCopy(string json)
    {
        try { VNodeCreator.FromJson(json, "copy-invalid.json"); Check(false, "非法复制必须拒绝"); }
        catch (JsonException error)
        {
            Check(error.Message.Contains("copy-invalid.json") && error.Message.Contains("$"), "复制失败包含文件与JSON路径");
        }
    }

    /// <summary>对照复制与显式展开，覆盖随机、延迟、成员动作、父引用及结束清理。</summary>
    /// <param name="expanded">true使用手工展开的第二组。</param>
    /// <returns>逐步状态记录及最终随机值。</returns>
    private List<string> CaptureCopy(bool expanded)
    {
        // 原始来源有两个基础项和两轮，延迟出生、独立随机及短寿命后代。
        var source = JsonNode.Parse("""
            {"Core":{"Type":"VBullet","Name":"Seed","Amount":2,"LifeTimeMs":500},"Display":{},
             "BaseAttributes":[{"Angle":0.2,"Speed":80,"RefMoveQueue":[{"Type":"XYMove","X":400,"Y":100}]},
                               {"Angle":0.4,"Speed":90,"SpawnDelayMs":50,"RefMoveQueue":[{"Type":"XYMove","X":450,"Y":100}]}],
             "AddAttributes":{"SpawnDelayMs":100},"RandDiffAttributes":{"Batch":{"Angle":0.1},"Member":{"Speed":10}},
             "Timeline":[{"StartMs":0}],"MemberTimeline":[{"StartMs":100,"Set":{"Speed":30}}],
             "Children":[{"Core":{"Type":"VNode","Name":"Marker","LifeTimeMs":200},"BaseAttributes":[{}],"Timeline":[{"StartMs":0}]}]}
            """)!;
        JsonNode second = JsonNode.Parse("""{"Core":{"CopySource":"Seed","Name":"Mirror"},"BaseAttributes":[{"Angle":-0.2}]}""")!;
        if (expanded)
        {
            second = source.DeepClone();
            second["Core"]!["Name"] = "Seed_copy_Mirror";
            second["BaseAttributes"]![0]!["Angle"] = -0.2;
            second["Children"]![0]!["Core"]!["Name"] = "Marker_copy_Mirror";
        }
        var root = CopyRoot(source, second);
        var battle = CreateBattle(out var world);
        VMath.setRandomSeed(731);
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(731);
        VNodeCreator.FromJson(root.ToJsonString());
        Check(VMath.getRandomDouble(0, 1) == next, "复制加载不消耗随机");
        VMath.setRandomSeed(731);
        var emitter = StartSpawnFixture(battle, root.ToJsonString());
        Check(emitter.GetCreator("Seed_copy_Mirror") == emitter.Root.Children[1], "最终复制名称可用于现有查询");
        var output = new List<string>();
        for (int tick = 0; tick < 48; tick++)
        {
            battle.StepFixed(Vector2.Zero, false);
            output.Add(string.Join(";", emitter.Root.EnumerateMembers(true).Select(node =>
                $"{node.Creator!.Core.Id}:{node.ParentVNode?.Creator?.Core.Id}:{node.BirthIndex}:{node.WorldPosition}:{node.Velocity}:{node.Age:R}")));
        }
        Check(emitter.Root.Children.All(child => child.Members.Count == 0 && child.Batches.Count == 0
            && child.Children[0].Members.Count == 0), "副本及来源独立到期并回收批次与后代");
        output.Add(VMath.getRandomDouble(0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        world.Free();
        return output;
    }
}
