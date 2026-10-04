using Godot;
using System;
using System.Linq;

/// <summary>验证输入适配、语义行为和视图接口的边界，不运行完整回归。</summary>
public partial class BattleVerification
{
    /// <summary>覆盖默认操作、控制器生命周期和只读HUD。</summary>
    private void VerifyInputLayers()
    {
        VerifyInput();
        // 原始输入解码不创建业务对象，方向重复允许而确认重复禁止。
        using var direction = new InputEventKey { Keycode = Key.Right, Pressed = true, Echo = true };
        using var confirm = new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = true };
        using var back = new InputEventKey { Keycode = Key.Escape, Pressed = true };
        using var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(8, 9) };
        Check(MenuInputRouter.Decode(direction) == MenuAction.Right, "方向长按继续导航");
        Check(MenuInputRouter.Decode(confirm) == MenuAction.None, "确认长按不重复开始");
        confirm.Echo = false;
        Check(MenuInputRouter.Decode(confirm) == MenuAction.Confirm, "确认首次按下有效");
        confirm.Pressed = false;
        Check(MenuInputRouter.Decode(confirm) == MenuAction.None, "确认释放不触发行为");
        Check(MenuInputRouter.Decode(back) == MenuAction.Back && MenuInputRouter.Decode(click) == MenuAction.None, "菜单返回仍仅来自现有键盘操作");
        Check(PointerInputAdapter.Decode(click) == new PointerInput(PointerAction.Press, new Vector2(8, 9)), "卡片和画布共用左键语义位置");
        click.Pressed = false;
        Check(PointerInputAdapter.Decode(click).Action == PointerAction.Release, "指针释放保持独立动作");
        click.ButtonIndex = MouseButton.Right;
        Check(PointerInputAdapter.Decode(click).Action == PointerAction.None, "没有新增右键手势");
        Check(PointerInputAdapter.Decode(back).Action == PointerAction.Cancel, "Esc仍取消画布手势");
        // 快捷键的修饰键、重复和Shift含义保持原约定。
        using var shortcut = new InputEventKey { Keycode = Key.S, CtrlPressed = true, Pressed = true };
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Save, "Ctrl+S保存");
        shortcut.ShiftPressed = true;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.SaveAs, "Ctrl+Shift+S另存");
        shortcut.Keycode = Key.Z;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Redo, "Ctrl+Shift+Z重做");
        shortcut.ShiftPressed = false;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Undo, "Ctrl+Z撤销");
        shortcut.Keycode = Key.Y;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Redo, "Ctrl+Y重做");
        shortcut.Keycode = Key.F;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Search, "Ctrl+F搜索");
        shortcut.Keycode = Key.O;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.Open, "Ctrl+O打开");
        shortcut.Echo = true;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.None, "快捷键长按不重复");
        shortcut.Echo = false; shortcut.CtrlPressed = false;
        Check(EditorInput.Shortcut(shortcut) == EditorAction.None, "普通字母不触发文档操作");
        // 替换为内存输入源仍能驱动行为，控制器不需要键盘或UI。
        var source = new ProbeBattleInput(); var target = new ProbeBattleActions();
        var controller = new BattleInputController(source, target);
        source.Frame = new BattleInputFrame(new Vector2(0.25f, 0), true, true, false, true, true);
        controller.WaitForConfirmRelease(); controller.Tick();
        Check(source.Reads == 1 && target.Steps == 1 && !target.Last.DodgePressed, "固定步只采样一次，确认保持时屏蔽闪避");
        Check(target.Last.Movement.X == 0.25f && target.Last.PreviousPhasePressed && target.Last.NextPhasePressed, "方向幅度和阶段同时按下交给战斗逻辑");
        source.Frame = source.Frame with { DodgeHeld = false, DodgePressed = false };
        controller.Tick();
        source.Frame = source.Frame with { DodgeHeld = true, DodgePressed = true };
        controller.Tick();
        Check(target.Last.DodgePressed, "释放门闩后新闪避请求生效");
        source.Frame = source.Frame with { RestartPressed = true };
        controller.Tick();
        Check(target.Restarts == 0 && target.Steps == 4, "战斗中R不改变原有行为");
        target.State = BattleState.Victory; controller.Tick();
        Check(target.Restarts == 1 && target.Steps == 4, "胜利后重开步不同时推进新战斗");
        target.IsStopped = true; int reads = source.Reads; controller.Tick();
        Check(source.Reads == reads && target.Restarts == 1, "离场不读取设备或执行行为");
        // 无Godot控件的视图替身验证网格规则、选择与确认严格分离。
        var view = new ProbeSelectionView();
        using var selection = new BossSelectionController(Enumerable.Range(0, 7).Select(index => "boss" + index), 4, view);
        int confirms = 0, aiChanges = 0; string selected = "";
        selection.Confirmed += _ => confirms++; selection.SelectionChanged += id => selected = id;
        selection.AISelectionChanged += _ => aiChanges++;
        selection.Restore("missing");
        Check(view.Index == 0 && selected == "boss0", "缺失选择恢复第一项");
        view.Select(3); selection.Handle(MenuAction.Right);
        Check(view.Index == 3 && confirms == 0, "右边缘不跨行，点选不确认");
        view.Navigate(Vector2I.Down);
        Check(view.Index == 6 && selected == "boss6", "末行不足时选最后项");
        view.Navigate(Vector2I.Up); view.Confirm(); view.SetAI(true);
        Check(view.Index == 2 && confirms == 1 && aiChanges == 1, "导航确认与AI选项各自分发");
        Check(!selection.Handle(MenuAction.Back), "选择场景不新增返回行为");
        selection.Dispose(); view.Select(0); view.Confirm(); view.SetAI(false);
        Check(view.Index == 2 && confirms == 1 && aiChanges == 1 && !selection.Handle(MenuAction.Confirm), "离场解绑后旧视图不能触发控制器");
        var emptyView = new ProbeSelectionView();
        using var empty = new BossSelectionController(Array.Empty<string>(), 4, emptyView);
        empty.Confirmed += _ => confirms++; empty.Restore(""); emptyView.Confirm();
        Check(empty.SelectedIndex == -1 && emptyView.Index == -1 && confirms == 1, "空目录没有确认目标");
        // 同一个语义返回入口可由操作层调用，但场景已退出时必须拒绝。
        bool active = true; int backs = 0;
        var menu = new BattleMenuController(() => active, () => backs++);
        Check(!menu.Handle(MenuAction.Confirm) && menu.Handle(MenuAction.Back) && backs == 1, "战斗菜单只接收返回");
        active = false;
        Check(!menu.Handle(MenuAction.Back) && backs == 1, "退出后返回请求不泄漏");
        // 显示层只消费值快照；刷新不能改变战斗时间、输入或随机状态。
        var battle = CreateBattle(out var world); var status = battle.CaptureStatus();
        var hud = new BattleHud(); hud.ShowStatus(status);
        Check(hud.Text.Contains("WASD / 方向键移动 · 空格闪避 · Q/E 切换阶段 · 自动攻击 · Esc 返回选择")
            && hud.Text.Contains("玩家 HP " + battle.Player.Health.Hp), "HUD保留原有操作提示与生命显示");
        VMath.setRandomSeed(515); double expected = VMath.getRandomDouble(0, 1); VMath.setRandomSeed(515);
        hud.ShowStatus(battle.CaptureStatus());
        Check(battle.CaptureStatus() == status && VMath.getRandomDouble(0, 1) == expected, "读取和显示快照不推进或消耗战斗状态");
        hud.ShowStatus(status with { AIHitCount = 5000000000L, State = BattleState.Victory });
        Check(hud.Text.Contains("AI 被击中次数：5000000000") && hud.Text.Contains("胜利！按 R 重新开始"), "HUD保留长整型AI次数与胜利提示");
        hud.Free(); world.Free();
    }

    /// <summary>可替换的内存输入源，记录固定步采样次数。</summary>
    private sealed class ProbeBattleInput : IBattleInputSource
    {
        /// <summary>下一次采样值。</summary>
        public BattleInputFrame Frame { get; set; }
        /// <summary>读取次数。</summary>
        public int Reads { get; private set; }
        /// <summary>返回固定快照，不读取真实输入。</summary>
        /// <returns>当前测试快照。</returns>
        public BattleInputFrame Read() { Reads++; return Frame; }
    }
    /// <summary>记录语义分发结果的独立战斗替身。</summary>
    private sealed class ProbeBattleActions : IBattleActions
    {
        /// <summary>可控离场状态。</summary>
        public bool IsStopped { get; set; }
        /// <summary>可控战斗状态。</summary>
        public BattleState State { get; set; } = BattleState.Running;
        /// <summary>执行过的固定步数量。</summary>
        public int Steps { get; private set; }
        /// <summary>重开次数。</summary>
        public int Restarts { get; private set; }
        /// <summary>最后提交的语义输入。</summary>
        public BattleInputFrame Last { get; private set; }
        /// <summary>仅记录请求，不创建真实战斗。</summary>
        public void Restart() => Restarts++;
        /// <summary>记录行为参数，不做设备判断。</summary>
        /// <param name="movement">移动方向。</param>
        /// <param name="dodgePressed">闪避请求。</param>
        /// <param name="previousPhasePressed">上一阶段请求。</param>
        /// <param name="nextPhasePressed">下一阶段请求。</param>
        public void StepFixed(Vector2 movement, bool dodgePressed, bool previousPhasePressed = false, bool nextPhasePressed = false)
        {
            Steps++; Last = new BattleInputFrame(movement, false, dodgePressed, false, previousPhasePressed, nextPhasePressed);
        }
    }
    /// <summary>无控件的视图接口替身，用于检查业务不依赖Godot界面。</summary>
    private sealed class ProbeSelectionView : IBossSelectionView
    {
        /// <summary>选择请求。</summary>
        public event Action<int>? SelectionRequested;
        /// <summary>导航请求。</summary>
        public event Action<Vector2I>? NavigationRequested;
        /// <summary>确认请求。</summary>
        public event Action? ConfirmationRequested;
        /// <summary>AI选择请求。</summary>
        public event Action<bool>? AISelectionRequested;
        /// <summary>最后显示下标。</summary>
        public int Index { get; private set; } = -1;
        /// <summary>接收显示结果。</summary>
        /// <param name="index">有效下标。</param>
        public void ShowSelection(int index) => Index = index;
        /// <summary>模拟视图选择请求。</summary>
        /// <param name="index">候选下标。</param>
        public void Select(int index) => SelectionRequested?.Invoke(index);
        /// <summary>模拟视图导航请求。</summary>
        /// <param name="direction">网格方向。</param>
        public void Navigate(Vector2I direction) => NavigationRequested?.Invoke(direction);
        /// <summary>模拟视图确认请求。</summary>
        public void Confirm() => ConfirmationRequested?.Invoke();
        /// <summary>模拟AI开关请求。</summary>
        /// <param name="enabled">开关值。</param>
        public void SetAI(bool enabled) => AISelectionRequested?.Invoke(enabled);
    }
}
