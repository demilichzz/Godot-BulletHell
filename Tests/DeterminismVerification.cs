using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>验证弧度坐标工具、固定随机序列、Boss圆周移动时序及相同输入下的战斗重现。</summary>
public partial class DeterminismVerification : Node
{
    // 本场验证已通过的断言数量。
    private int _checks;
    /// <summary>等待测试场景入树完成后运行验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>检查断言并累积通过数量。</summary>
    /// <param name="condition">必须成立的条件。</param>
    /// <param name="message">失败说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    /// <summary>运行随机与移动验证，失败时返回非零退出码。</summary>
    private void Run()
    {
        try
        {
            VerifyRandom();
            VerifyGeometry();
            VerifyMovement();
            VerifyReplay();
            GD.Print($"PASS: {_checks} determinism assertions");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
    /// <summary>验证固定样本、闭区间、拒绝采样及非法输入不消耗随机序列。</summary>
    private void VerifyRandom()
    {
        VMath.setRandomSeed();
        Check(VMath.randomSeed == 0, "默认种子0");
        // SplitMix64种子0的前三个标准输出高32位，映射到完整有符号范围。
        int[] expected = { 0x6220A839, unchecked((int)0xEE789E6A), unchecked((int)0x86C45D18) };
        foreach (int value in expected) Check(VMath.getRandomInt(int.MinValue, int.MaxValue) == value, "固定算法样本");
        VMath.setRandomSeed();
        Check(VMath.getRandomDouble(0, 1) == (0xE220A8397B1DCDAFUL >> 11) / 9007199254740991.0, "固定53位小数样本");
        VMath.setRandomSeed();
        Check(VMath.getRandomInt(-1, int.MaxValue) == 0x6E789E69, "拒绝首个不能均分的样本");
        // 混合调用在负种子下也必须按相同顺序完全复现。
        VMath.setRandomSeed(-123);
        var integers = new List<int>();
        var values = new List<double>();
        for (int index = 0; index < 100; index++)
        {
            integers.Add(VMath.getRandomInt(-300, 800));
            values.Add(VMath.getRandomDouble(-5, 10));
        }
        VMath.setRandomSeed(-123);
        for (int index = 0; index < 100; index++)
        {
            Check(integers[index] == VMath.getRandomInt(-300, 800), "整数重置序列");
            Check(values[index] == VMath.getRandomDouble(-5, 10), "小数重置序列");
        }
        Check(VMath.randomSeed == -123, "抽样不改变初始种子属性");
        // 小整数区间必须能覆盖两个端点，极端浮点区间不能溢出。
        var endpoints = new HashSet<int>();
        for (int index = 0; index < 1000; index++)
        {
            int sample = VMath.getRandomInt(-1, 1);
            endpoints.Add(sample);
            Check(sample >= -1 && sample <= 1, "整数闭区间");
            double extreme = VMath.getRandomDouble(-double.MaxValue, double.MaxValue);
            Check(double.IsFinite(extreme) && extreme >= -double.MaxValue && extreme <= double.MaxValue, "浮点极端区间有限");
            double positive = VMath.getRandomDouble(double.MaxValue / 2, double.MaxValue);
            Check(double.IsFinite(positive) && positive >= double.MaxValue / 2 && positive <= double.MaxValue, "正数大区间有限");
        }
        Check(endpoints.SetEquals(new[] { -1, 0, 1 }), "整数两个端点可达");
        // 相邻可表示浮点数之间不存在其他值，可直接验证小数闭区间的两个端点。
        var floatEndpoints = new HashSet<double>();
        double adjacent = Math.BitIncrement(1.0);
        for (int index = 0; index < 100; index++) floatEndpoints.Add(VMath.getRandomDouble(1, adjacent));
        Check(floatEndpoints.SetEquals(new[] { 1.0, adjacent }), "小数两个端点可达");
        VMath.setRandomSeed(42);
        int next = VMath.getRandomInt(int.MinValue, int.MaxValue);
        VMath.setRandomSeed(42);
        Check(VMath.getRandomInt(int.MaxValue, int.MaxValue) == int.MaxValue, "相等整数端点");
        Check(VMath.getRandomDouble(double.MaxValue, double.MaxValue) == double.MaxValue, "相等小数端点");
        // 反向范围和非有限端点必须报错，不能影响后续抽样。
        Action[] invalid = {
            () => VMath.getRandomInt(1, 0), () => VMath.getRandomDouble(1, -1),
            () => VMath.getRandomDouble(double.NaN, 1), () => VMath.getRandomDouble(0, double.PositiveInfinity)
        };
        foreach (var action in invalid)
        {
            try { action(); Check(false, "非法范围未报错"); }
            catch (ArgumentOutOfRangeException) { _checks++; }
        }
        Check(VMath.getRandomInt(int.MinValue, int.MaxValue) == next, "相等及非法范围不消耗序列");
    }
    /// <summary>验证坐标几何、弧度标准化及单位转换，不改变随机序列。</summary>
    private void VerifyGeometry()
    {
        VMath.setRandomSeed(8);
        int expectedRandom = VMath.getRandomInt(0, 1000);
        VMath.setRandomSeed(8);
        Check(VMath.GetDistanceBetween2Points(1, 2, 4, 6) == 5, "坐标3-4-5距离");
        Check(VMath.GetDistanceBetween2Points(Vector2.Zero, new Vector2(3, 4)) == 5, "向量距离重载");
        Check(VMath.GetDistanceBetween2Points(2, 3, 2, 3) == 0, "重合距离");
        Check(double.IsFinite(VMath.GetDistanceBetween2Points(0, 0, 1e200, 1e200)), "距离平方防溢出");
        Check(VMath.GetDistanceBetween2Points(0, 0, 1e-200, 0) == 1e-200, "微小距离防下溢");
        // 八方向按屏幕顺时针排列，角度步进π/4。
        Vector2[] directions = { Vector2.Right, Vector2.One, Vector2.Down, new(-1, 1), Vector2.Left, -Vector2.One, Vector2.Up, new(1, -1) };
        for (int index = 0; index < directions.Length; index++)
        {
            Check(Math.Abs(VMath.GetAngleBetween2Points(Vector2.Zero, directions[index]) - index * Math.PI / 4) < 1e-12, "八方向弧度");
        }
        Check(VMath.GetAngleBetween2Points(2, 3, 2, 3) == 0, "重合角度");
        Check(Math.Abs(VMath.GetAngleBetween2Points(-double.MaxValue, 0, double.MaxValue, double.MaxValue) - Math.Atan(0.5)) < 1e-12, "极大坐标方向");
        Check(VMath.StandardizationAngle(Math.Tau) == 0 && VMath.StandardizationAngle(-Math.Tau * 2) == 0, "整圈标准化");
        Check(Math.Abs(VMath.StandardizationAngle(-Math.PI / 2) - Math.PI * 1.5) < 1e-12, "负角度标准化");
        Check(Math.Abs(VMath.StandardizationAngle(Math.Tau * 3 + 0.5) - 0.5) < 1e-12, "多圈标准化");
        Check(VMath.StandardizationAngleFloat(-1e-8) == 0, "单精度整圈上界归零");
        Check(BitConverter.DoubleToInt64Bits(VMath.StandardizationAngle(-0.0)) == 0, "负零归零");
        // 极坐标计算不修改源坐标，方向为弧度，位移可为负。
        var source = new Vector2(10, 20);
        Check(VMath.PolarMove(source, 0, 5) == new Vector2(15, 20), "极坐标向右");
        Check(VMath.PolarMove(source, Math.PI / 2, 5).DistanceTo(new Vector2(10, 25)) < 1e-5, "极坐标向下");
        Check(VMath.PolarMove(source, Math.PI, 5).DistanceTo(new Vector2(5, 20)) < 1e-5, "极坐标向左");
        Check(VMath.PolarMove(source, -Math.PI / 2, 5).DistanceTo(new Vector2(10, 15)) < 1e-5, "极坐标向上");
        Check(VMath.PolarMove(source, Math.Tau, -5) == new Vector2(5, 20), "极坐标负距离与整圈");
        Check(VMath.PolarMove(source, 1, 0) == source && source == new Vector2(10, 20), "极坐标零距离不修改原点");
        Check(VMath.DegreesToRadians(180) == Math.PI && VMath.RadiansToDegrees(Math.PI) == 180, "半圈转换");
        Check(VMath.DegreesToRadians(720) == Math.Tau * 2 && VMath.RadiansToDegrees(-Math.Tau) == -360, "转换保留符号与圈数");
        Check(Math.Abs(VMath.RadiansToDegrees(VMath.DegreesToRadians(123.456)) - 123.456) < 1e-10, "转换往返误差");
        // 非有限输入必须报错，超出返回类型范围必须明确溢出。
        Action[] invalid = {
            () => VMath.GetDistanceBetween2Points(double.NaN, 0, 0, 0),
            () => VMath.GetAngleBetween2Points(Vector2.Zero, new Vector2(float.PositiveInfinity, 0)),
            () => VMath.StandardizationAngle(double.PositiveInfinity),
            () => VMath.PolarMove(source, double.NaN, 1),
            () => VMath.DegreesToRadians(double.NaN), () => VMath.RadiansToDegrees(double.NegativeInfinity)
        };
        foreach (var action in invalid)
        {
            try { action(); Check(false, "非法数学输入未拒绝"); }
            catch (ArgumentOutOfRangeException) { _checks++; }
        }
        Action[] overflow = {
            () => VMath.GetDistanceBetween2Points(-double.MaxValue, 0, double.MaxValue, 0),
            () => VMath.PolarMove(Vector2.Zero, 0, double.MaxValue),
            () => VMath.RadiansToDegrees(double.MaxValue)
        };
        foreach (var action in overflow)
        {
            try { action(); Check(false, "数学结果溢出未拒绝"); }
            catch (OverflowException) { _checks++; }
        }
        Check(VMath.getRandomInt(0, 1000) == expectedRandom, "坐标工具不消耗随机序列");
    }
    /// <summary>创建仅通过显式调用推进的独立战斗。</summary>
    /// <param name="world">返回的战场节点，验证后须释放。</param>
    /// <returns>已初始化且关闭自动物理更新的管理器。</returns>
    private BattleManager CreateBattle(out Node2D world)
    {
        world = new Node2D();
        AddChild(world);
        // 每场创建由BattleManager重置固定随机种子。
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world);
        battle.SetPhysicsProcess(false);
        return battle;
    }
    /// <summary>验证第5秒选点、匀速抵达、周期事件及停止后的状态。</summary>
    private void VerifyMovement()
    {
        // 独立推进Boss避免伤害提前结束场景，弹幕仍由真实管理器登记。
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        var boss = battle.Boss;
        var phase = (B01_Phase01)boss.CurrentPhase!;
        var center = new Vector2(640, 250);
        VerificationClock.BossSeconds(boss, 4.99);
        Check(boss.Position == center && !phase.IsMoving, "5秒前静止");
        VerificationClock.BossSeconds(boss, 0.01);
        Check(boss.Position == center && phase.IsMoving && Math.Abs(phase.MoveTarget.DistanceTo(center) - 200) < 0.001, "第5秒只选圆周目标");
        var target = phase.MoveTarget;
        VerificationClock.BossSeconds(boss, 0.5);
        Check(Math.Abs(boss.Position.DistanceTo(center) - 50) < 0.001, "100像素每秒");
        VerificationClock.BossSeconds(boss, 1.5);
        Check(boss.Position.DistanceTo(target) < 0.001, "第7秒在浮点容差内抵达");
        // 单精度圆周坐标可能略超出200像素，推进微秒后须精确停止。
        VerificationClock.BossSeconds(boss, 1.0 / 60000);
        Check(boss.Position == target && !phase.IsMoving, "抵达后精确停止");
        VerificationClock.BossSeconds(boss, 2.99 - 1.0 / 60000);
        Check(boss.Position == target && phase.MoveTarget == target, "到达后等待下一选点");
        VerificationClock.BossSeconds(boss, 0.01);
        Check(boss.Position == target && phase.MoveTarget != target && phase.IsMoving, "第10秒选择新目标但不提前移动");
        Check(Math.Abs(phase.MoveTarget.DistanceTo(center) - 200) < 0.001, "后续目标仍在固定圆周");
        // 直接退出阶段后调用也必须没有移动、发射或随机消耗。
        var stoppedPosition = boss.Position;
        int stoppedCount = battle.Bullets.ActiveCount;
        VMath.setRandomSeed(77);
        int expectedNext = VMath.getRandomInt(0, int.MaxValue);
        VMath.setRandomSeed(77);
        boss.Stop();
        phase.Advance(boss, 20);
        VerificationClock.BossSeconds(boss, 20);
        Check(boss.Position == stoppedPosition && !phase.IsMoving && battle.Bullets.ActiveCount == stoppedCount, "退出后停止所有事件");
        Check(VMath.getRandomInt(0, int.MaxValue) == expectedNext, "退出后不消耗随机数");
        world.Free();
        // 比较单个大步长与60Hz推进，发射起点也应与每个事件时刻一致。
        var large = CaptureMovement(new[] { 12.0 });
        var fixedSteps = CaptureMovement(Enumerable.Repeat(1.0 / 60, 720));
        Check(large.Position.DistanceTo(fixedSteps.Position) < 0.002 && large.Target == fixedSteps.Target, "大小步长移动一致");
        Check(large.NextRandom == fixedSteps.NextRandom, "大小步长随机消耗一致");
        Check(large.Origins.Length == 12 * 48 && large.Origins.Length == fixedSteps.Origins.Length, "大小步长射击次数一致");
        for (int index = 0; index < large.Origins.Length; index++)
            Check(large.Origins[index].DistanceTo(fixedSteps.Origins[index]) < 0.002, "逐批发射起点一致");
        Check(large.Origins[4 * 48] == center && Math.Abs(large.Origins[5 * 48].DistanceTo(center) - 100) < 0.002, "移动中从当前起点发射");
    }
    /// <summary>按给定步长记录Boss位置、目标和所有发射起点。</summary>
    /// <param name="steps">依次推进的非负秒数。</param>
    /// <returns>最终位置、目标、发射起点和下一次随机值。</returns>
    private (Vector2 Position, Vector2 Target, Vector2[] Origins, int NextRandom) CaptureMovement(IEnumerable<double> steps)
    {
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        foreach (double step in steps) VerificationClock.BossSeconds(battle.Boss, step);
        // 子弹未推进，SpawnPosition精确反映每个射击事件的起点。
        var result = (battle.Boss.Position, ((B01_Phase01)battle.Boss.CurrentPhase!).MoveTarget,
            battle.Bullets.ActiveBullets.Select(bullet => bullet.SpawnPosition).ToArray(), VMath.getRandomInt(0, int.MaxValue));
        world.Free();
        return result;
    }
    /// <summary>记录相同固定步输入下的逐步状态，验证重开完全复现。</summary>
    private void VerifyReplay()
    {
        var battle = CreateBattle(out var world);
        var first = CaptureBattle(battle);
        battle.Restart();
        Check(VMath.randomSeed == 0 && battle.Elapsed == 0 && battle.Boss.Position == BattleConfig.BossSpawn, "重开种子位置时钟归零");
        var second = CaptureBattle(battle, true);
        Check(first.SequenceEqual(second), "同种子同输入逐步战斗状态完全一致");
        // StopBattle同样阻止抽样，哪怕外部继续调用Step。
        battle.Restart();
        int expected = VMath.getRandomInt(0, 100000);
        battle.Restart();
        battle.StopBattle();
        VerificationClock.BattleSeconds(battle, 20, Vector2.Zero, false);
        Check(battle.Elapsed == 0 && VMath.getRandomInt(0, 100000) == expected, "战斗停止后不推进随机序列");
        world.Free();
    }
    /// <summary>施加固定的移动与闪避输入，并记录玩家、Boss及弹幕状态。</summary>
    /// <param name="battle">从初始状态开始的战斗。</param>
    /// <param name="extraDisplayUpdates">额外刷新显示以验证渲染频率不影响模拟，默认false。</param>
    /// <returns>每个物理步后的状态序列。</returns>
    private List<string> CaptureBattle(BattleManager battle, bool extraDisplayUpdates = false)
    {
        var frames = new List<string>();
        // 确认对比涵盖真实移动，而不是仅比较提前结束后的冻结场景。
        bool sawMovement = false;
        // 12秒固定60Hz，右移和左移交替，按每秒一次触发闪避。
        for (int tick = 0; tick < 720; tick++)
        {
            var input = tick / 180 % 2 == 0 ? Vector2.Right : Vector2.Left;
            if (extraDisplayUpdates)
                for (int frame = 0; frame < 3; frame++) battle.Player.FinishStep();
            battle.StepFixed(input, tick % 60 == 0);
            var target = (battle.Boss.CurrentPhase as B01_Phase01)?.MoveTarget;
            sawMovement |= (battle.Boss.CurrentPhase as B01_Phase01)?.IsMoving == true;
            frames.Add($"{battle.State}|{battle.Timers.NowUnits}|{battle.Timers.ActiveCount}|{battle.Elapsed:R}|{FormatVector(battle.Player.Position)}|{battle.Player.Health.Hp}|{FormatVector(battle.Boss.Position)}|{FormatVector(target ?? Vector2.Zero)}|{battle.Boss.Hp}|"
                + string.Join(";", battle.Bullets.ActiveBullets.Select(bullet => $"{FormatVector(bullet.Position)}:{FormatVector(bullet.Velocity)}:{bullet.Age:R}:{bullet.Team}")));
        }
        Check(sawMovement, "重现对比覆盖5秒后的实际移动");
        return frames;
    }
    /// <summary>用可往返精度记录二维坐标，避免默认字符串舍入掩盖差异。</summary>
    /// <param name="value">逻辑像素坐标或逻辑像素/秒的速度向量。</param>
    /// <returns>包含两个原始浮点分量的字符串。</returns>
    private static string FormatVector(Vector2 value) => $"{value.X:R},{value.Y:R}";
}
