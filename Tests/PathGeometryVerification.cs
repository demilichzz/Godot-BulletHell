using Godot;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>公共几何拆分前后的浮点采样契约和调用方隔离验证。</summary>
public partial class BattleVerification
{
    /// <summary>无战斗时使用几何，检查懒取目标、一次快照、重入及输入隔离。</summary>
    private void VerifyIndependentPathGeometry()
    {
        // 本专项在创建任何战斗前执行，几何只接受调用方提供的坐标。
        Check(!GlobalEvent.IsValidBattle(GlobalEvent.CaptureCurrent()), "公共几何验证不需要活动战斗");
        VMath.setRandomSeed(819);
        double expectedRandom = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(819);
        var line = JsonData.Parse("[" + PathLine + "]", "独立直线", element => VPathJson.Read(element, 3));
        var straight = line.Sample(new Vector2(13, 17), 3, () => throw new Exception("普通路径不应获取目标"));
        Check(straight.SequenceEqual(new[] { Vector2.Zero, new Vector2(50, 0), new Vector2(100, 0) }), "普通路径完全不读取目标提供器");
        // 多个瞄准段偏移不同，整次采样仍只能捕获一个目标。
        var geometry = JsonData.Parse("""[{"Type":"AimPlayer"},{"Type":"AimPlayer","X":20,"Y":10},{"Type":"AimPlayer","X":-30,"Y":40}]""",
            "独立瞄准", element => VPathJson.Read(element, 4));
        int calls = 0;
        var first = geometry.Sample(Vector2.Zero, 4, () => { calls++; return new Vector2(100 * calls, 200); });
        Check(calls == 1 && first.SequenceEqual(new[] { Vector2.Zero, new Vector2(100, 200), new Vector2(120, 210), new Vector2(70, 240) }),
            "同一路径多个瞄准段共享一次调用方快照");
        var second = geometry.Sample(new Vector2(10, 10), 4, () => new Vector2(300, 400));
        Check(second[^1] == new Vector2(260, 430) && first[^1] == new Vector2(70, 240), "几何复用不覆盖旧采样或沿用旧目标");
        // 重入同一个几何对象不能覆盖外层端点或函数上下文。
        var nested = Array.Empty<Vector2>();
        var outer = geometry.Sample(Vector2.Zero, 4, () =>
        {
            nested = geometry.Sample(new Vector2(10, 10), 4, () => new Vector2(200, 300)).ToArray();
            return new Vector2(100, 200);
        });
        Check(nested[^1] == new Vector2(160, 330) && outer.SequenceEqual(first), "嵌套采样独立持有目标与坐标缓存");
        // 缺失目标及提供器异常携带段位置；失败后不污染后续调用。
        foreach (Func<Vector2>? target in new Func<Vector2>?[] { null, () => throw new InvalidOperationException("提供器失败") })
        {
            bool rejected = false;
            try { geometry.Sample(Vector2.Zero, 4, target); }
            catch (JsonException error) { rejected = error.Message.Contains("PathQueue[0]"); }
            Check(rejected, "缺失或失败目标被附加路径段诊断");
        }
        Check(geometry.Sample(Vector2.Zero, 4, () => new Vector2(100, 200)).SequenceEqual(first), "失败取样后仍可完整重试");
        Check(VMath.getRandomDouble(0, 1) == expectedRandom, "独立解析和采样不消耗业务随机");
        // 连输入数组也不与几何共享，防止调用方改动原列表破坏已编译对象。
        var moves = new[] { new VNodeMoveActionAttribute { Type = "XYMove", X = 40 } };
        var segments = new[] { new VPathSegmentAttribute { EndMoveQueue = moves } };
        var frozen = new VPathGeometry(segments);
        moves[0] = moves[0] with { X = 90 }; segments[0] = new VPathSegmentAttribute();
        Check(frozen.Sample(Vector2.Zero, 2)[^1] == new Vector2(40, 0), "公共几何冻结外部段列表与位移列表");
    }
    /// <summary>固定多种采样密度及参考坐标的逐位结果，防止重构改变弧长分配或舍入顺序。</summary>
    private void VerifyPathSamplingContract()
    {
        // 基线由拆分前0016eaf的生产采样器生成，覆盖曲线、世界目标、瞄准及多段连接。
        string[] queues =
        {
            PathLine,
            """{"PathMode":"Bezier","StartMoveQueue":[{"Type":"XYMove","X":13.25,"Y":-11.5}],"EndMoveQueue":[{"Type":"TarMove","X":935.25,"Y":415.75,"Dist":253.5}],"ControlPoints":[{"X":-100.25,"Y":200.75},{"X":300.5,"Y":-91.25}],"Samples":777}""",
            """{"PathMode":"Function","AxisMode":"Relative","EndMoveQueue":[{"Type":"PMove","Angle":"PI/3","Dist":205.75}],"X":"L*t","Y":"17*sin(TAU*t)","Samples":333}""",
            """{"PathMode":"Function","EndMoveQueue":[{"Type":"XYMove","X":300.5,"Y":-43.125}],"TMin":-2,"TMax":3,"X":"SX+(EX-SX)*(t+2)/5","Y":"SY+(EY-SY)*(t+2)/5+17*sin(PI*(t+2)/5)","Samples":511}""",
            PathLine + """,{"Type":"AimPlayer","X":17.125,"Y":-29.5},{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":-91.75,"Y":-15.5}]},{"PathMode":"Function","AxisMode":"Relative","EndMoveQueue":[{"Type":"XYMove","X":125.25,"Y":91.75}],"X":"L*t","Y":"21*sin(PI*t)"}"""
        };
        var battle = CreateBattle(out var world);
        var trace = new StringBuilder();
        try
        {
            battle.Boss.Stop(); battle.Player.Attack.Stop();
            // 三次变化的玩家和父位置，防止编译对象误保留第一次运行上下文。
            for (int context = 0; context < 3; context++)
            {
                Vector2 source = new(123.25f + context * 47.375f, 234.5f - context * 35.125f);
                battle.Player.Position = new Vector2(789.125f - context * 69.625f, 654.25f + context * 11.875f);
                foreach (string queue in queues)
                    foreach (int amount in new[] { 5, 17, 257, 1025 })
                    {
                        var path = (VPathCreator)VNodeCreator.FromJson(PathFixture(queue, amount));
                        foreach (Vector2 point in path.SampleGeometry(source))
                            trace.Append(BitConverter.SingleToInt32Bits(point.X)).Append(',').Append(BitConverter.SingleToInt32Bits(point.Y)).Append(';');
                    }
            }
            // 单个摘要包含19560个偏移，比较浮点位模式而不是格式化小数。
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString())));
            GD.Print("Path geometry baseline: " + actual);
            Check(actual == "3D4DF83925513C6C33585C4D5E06C516FD1A92FD4A4DA958C9A1C3C4CF1FBC9E", "公共路径几何保持拆分前的逐位采样结果");
        }
        finally { world.Free(); }
    }
}
