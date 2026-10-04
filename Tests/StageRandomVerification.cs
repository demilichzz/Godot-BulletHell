using Godot;
using System;
using System.Threading.Tasks;

/// <summary>通过实际延迟场景切换验证随机上下文的异常回滚。</summary>
public partial class StageVerification
{
    /// <summary>新场景已绑定战斗后抛错，旧场景与随机游标必须一起恢复。</summary>
    /// <returns>场景切换及清理完成后的任务。</returns>
    private async Task VerifyRandomRollback()
    {
        // 无战斗时的工具序列不应受任何场景初始化影响。
        VMath.setRandomSeed(73);
        var toolExpected = VMath.CreateRandomStream(73);
        Check(VMath.getRandomInt(0, int.MaxValue) == toolExpected.GetRandomInt(0, int.MaxValue), "工具流起点");
        var game = new GameManager(); AddChild(game);
        try
        {
            await Settle();
            Check(game.RequestStage(GameManager.BattleStageId), "进入被测战斗");
            await Settle();
            // 冻结自动推进，明确控制失败窗口前后的随机消耗。
            var previous = (BattleStage)game.CurrentStage!;
            var battle = previous.View.Battle;
            battle.SetPhysicsProcess(false);
            VMath.setRandomSeed(31);
            var expected = VMath.CreateRandomStream(31);
            Check(VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue), "旧战斗先消费一个样本");
            game.RegisterStage("random_failure", () => new RandomFailureStage());
            Check(game.RequestStage("random_failure"), "排队故障场景");
            await Settle();
            Check(ReferenceEquals(game.CurrentStage, previous) && game.StageHost.GetChildCount() == 1,
                "失败后保留旧场景并移除新场景");
            Check(game.LastError == "预期的场景进入失败" && previous.ProcessMode == ProcessModeEnum.Inherit,
                "确认命中实际回滚分支并恢复旧场景处理");
            Check(ReferenceEquals(GlobalEvent.GetBoss(), battle.Boss) && VMath.randomSeed == 31,
                "服务与随机恢复到同一旧战斗");
            Check(VMath.getRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue),
                "失败场景的重置与抽样不改变旧随机游标");
        }
        finally { game.Free(); }
        Check(VMath.randomSeed == 73 && VMath.getRandomInt(0, int.MaxValue) == toolExpected.GetRandomInt(0, int.MaxValue),
            "全部离场后工具流仍从原位置继续");
    }

    /// <summary>测试专用故障场景，在新战斗提交全局绑定后抛出可识别异常。</summary>
    private partial class RandomFailureStage : Stage
    {
        // 本故障场景独占的战斗，由正常退出流程清理。
        private readonly BattleManager _battle = new();
        /// <summary>测试场景注册标识。</summary>
        public override string StageId => "random_failure";
        /// <summary>创建并启动战斗、消费随机后失败。</summary>
        /// <param name="context">正式场景管理器提供的进入参数。</param>
        protected override void OnEnter(StageContext context)
        {
            // 宿主属于当前场景，失败销毁时一并释放全部节点。
            var world = new Node2D(); AddChild(world); world.AddChild(_battle);
            _battle.Initialize(world, context.Game.Catalog.Entries[0]);
            _battle.SetPhysicsProcess(false);
            VMath.setRandomSeed(999);
            VMath.getRandomDouble(0, 1);
            throw new InvalidOperationException("预期的场景进入失败");
        }
        /// <summary>沿用正式战斗离场清理，不直接操作随机状态。</summary>
        protected override void OnExit() => _battle.StopBattle();
    }
}
