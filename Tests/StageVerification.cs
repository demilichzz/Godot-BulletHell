using Godot;
using System;
using System.Threading.Tasks;
using System.Linq;

/// <summary>验证场景切换、独立 Boss 配置和选择界面的集成行为。</summary>
public partial class StageVerification : Node
{
    // 已通过的断言数量。
    private int _checks;
    /// <summary>场景就绪后运行异步集成验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>检查条件，失败时给出具体说明。</summary>
    /// <param name="condition">应为真的条件。</param>
    /// <param name="message">失败说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    /// <summary>等待延迟场景切换与节点释放完成。</summary>
    /// <returns>两个渲染帧后的任务。</returns>
    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    /// <summary>执行集成测试并使用退出码报告结果。</summary>
    private async void Run()
    {
        try
        {
            if (Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--targeted-new-bosses"))
            {
                await VerifyNewBosses();
                GD.Print($"PASS: {_checks} targeted new boss assertions");
                GetTree().Quit();
                return;
            }
            // 正式目录使用四帧图集，每帧仍为64像素。
            var production = BossCatalog.Load();
            production.Validate();
            Check(production.Entries.Count == 23, "正式目录包含3个原Boss和20个AI生成Boss");
            for (int index = 0; index < 3; index++)
                Check(production.Entries[index].Id == $"Boss_0{index + 1}"
                    && production.Entries[index].Phases.Count > 0,
                    "正式Boss目录按编号排序且阶段配置匹配");
            Check(production.Entries[0].Texture!.ResourcePath == "res://Assets/Units/Boss_01.png"
                && production.Entries[0].Texture!.GetSize() == new Vector2(128, 128), "正式Boss四帧图集");
            VerifyAnimation(production.Entries[0]);
            Check(ResourceUid.GetIdPath(ResourceUid.TextToId("uid://7iuuwxiu5ji")) == "res://Assets/Units/Boss_01.png"
                && ResourceUid.GetIdPath(ResourceUid.TextToId("uid://cyx82pr2q67tt")) == "res://Assets/Units/Boss_05.png", "图片资源身份保留");
            // 使用七个仅测试可见的配置，覆盖多行和不完整末行。
            var entries = new System.Collections.Generic.List<BossData>();
            for (int index = 0; index < 7; index++)
                entries.Add(new BossData
                {
                    Id = $"test_{index}", DisplayName = $"测试 Boss {index}",
                    Texture = GD.Load<Texture2D>("res://Assets/Units/Boss_01.png"),
                    Hframes = 2, Vframes = 2, AnimationFps = 4,
                    MaxHp = 100 + index * 10, CollisionRadius = 32 + index,
                    VisualScale = 2 + index * 0.1f, SpawnPosition = new Vector2(600 + index, 250)
                });
            var catalog = new BossCatalog(entries);
            catalog.Validate();
            var game = new GameManager { Catalog = catalog };
            AddChild(game);
            await Settle();
            Check(game.CurrentStage is BossSelectStage, "启动进入选择场景");
            Check(game.StageHost.GetChildCount() == 1, "唯一Stage节点");
            var select = (BossSelectStage)game.CurrentStage!;
            Check(select.UI.SelectedIndex == 0 && game.SelectedBossId == "test_0", "初始选中第一项");
            select.UI.Navigate(Vector2I.Left);
            Check(select.UI.SelectedIndex == 0, "左边缘不环绕");
            select.UI.Select(3);
            select.UI.Navigate(Vector2I.Right);
            Check(select.UI.SelectedIndex == 3, "右边缘不跨行");
            select.UI.Navigate(Vector2I.Down);
            Check(select.UI.SelectedIndex == 6, "末行缺项选择最后一项");
            select.UI.Navigate(Vector2I.Up);
            Check(select.UI.SelectedIndex == 2, "向上保持列");
            // 发送实际卡片鼠标事件，必须只选中而不切换。
            var item = select.UI.FindChild("BossItem5", true, false) as BossSelectItem;
            // 通过视口分发真实命中位置，验证控件层级没有截获卡片点击。
            var clickPosition = item!.GetGlobalRect().GetCenter();
            using var motion = new InputEventMouseMotion { Position = clickPosition, GlobalPosition = clickPosition };
            GetViewport().PushInput(motion, true);
            using var mouse = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = clickPosition, GlobalPosition = clickPosition };
            GetViewport().PushInput(mouse, true);
            using var mouseRelease = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = clickPosition, GlobalPosition = clickPosition };
            GetViewport().PushInput(mouseRelease, true);
            Check(select.UI.SelectedIndex == 5 && game.CurrentStage == select && !game.IsTransitioning, "点击仅选择");
            using var right = new InputEventKey { Keycode = Key.Right, Pressed = true };
            GetViewport().PushInput(right, true);
            Check(select.UI.SelectedIndex == 6, "实际方向键事件");
            using var echo = new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = true };
            GetViewport().PushInput(echo, true);
            Check(!game.IsTransitioning, "长按确认事件不触发切换");
            select.UI.Select(5);
            // 持续按住确认进入战斗，验证确认键隔离。
            Input.ActionPress("player_dodge");
            using var confirm = new InputEventKey { Keycode = Key.Space, Pressed = true };
            GetViewport().PushInput(confirm, true);
            Check(game.IsTransitioning && game.CurrentStage == select, "切换请求延迟执行");
            Check(!game.RequestStage(GameManager.BattleStageId, "test_0"), "重复切换被拒绝");
            await Settle();
            var battleStage = (BattleStage)game.CurrentStage!;
            var battle = battleStage.View.Battle;
            battle.SetPhysicsProcess(false);
            Check(game.StageHost.GetChildCount() == 1 && !GodotObject.IsInstanceValid(select), "旧Stage释放");
            Check(battle.Boss.MaxHp == 150 && battle.Boss.Hp == 150 && battle.Boss.CollisionRadius == 37, "独立生命与判定参数");
            Check(battle.Boss.Position == new Vector2(605, 250), "独立出生位置");
            Check(battle.Boss.GetNode<Sprite2D>("Sprite").Scale == Vector2.One * 2.5f, "独立贴图倍率");
            battle._PhysicsProcess(1.0 / 60);
            Check(battle.Player.Dodge.Cooldown == 0, "确认空格不触发闪避");
            Input.ActionRelease("player_dodge");
            battle.SetPhysicsProcess(true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionPress("player_dodge");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            battle.SetPhysicsProcess(false);
            Check(battle.Player.Dodge.Cooldown > 0, "释放后重新按下可以闪避");
            Input.ActionRelease("player_dodge");
            // Boss 在子弹终点外37像素判定范围内，独立半径应参与碰撞。
            battle.Bullets.Clear();
            battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with { Position = battle.Boss.GlobalPosition + new Vector2(39, 0) })!.SetSpeed(0);
            battle.StepFixed( Vector2.Zero, false);
            Check(battle.Boss.Hp == 149, "碰撞使用独立Boss半径");
            var originalBoss = battle.Boss;
            var originalPhase = battle.Boss.CurrentPhase;
            battle.Restart();
            Check(battle.Boss != originalBoss && battle.Boss.CurrentPhase != originalPhase && battle.Boss.Hp == 150, "重开同配置新实例");
            battle.Boss.TakeDamage(150);
            battle.StepFixed( Vector2.Zero, false);
            Check(battle.State == BattleState.Victory, "配置Boss击败结算");
            using var back = new InputEventKey { Keycode = Key.Escape, Pressed = true };
            GetViewport().PushInput(back, true);
            await Settle();
            Check(game.CurrentStage is BossSelectStage restored && restored.UI.SelectedIndex == 5, "Esc返回并恢复选择");
            Check(!GodotObject.IsInstanceValid(battleStage), "战斗Stage与子节点释放");
            // 无效请求不能清除现有场景。
            var stable = game.CurrentStage;
            Check(!game.RequestStage("missing") && game.CurrentStage == stable, "未知场景保留当前界面");
            Check(!game.RequestStage(GameManager.BattleStageId, "missing") && game.CurrentStage == stable, "未知Boss保留当前界面");
            // 扩展Stage不修改GameManager判断逻辑，生命周期必须仅执行一次。
            var probe = new ProbeStage();
            game.RegisterStage("probe", () => probe);
            Check(game.RequestStage("probe"), "注册扩展Stage");
            await Settle();
            Check(game.CurrentStage == probe && probe.Enters == 1, "扩展Stage进入一次");
            game.RequestStage(GameManager.SelectStageId);
            await Settle();
            Check(probe.Exits == 1, "扩展Stage退出一次");
            // 循环切换，确认宿主不积累节点且战斗始终重新开始。
            for (int round = 0; round < 3; round++)
            {
                game.RequestStage(GameManager.BattleStageId);
                await Settle();
                Check(((BattleStage)game.CurrentStage!).View.Battle.Boss.Hp == 150, "多轮进入生命恢复");
                game.RequestStage(GameManager.SelectStageId);
                await Settle();
                Check(game.StageHost.GetChildCount() == 1, "多轮切换不残留");
            }
            game.QueueFree();
            await Settle();
            // 空目录仍可显示界面，但无法进入战斗。
            var empty = new GameManager { Catalog = new BossCatalog() };
            AddChild(empty);
            await Settle();
            Check(((BossSelectStage)empty.CurrentStage!).UI.SelectedIndex == -1, "空目录无选择");
            Check(!empty.RequestStage(GameManager.BattleStageId), "空目录无法确认");
            empty.QueueFree();
            await Settle();
            GD.Print($"PASS: {_checks} stage integration assertions");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Input.ActionRelease("player_dodge");
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
    /// <summary>验证新增Boss的正式配置、选择入口及各自阶段弹幕。</summary>
    /// <returns>全部场景切换完成后的任务。</returns>
    private async Task VerifyNewBosses()
    {
        var catalog = BossCatalog.Load();
        catalog.Validate();
        Check(catalog.Entries.Count == 23, "正式选择目录包含3个原Boss和20个AI生成Boss");
        var game = new GameManager { Catalog = catalog };
        AddChild(game);
        await Settle();
        for (int index = 1; index <= 2; index++)
        {
            // 逐项验证资源、卡片和进入战斗后的首个阶段与对应发射器。
            var data = catalog.Entries[index];
            string id = $"Boss_0{index + 1}";
            Check(data.Id == id && data.Phases.Count > 0 && data.DisplayName == $"Boss 0{index + 1}",
                "新增Boss配置与目录顺序匹配");
            Check(data.Texture!.ResourcePath == $"res://Assets/Units/{id}.png"
                && data.Texture.GetSize() == new Vector2(128, 128)
                && data.Hframes == 2 && data.Vframes == 2 && data.AnimationFps == 4,
                "新增Boss使用对应四帧图集");
            Check(data.GetSelectionTexture() is AtlasTexture atlas
                && atlas.Region == new Rect2(0, 0, 64, 64), "新增Boss选择卡片显示首帧");
            var select = (BossSelectStage)game.CurrentStage!;
            Check(select.UI.FindChild($"BossItem{index}", true, false) is BossSelectItem,
                "新增Boss有选择卡片");
            select.UI.Select(index);
            select.UI.Confirm();
            await Settle();
            var battle = ((BattleStage)game.CurrentStage!).View.Battle;
            battle.SetPhysicsProcess(false);
            // 清除等待UI帧期间推进的时间，从完整相同初始状态检查发射时刻。
            battle.Restart();
            battle.Player.Attack.Stop();
            BossPhase phase = battle.Boss.CurrentPhase!;
            Check(battle.Boss.DisplayName == data.DisplayName && battle.Boss.Hp == 300
                && phase is DataBossPhase { Index: 0 }
                && (id == "Boss_03" || !battle.Boss.TrySwitchAdjacentPhase(1)), "新Boss进入对应基础阶段");
            VBulletEmitter emitter = phase.Emitters[0];
            Check(phase.Emitters.Count == data.Phases[0].Emitters.Count, "每个新阶段绑定对应发射器");
            VerificationClock.BossSeconds(battle, 2);
            Check(battle.Boss.Position == new Vector2(640, 240), "阶段沿用基类移动");
            if (id == "Boss_02")
            {
                // 第一条曲线在1000ms开始，到2000ms有21颗，第二条恰好出生首颗。
                var path = emitter.Root.Children[0];
                Check(path is VPathCreator && path.TotalAmount == 50 && path.Batches.Count == 2
                    && path.Batches[0].Count == 50 && path.Batches[1].Count == 50,
                    "Boss02每秒以独立随机VNode为父生成50点曲线");
                Check(emitter.Bullets.Count == 22 && battle.Bullets.ActiveCount == 22, "每条曲线按50ms间隔逐颗生成Dot子弹");
                Check(emitter.Root.Members[0].WorldPosition.X != emitter.Root.Members[1].WorldPosition.X,
                    "不同曲线使用新的上方扇区随机位置");
                foreach (var batch in path.Batches)
                {
                    Check((batch[0].WorldPosition + batch[^1].WorldPosition).DistanceTo(new Vector2(1280, 800)) < 0.001
                        && Math.Abs(batch[0].WorldPosition.DistanceTo(new Vector2(640, 400)) - 400) < 0.001,
                        "曲线两端在半径400圆弧上且关于中心对称");
                    foreach (var node in batch)
                        Check(node.WorldPosition.X >= 0 && node.WorldPosition.X <= 1280
                            && node.WorldPosition.Y >= 0 && node.WorldPosition.Y <= 800, "曲线节点保持在屏幕范围内");
                }
                // 隔离时钟只推进子弹时间线；直接读取整数时间单位验证50ms间隔。
                for (int bulletIndex = 0; bulletIndex < 21; bulletIndex++)
                    Check(emitter.Bullets[bulletIndex].Timeline!.ElapsedUnits == 60000 - bulletIndex * 3000,
                        "曲线每颗实际出生时间严格相隔50ms");
                Check(emitter.Bullets[21].Timeline!.ElapsedUnits == 0, "下一条曲线恰在下一秒开始发射");
                // 已发子弹采用Snapshot，父节点消失后仍按自己的3秒寿命存在。
                var firstBullet = emitter.Bullets[0];
                var firstPosition = firstBullet.WorldPosition;
                var sprite = firstBullet.GetNode<Sprite2D>("Sprite");
                Check(sprite.Texture!.ResourcePath == "res://Assets/Sprites/Sprite_dot.png" && sprite.Frame == 0
                    && firstBullet.Speed == 0 && firstBullet.LifeTimeMs == 3000, "使用Dot索引0、零速和3000ms寿命");
                VerificationClock.BossSeconds(battle, 1.45);
                Check(emitter.Bullets.Count == 90 && emitter.Bullets.Any(bullet => ReferenceEquals(bullet.ParentVNode, path.Batches[0][49]) && bullet.WorldPosition == path.Batches[0][49].WorldPosition),
                    "2450ms内补齐第一条50颗曲线且后续曲线并行生成");
                VerificationClock.BossSeconds(battle, 0.05);
                Check(firstBullet.IsAlive && firstBullet.ParentVNode is null && firstBullet.WorldPosition == firstPosition,
                    "2500ms锚点结束不提前清除已发子弹");
                // 隔离时钟已累计2.5秒年龄，只补足到出生后2999ms。
                firstBullet.Advance(0.499);
                Check(!firstBullet.Expired && firstBullet.WorldPosition == firstPosition, "静止子弹出生后不足3秒不失效");
                firstBullet.Advance(0.001);
                Check(firstBullet.Expired, "静止子弹出生满3秒到期");
            }
            else if (id == "Boss_03")
                Check(emitter.Bullets.OfType<VLaser>().Count() == 24 && emitter.Bullets.Count > 24,
                    "Boss03首轮发射24条预警激光并叠加圆弹");
            else
                Check(battle.Bullets.ActiveCount == 0 && emitter.Bullets.Count == 0
                    && battle.Timers.TimelineActionCount == 1, "其他未定义动作的空发射器不产生弹幕");
            Check(game.RequestStage(GameManager.SelectStageId), "可返回选择界面");
            await Settle();
            Check(game.CurrentStage is BossSelectStage restored && restored.UI.SelectedIndex == index,
                "返回后恢复新增Boss选中项");
        }
        game.QueueFree();
        await Settle();
    }
    /// <summary>验证图集播放、结束冻结、重开复位及静态选择图片。</summary>
    /// <param name="data">正式Boss配置，使用2×2图集与每秒4帧。</param>
    private void VerifyAnimation(BossData data)
    {
        // 独立战场禁止自动更新，以精确注入动画时间。
        var world = new Node2D();
        AddChild(world);
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world, data);
        battle.SetPhysicsProcess(false);
        var sprite = battle.Boss.GetNode<Sprite2D>("Sprite");
        Check(sprite.Hframes == 2 && sprite.Vframes == 2 && sprite.Frame == 0
            && sprite.GetRect().Size == new Vector2(64, 64), "动画初始帧与单帧尺寸");
        battle.Boss.Advance(0.24);
        Check(sprite.Frame == 0, "换帧前保持首帧");
        battle.Boss.Advance(0.01);
        Check(sprite.Frame == 1, "四分之一秒第二帧");
        battle.Boss.Advance(0.25);
        Check(sprite.Frame == 2, "半秒第三帧");
        battle.Boss.Advance(0.25);
        Check(sprite.Frame == 3, "四分之三秒第四帧");
        battle.Boss.Advance(0.25);
        Check(sprite.Frame == 0, "一秒循环复位");
        battle.Boss.Advance(2.75);
        Check(sprite.Frame == 3, "大时间步保留余量");
        battle.Boss.Stop();
        battle.Boss.Advance(0.5);
        Check(sprite.Frame == 3, "结束冻结动画");
        battle.Restart();
        sprite = battle.Boss.GetNode<Sprite2D>("Sprite");
        Check(sprite.Frame == 0, "重开首帧");
        // 六十次固定物理步仍应恰好循环一次。
        for (int tick = 0; tick < 60; tick++) battle.Boss.Advance(1.0 / 60);
        Check(sprite.Frame == 0, "60Hz动画循环");
        // 卡片只持有静态区域，不受战斗更新影响。
        var card = new BossSelectItem();
        card.Initialize(data, 0);
        var preview = card.GetChild<VBoxContainer>(0).GetChild<TextureRect>(0);
        Check(preview.Texture is AtlasTexture atlas && atlas.Atlas == data.Texture
            && atlas.Region == new Rect2(0, 0, 64, 64), "选择卡片仅首帧");
        battle.Boss.Advance(0.25);
        Check(preview.Texture.GetSize() == new Vector2(64, 64), "动画不改变选择图片");
        card.Free();
        // 单帧配置保持静态，独立肖像仍优先。
        var single = new BossData { Texture = data.GetSelectionTexture() };
        single.Validate();
        Check(single.GetSelectionTexture() == single.Texture, "单帧选择贴图兼容");
        battle.StopBattle();
        var staticBoss = BossFactory.Create(single);
        world.AddChild(staticBoss);
        staticBoss.Advance(0.75);
        Check(staticBoss.GetNode<Sprite2D>("Sprite").Frame == 0, "单帧战斗静态");
        single.Portrait = GD.Load<Texture2D>("res://Assets/Units/Boss_05.png");
        Check(single.GetSelectionTexture() == single.Portrait, "独立肖像优先");
        // 无效行列、不可整除尺寸以及非法帧率必须被拒绝。
        var invalid = (BossData)data.Duplicate();
        invalid.Hframes = 3;
        CheckInvalidAnimation(invalid);
        invalid.Hframes = 0;
        CheckInvalidAnimation(invalid);
        invalid.Hframes = 2;
        invalid.AnimationFps = 0;
        CheckInvalidAnimation(invalid);
        invalid.AnimationFps = double.NaN;
        CheckInvalidAnimation(invalid);
        world.Free();
    }
    /// <summary>确认非法动画配置抛出参数异常。</summary>
    /// <param name="data">预期验证失败的配置。</param>
    private void CheckInvalidAnimation(BossData data)
    {
        // 捕获结果用于区分正确拒绝与静默接受。
        bool rejected = false;
        try { data.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "拒绝非法动画配置");
    }
    /// <summary>验证注册扩展和生命周期次数的测试场景。</summary>
    private partial class ProbeStage : Stage
    {
        // 生命周期累计调用次数，节点释放后保留托管计数用于断言。
        public int Enters, Exits;
        /// <summary>测试场景标识。</summary>
        public override string StageId => "probe";
        /// <summary>记录进入。</summary>
        /// <param name="context">进入参数。</param>
        protected override void OnEnter(StageContext context) => Enters++;
        /// <summary>记录退出。</summary>
        protected override void OnExit() => Exits++;
    }
}
