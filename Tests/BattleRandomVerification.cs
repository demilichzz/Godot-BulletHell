using Godot;
using System;
using System.IO;
using System.Linq;

/// <summary>验证随机状态随战斗上下文隔离，初始化失败和其他战斗不改变本场轨迹。</summary>
public partial class BattleVerification
{
    /// <summary>覆盖异常初始化、嵌套上下文、重开和交错推进。</summary>
    private void VerifyBattleRandomIsolation()
    {
        // 独立工具流作为期望值，不消费被测战斗的状态。
        var battle = CreateBattle(out var world);
        var expected = VMath.CreateRandomStream(37);
        VMath.setRandomSeed(37);
        Check(VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue), "本场随机起点");
        // 在Boss创建后由AI初始化抛错，覆盖随机已重置但战斗尚未提交的窗口。
        var failedWorld = new Node2D(); AddChild(failedWorld);
        var failed = new BattleManager(); failedWorld.AddChild(failed);
        bool rejected = false;
        try
        {
            failed.Initialize(failedWorld, aiConfig: new AICharConfig
            {
                Enabled = true, StrategyFactory = () => throw new InvalidOperationException("预期的初始化失败")
            });
        }
        catch (InvalidOperationException error) when (error.Message == "预期的初始化失败") { rejected = true; }
        try
        {
            Check(rejected && ReferenceEquals(GlobalEvent.GetBoss(), battle.Boss), "失败初始化恢复旧战斗绑定");
            Check(VMath.randomSeed == 37 && VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue),
                "失败初始化不重置旧战斗随机");
            // 嵌套临时绑定与重开只改变第二场状态，退出后回到外层随机。
            var other = CreateBattle(out var otherWorld);
            try
            {
                using (GlobalEvent.UseBattle(battle))
                {
                    using (GlobalEvent.UseBattle(other)) { VMath.setRandomSeed(91); VMath.getRandomDouble(0, 1); }
                    Check(VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue), "嵌套上下文恢复原随机游标");
                    other.Restart();
                    Check(VMath.randomSeed == 37 && VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue),
                        "另一战斗重开不改变外层状态");
                }
                Check(ReferenceEquals(GlobalEvent.GetBoss(), other.Boss) && VMath.randomSeed == 0, "退出临时上下文回到持久绑定");
            }
            finally { otherWorld.Free(); }
        }
        finally { failedWorld.Free(); world.Free(); }
        // 全程逐步记录主战斗；干扰战斗使用相同输入并额外重开。
        byte[] alone = CaptureRandomBattle(false), interleaved = CaptureRandomBattle(true);
        Check(alone.SequenceEqual(interleaved), "720步交错战斗与单独运行的主战斗轨迹及随机后继一致");
    }

    /// <summary>记录主战斗12秒的移动、弹幕次序和下一随机值。</summary>
    /// <param name="interleave">是否在每步之间推进并重开另一战斗。</param>
    /// <returns>精确浮点值和随机后继组成的二进制轨迹。</returns>
    private byte[] CaptureRandomBattle(bool interleave)
    {
        // 两个世界共享进程但不共享战斗状态，自动物理更新均关闭。
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        Node2D? otherWorld = null;
        BattleManager? other = null;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        try
        {
            if (interleave) { other = CreateBattle(out otherWorld); other.Player.Attack.Stop(); }
            // 固定720步跨越两次随机移动选点，额外记录保持原顺序的弹幕状态。
            for (int frame = 0; frame < 720; frame++)
            {
                if (frame == 420 && other is not null) { other.Restart(); other.Player.Attack.Stop(); }
                other?.StepFixed(Vector2.Right, false);
                battle.StepFixed(Vector2.Zero, false);
                WriteVector(writer, battle.Boss.Position);
                WriteVector(writer, battle.Boss.CurrentPhase!.MoveTarget);
                writer.Write(battle.Bullets.ActiveCount);
                foreach (var bullet in battle.Bullets.ActiveBullets)
                {
                    WriteVector(writer, bullet.WorldPosition); writer.Write(bullet.Angle); writer.Write(bullet.Age);
                }
            }
            using (GlobalEvent.UseBattle(battle)) writer.Write(VMath.getRandomInt(int.MinValue, int.MaxValue));
            writer.Flush();
            return buffer.ToArray();
        }
        finally { otherWorld?.Free(); world.Free(); }
    }
}
