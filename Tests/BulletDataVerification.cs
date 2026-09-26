using Godot;
using System;
using System.Linq;
using System.Text.Json;

/// <summary>战斗验证的弹幕数据化定向用例。</summary>
public partial class BattleVerification
{
    // 含表达式、共享随机和首颗随机的最小完整定义。
    private const string DataSample = """
    {
      "Core": { "Id": "test", "Version": 2, "Amount": 3 },
      "Display": { "TextureName": "Scale", "TextureIndex": 0 },
      "BaseAttributes": { "Angle": "-(PI/6) + TAU/12", "Speed": "1e2 + (2*40)" },
      "AddAttributes": { "Speed": 10 },
      "AddAttributesRandDiff": { "Speed": 20 },
      "PositionAttributes": {
        "RefObject": "Emitter",
        "RefMoveQueue": [
          { "Type": "XYMove", "X": { "Value": 10, "RandDiff": 20, "ValueAdd": 3, "RandDiffAdd": 4 }, "Y": {} },
          { "Type": "PMove", "Angle": { "Value": "PI/2" }, "Dist": { "Value": -5 } }
        ]
      }
    }
    """;

    /// <summary>验证配置、随机消耗、独立实例、运动积分和固定输入重复结果。</summary>
    private void VerifyBulletData()
    {
        // 隔离战斗保持服务绑定，关闭非目标攻击和阶段。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var manager = battle.Bullets;
        var origin = new Vector2(-10000, -10000);
        var emitter = new B01P01_Emitter01();
        VMath.setRandomSeed(317);
        double untouched = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(317);
        var template = BulletQueue.FromJson(DataSample, "test.json");
        Check(VMath.getRandomDouble(0, 1) == untouched, "加载配置不消耗随机序列");
        Check(template.BaseAttributes.Angle == 0 && template.BaseAttributes.Speed == 180
            && template.AddAttributes.Angle == 0, "表达式优先级、常量、一元负号与科学计数法");
        var defaults = BulletQueue.FromJson(DataSample.Replace("\"Speed\": \"1e2 + (2*40)\"", "\"AAngle\": 0"));
        Check(defaults.BaseAttributes.Speed == 180 && defaults.AddAttributesRandDiff.Angle == 0,
            "基础缺省速度180且随机组缺省零");
        Check(template.PositionAttributes.RefMoveQueue is not BulletMoveActionAttribute[], "位移列表冻结为只读包装");

        // 无效配置在加载时拒绝，错误包含来源或字段路径。
        string[] invalid =
        {
            DataSample.Replace("\"Amount\": 3", "\"Amount\": \"3\""),
            DataSample.Replace("\"Amount\": 3", "\"Amount\": \"6/2\""),
            DataSample.Replace("\"Amount\": 3", "\"Amount\": 0"),
            DataSample.Replace("\"Amount\": 3", "\"Amount\": 3, \"Team\": \"enemy\""),
            DataSample.Replace("\"Core\":", "\"Source\": {}, \"Core\":"),
            DataSample.Replace("\"Version\": 2", "\"Version\": 1"),
            DataSample.Replace("1e2 + (2*40)", "1/0"),
            DataSample.Replace("1e2 + (2*40)", "1e999"),
            DataSample.Replace("1e2 + (2*40)", "sin(PI)"),
            DataSample.Replace("1e2 + (2*40)", "(1+2"),
            DataSample.Replace("1e2 + (2*40)", "2PI"),
            DataSample.Replace("\"TextureName\": \"Scale\"", "\"TextureName\": \"Missing\""),
            DataSample.Replace("\"TextureIndex\": 0", "\"TextureIndex\": 10"),
            DataSample.Replace("\"Speed\": 20", "\"Speed\": -20"),
            DataSample.Replace("\"Speed\": 10", "\"Damage\": 10"),
            DataSample.Replace("\"RefObject\": \"Emitter\"", "\"RefObject\": \"\""),
            DataSample.Replace("\"RefObject\": \"Emitter\"", "\"PositionMode\": \"Relative\""),
            DataSample.Replace("\"Amount\": 3", "\"Amount\": 3, \"Amount\": 4"),
            DataSample.Replace("\"Y\": {}", "\"Y\": null"),
            DataSample.Replace("\"Y\": {}", "\"Y\": {}, \"Dist\": {}")
        };
        foreach (string json in invalid)
        {
            try { BulletQueue.FromJson(json, "invalid.json"); Check(false, "无效配置须拒绝"); }
            catch (JsonException error) { Check(error.Message.Contains("invalid.json"), "配置错误包含来源"); }
        }

        // 按约定抽样顺序独立建立预期值，不从实现结果反推期望。
        VMath.setRandomSeed(42);
        double sharedX = 10 + VMath.getRandomDiff(20, RandomDiffMode.Center);
        var expectedSpeeds = new double[3];
        var expectedX = new double[3];
        for (int index = 0; index < 3; index++)
        {
            expectedSpeeds[index] = 180 + index * 10 + VMath.getRandomDiff(20, RandomDiffMode.Center);
            expectedX[index] = sharedX + index * 3 + VMath.getRandomDiff(4, RandomDiffMode.Center);
        }
        double expectedNext = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(42);
        var first = template.CreateInstance(origin);
        first.Emit(emitter, manager);
        for (int index = 0; index < 3; index++)
        {
            Check(first.BulletList[index].Speed == expectedSpeeds[index], "首颗及后续速度随机独立");
            Check(first.BulletList[index].GlobalPosition.DistanceTo(origin + new Vector2((float)expectedX[index], -5)) < 0.002,
                "共享位置随机、首颗位置随机与顺序位移");
        }
        Check(VMath.getRandomDouble(0, 1) == expectedNext, "固定字段求值顺序与零宽度不耗随机");
        Check(template.BulletList.Count == 0, "发射不向模板写入成员");
        try { first.Emit(emitter, manager); Check(false, "实例只能发射一次"); }
        catch (InvalidOperationException) { _checks++; }

        // 重复固定输入并逐步比较实际速度和位置，随机字段不随JSON键顺序变化。
        var second = template.CreateInstance(origin);
        VMath.setRandomSeed(42);
        second.Emit(emitter, manager);
        for (int step = 0; step < 120; step++)
        {
            for (int index = 0; index < 3; index++)
            {
                first.BulletList[index].Advance(1.0 / 60);
                second.BulletList[index].Advance(1.0 / 60);
                Check(first.BulletList[index].Position == second.BulletList[index].Position
                    && first.BulletList[index].Velocity == second.BulletList[index].Velocity, "固定种子逐步重现");
            }
        }
        Check(!ReferenceEquals(first.BulletList, second.BulletList), "实例独立成员列表");
        manager.Clear();
        Check(first.BulletList.Count == 0 && second.BulletList.Count == 0, "清场注销所有队列成员");

        // JSON键顺序不改变固定求值顺序；同向模式忽略AAngle随机。
        var reordered = BulletQueue.FromJson(DataSample.Replace(
            "\"Speed\": 20", "\"AAngle\": 100, \"Speed\": 20"));
        VMath.setRandomSeed(42);
        var ignoredAngle = reordered.CreateInstance(origin);
        ignoredAngle.Emit(emitter, manager);
        Check(ignoredAngle.BulletList[0].Speed == expectedSpeeds[0]
            && VMath.getRandomDouble(0, 1) == expectedNext, "忽略的加速度角不抽样且属性顺序不影响序列");
        manager.Clear();
        var overflow = BulletQueue.FromJson(DataSample.Replace("\"Speed\": 10", "\"Speed\": 1e308"));
        var overflowingInstance = overflow.CreateInstance(origin);
        try { overflowingInstance.Emit(emitter, manager); Check(false, "出生参数越界须拒绝"); }
        catch (ArgumentOutOfRangeException)
        {
            Check(overflowingInstance.BulletList.Count == 1 && manager.ActiveCount == 1, "求值异常保留已登记成员");
        }
        manager.Clear();

        // 满额与部分容量分别验证，不预取未生成子弹的随机。
        for (int index = 0; index < BattleConfig.MaxBullets - 1; index++)
            manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = origin });
        VMath.setRandomSeed(55);
        VMath.getRandomDiff(20, RandomDiffMode.Center);
        VMath.getRandomDiff(20, RandomDiffMode.Center);
        VMath.getRandomDiff(4, RandomDiffMode.Center);
        double afterOne = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(55);
        var partial = template.CreateInstance(origin);
        partial.Emit(emitter, manager);
        Check(partial.BulletList.Count == 1 && VMath.getRandomDouble(0, 1) == afterOne, "部分容量只抽共享值和成功首颗");
        VMath.setRandomSeed(66);
        double afterNone = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(66);
        var blocked = template.CreateInstance(origin);
        blocked.Emit(emitter, manager);
        Check(blocked.BulletList.Count == 0 && VMath.getRandomDouble(0, 1) == afterNone, "已满额完全不抽样");
        manager.Clear();

        // 以每秒60步验证有符号速度、独立加速度与参数重置。
        var accelerated = manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            Speed = -1,
            ASpeed = 60,
            AAngle = Math.PI / 2,
            AAngleIsSameAsAngle = true
        })!;
        accelerated.Advance(1.0 / 60);
        Check(accelerated.Velocity == Vector2.Zero && accelerated.Speed == -1
            && Mathf.IsEqualApprox(accelerated.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi), "过零保留实际朝向及外部速度");
        accelerated.Advance(1.0 / 60);
        Check(accelerated.Velocity.X > 0 && accelerated.Angle == 0
            && accelerated.GetNode<Sprite2D>("Sprite").Rotation == 0, "同向加速度沿外部Angle越过零速");
        accelerated.SetSpeed(10);
        accelerated.Advance(1.0 / 60);
        accelerated.SetDirection(Math.PI / 2);
        Check(accelerated.Velocity.DistanceTo(Vector2.Down * 10) < 0.001 && accelerated.ASpeed == 60,
            "设置方向按外部速度重建实际速度且保留加速度");
        var vector = manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = Vector2.Zero,
            Speed = 10,
            AAngle = Math.PI / 2,
            ASpeed = 60,
            AAngleIsSameAsAngle = false
        })!;
        for (int step = 0; step < 60; step++) vector.Advance(1.0 / 60);
        Check(vector.Position.DistanceTo(new Vector2(10, 30.5f)) < 0.001 && vector.Velocity.DistanceTo(new Vector2(10, 60)) < 0.001,
            "先加速后移动的60Hz向量积分");
        Check(vector.Angle == 0 && vector.Speed == 10
            && Math.Abs(vector.GetNode<Sprite2D>("Sprite").Rotation - Math.Atan2(60, 10)) < 0.00001,
            "加速度不改写外部参数且贴图跟随实际方向");
        vector.SetSpeed(-5);
        Check(vector.Velocity.DistanceTo(Vector2.Left * 5) < 0.001, "设置速度清除已累积速度并使用外部Angle");
        // 零速出生采用配置方向，负加速度沿独立加速度角反向起动。
        var stopped = manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            AngleRadians = Math.PI / 2,
            Speed = 0,
            AAngle = 0,
            ASpeed = -60,
            AAngleIsSameAsAngle = false
        })!;
        Check(Mathf.IsEqualApprox(stopped.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi / 2), "静止出生使用配置朝向");
        stopped.Advance(1.0 / 60);
        Check(stopped.Velocity == Vector2.Left && stopped.Speed == 0
            && Mathf.IsEqualApprox(stopped.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi), "负加速度从静止反向起动");
        world.Free();
    }
}
