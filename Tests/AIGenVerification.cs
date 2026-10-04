using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>只验证20个AI生成Boss的目录、60个数据阶段、生命周期与确定性。</summary>
public partial class AIGenVerification : Node
{
    // 累计断言与所有阶段的实际峰值弹幕数。
    private int _checks, _peak;
    /// <summary>入树后延迟运行，避免测试期间改变正在创建的场景。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();

    /// <summary>检查条件并在失败时停止定向验证。</summary>
    /// <param name="condition">预期为真的条件。</param>
    /// <param name="message">失败位置和含义。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    /// <summary>等待场景切换与延迟释放完成。</summary>
    /// <returns>两个显示帧后的任务。</returns>
    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>验证正式选择入口、全部新阶段及返回选择时的状态。</summary>
    private async void Run()
    {
        try
        {
            // 正式目录必须保留前三项，随后按AI生成编号排列。
            var catalog = BossCatalog.Load();
            catalog.Validate();
            Check(catalog.Entries.Count == 23, "目录应有3个原Boss和20个AI生成Boss");
            for (int index = 0; index < 3; index++)
                Check(catalog.Entries[index].Id == $"Boss_{index + 1:00}", "前三项保持原Boss编号");
            var game = new GameManager { Catalog = catalog };
            AddChild(game);
            await Settle();
            if (OS.GetCmdlineUserArgs().Contains("--preview-aigen"))
            {
                await Preview(game);
                game.QueueFree();
                await Settle();
                GetTree().Quit();
                return;
            }
            // 首尾行、末行空位及自动滚动使用现有选择UI。
            var initial = (BossSelectStage)game.CurrentStage!;
            initial.UI.Select(19);
            initial.UI.Navigate(Vector2I.Down);
            Check(initial.UI.SelectedIndex == 22, "不完整末行选择最后一个Boss");
            initial.UI.Navigate(Vector2I.Up);
            Check(initial.UI.SelectedIndex == 18, "末行上移保持列");
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int number = 1; number <= 20; number++)
            {
                // 逐个通过正式选择界面进入，而非直接构造测试替身。
                int index = number + 2;
                var data = catalog.Entries[index];
                string id = $"Boss_AIGen_{number:00}";
                Check(data.Id == id && data.DisplayName == id && data.Phases.Count > 0, $"{id}名称和注册");
                Check(data.Texture!.ResourcePath == $"res://Assets/Units/Boss_{4 + (number - 1) % 12:00}.png"
                    && data.Hframes == 2 && data.Vframes == 2 && data.AnimationFps == 4, $"{id}复用已有四帧图集");
                var select = (BossSelectStage)game.CurrentStage!;
                Check(select.UI.FindChild($"BossItem{index}", true, false) is BossSelectItem, $"{id}选择卡片存在");
                select.UI.Select(index);
                select.UI.Confirm();
                await Settle();
                var battle = ((BattleStage)game.CurrentStage!).View.Battle;
                battle.SetPhysicsProcess(false);
                for (int phase = 1; phase <= 3; phase++)
                {
                    // 两次从完整重开状态进入同一阶段，输入和种子保持相同。
                    var first = Capture(battle, phase, false);
                    string name = battle.Boss.CurrentPhase!.Name;
                    Check(names.Add(name), $"{id}阶段{phase}名称必须唯一");
                    Check(first.SequenceEqual(Capture(battle, phase, true)), $"{id}阶段{phase}30秒逐步状态重现");
                    GD.Print($"AIGen {number:00}/{phase}: PASS, peak={_peak}, {name}");
                    // 给场景树机会回收本轮停止和重开排队释放的节点。
                    await Settle();
                }
                Check(!battle.Boss.TrySwitchAdjacentPhase(1), $"{id}恰有三个阶段");
                battle.Restart();
                battle.Player.Attack.Stop();
                battle.Boss.TakeDamage(100);
                Check(battle.Boss.Hp == 200 && battle.Boss.PhaseIndex == 1,
                    $"{id}自然切入第二阶段");
                battle.Boss.TakeDamage(100);
                Check(battle.Boss.Hp == 100 && battle.Boss.PhaseIndex == 2,
                    $"{id}自然切入第三阶段");
                battle.Boss.TakeDamage(100);
                battle.StepFixed(Vector2.Zero, false);
                Check(battle.State == BattleState.Victory && battle.Bullets.ActiveCount == 0
                    && battle.Timers.TimelineActionCount == 0, $"{id}击破后彻底清理");
                Check(game.RequestStage(GameManager.SelectStageId), $"{id}返回选择");
                await Settle();
                Check(game.CurrentStage is BossSelectStage restored && restored.UI.SelectedIndex == index,
                    $"{id}返回后恢复选中项");
            }
            Check(names.Count == 60, "60个阶段均有不同显示名称");
            game.QueueFree();
            await Settle();
            GD.Print($"PASS: {_checks} AIGen targeted assertions; 60 phases x 2 x 30 seconds; peak={_peak}/2048");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    /// <summary>从完整初态推进指定阶段，记录每半秒状态并检查容量和清场。</summary>
    /// <param name="battle">已暂停自动物理更新的正式战斗。</param>
    /// <param name="phase">1至3的阶段编号。</param>
    /// <param name="refresh">是否额外刷新激光几何，验证显示不改变业务状态。</param>
    /// <returns>按固定步顺序排列的状态字符串及后续随机值。</returns>
    private List<string> Capture(BattleManager battle, int phase, bool refresh)
    {
        battle.Restart();
        battle.Player.Attack.Stop();
        for (int step = 1; step < phase; step++)
            Check(battle.Boss.TrySwitchAdjacentPhase(1), "能够切入指定阶段");
        Check(battle.Boss.Hp == 400 - phase * 100, "直接切入恢复300/200/100血线");
        // 单个数据发射器管理阶段全部子树；JSON名称直接对应显示名称。
        var current = battle.Boss.CurrentPhase!;
        Check(current.Emitters.Count == 1 && current.Emitters[0].Core.Name == current.Name, "阶段显示名称与JSON一致");
        var emitter = current.Emitters[0];
        var states = new List<string>();
        int peak = 0;
        for (int tick = 1; tick <= 1800; tick++)
        {
            // 固定输入交替左右移动，每两秒尝试一次闪避。
            battle.StepFixed(tick % 360 < 180 ? Vector2.Left : Vector2.Right, tick % 120 == 0);
            peak = Math.Max(peak, battle.Bullets.ActiveCount);
            if (refresh)
                foreach (var laser in battle.Bullets.ActiveBullets.OfType<VLaser>()) laser.RefreshGeometry();
            Check(battle.Bullets.ActiveCount < 1800, "弹幕必须留有玩家弹容量余量");
            if (tick % 30 == 0)
            {
                Check(battle.Bullets.ActiveBullets.All(bullet => bullet.WorldPosition.IsFinite()
                    && bullet.Velocity.IsFinite()), "全部实际轨迹保持有限坐标");
                states.Add($"{battle.Player.Health.Hp}:{battle.Player.Position}:{battle.Boss.Position}|"
                    + string.Join(";", battle.Bullets.ActiveBullets.Select(bullet =>
                        $"{bullet.GetType().Name}:{bullet.WorldPosition.X:R},{bullet.WorldPosition.Y:R}:{bullet.Velocity.X:R},{bullet.Velocity.Y:R}:{bullet.Angle:R}:{bullet.Age:R}"))
                    + "|" + string.Join(";", emitter.Nodes.Select(point =>
                        $"{point.WorldPosition.X:R},{point.WorldPosition.Y:R}:{point.Age:R}")));
            }
        }
        Check(peak >= 5 && emitter.Timeline is not null, "阶段在30秒内实际持续发射弹幕");
        _peak = Math.Max(_peak, peak);
        states.Add(VMath.getRandomDouble(0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        // 离开已有弹幕的阶段时纯节点、子弹和发射器必须全部结束。
        Check(battle.Boss.TrySwitchAdjacentPhase(phase == 3 ? -1 : 1), "有活动弹幕时允许切阶段");
        Check(emitter.Timeline is null && emitter.Nodes.Count == 0 && emitter.Bullets.Count == 0
            && battle.Bullets.ActiveCount == 0, "切阶段清除旧发射器和全部弹幕");
        Check(battle.Boss.TrySwitchAdjacentPhase(phase == 3 ? 1 : -1), "恢复被验证阶段");
        return states;
    }

    /// <summary>用真实渲染器保存选择界面和代表性阶段截图供人工检查。</summary>
    /// <param name="game">已进入选择界面的正式游戏管理器。</param>
    /// <returns>截图保存完成后的任务。</returns>
    private async Task Preview(GameManager game)
    {
        // 截图仅存放在被Git忽略的工具目录，不创建游戏美术资源。
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://.tools/aigen-previews"));
        await SavePreview("selection-first");
        ((BossSelectStage)game.CurrentStage!).UI.Select(22);
        await Settle();
        await SavePreview("selection-last");
        // 覆盖普通弹、停时弹、曲线、固定激光、移动激光与最后一个Boss。
        foreach (var sample in new[] { (3, 3, 180), (5, 2, 165), (11, 3, 145), (14, 3, 230), (19, 2, 240), (20, 3, 210) })
        {
            Check(game.RequestStage(GameManager.BattleStageId, $"Boss_AIGen_{sample.Item1:00}"), "预览Boss可进入");
            await Settle();
            var battle = ((BattleStage)game.CurrentStage!).View.Battle;
            battle.SetPhysicsProcess(false);
            battle.Restart();
            battle.Player.Attack.Stop();
            for (int phase = 1; phase < sample.Item2; phase++) battle.Boss.TrySwitchAdjacentPhase(1);
            for (int tick = 0; tick < sample.Item3; tick++) battle.StepFixed(Vector2.Zero, false);
            await Settle();
            await SavePreview($"boss-{sample.Item1:00}-phase-{sample.Item2:00}");
            game.RequestStage(GameManager.SelectStageId);
            await Settle();
        }
    }

    /// <summary>等待本帧绘制完成并保存PNG。</summary>
    /// <param name="name">不带扩展名的截图文件名。</param>
    /// <returns>文件保存完成后的任务。</returns>
    private async Task SavePreview(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath($"res://.tools/aigen-previews/{name}.png")) == Error.Ok, "保存预览截图");
    }
}
