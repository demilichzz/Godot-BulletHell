using Godot;
using System;
using System.Linq;

/// <summary>Boss03前两个阶段的独立实机预览；关闭玩家射击以完整观察循环，Space暂停。</summary>
public partial class Boss03Preview : Node2D
{
    // 预览仍使用唯一BattleManager的固定步，关闭默认输入与自动攻击。
    private BattleManager _battle = null!;
    private readonly Label _status = new() { Position = new Vector2(160, 32) };
    private bool _capture, _paused;
    // 命令行选择第二阶段，仅影响预览入口。
    private bool _phase02;

    /// <summary>加载正式Boss03数据和阶段，用于观察激光网与圆弹。</summary>
    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().Contains("--boss03-capture");
        _phase02 = OS.GetCmdlineUserArgs().Contains("--boss03-phase02");
        // 使用正式战场同一张浅色背景，避免只在黑底检查加算颜色。
        AddChild(new TextureRect
        {
            Position = BattleConfig.Bounds.Position,
            Size = BattleConfig.Bounds.Size,
            Texture = GD.Load<Texture2D>("res://Assets/UI/UI_400x600_gamearea_4.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = -1
        });
        _battle = new BattleManager();
        AddChild(_battle);
        _battle.Initialize(this, GD.Load<BossData>("res://Data/Bosses/Boss_03.tres"));
        _battle.SetPhysicsProcess(false);
        _battle.Player.Attack.Stop();
        if (_phase02) _battle.Boss.TrySwitchAdjacentPhase(1);
        // 独立HUD画布避免被激光亮芯覆盖。
        var hud = new CanvasLayer();
        AddChild(hud);
        hud.AddChild(_status);
        hud.AddChild(new Label { Text = "Space: pause / resume", Position = new Vector2(160, 750) });
        UpdateStatus();
        if (_capture) Callable.From(Capture).CallDeferred();
    }

    /// <summary>在每次物理回调推进一个正式60Hz步。</summary>
    /// <param name="delta">Godot物理秒数；战斗使用固定步，不直接消费此值。</param>
    public override void _PhysicsProcess(double delta)
    {
        if (_capture || _paused) return;
        _battle.StepFixed(Vector2.Zero, false);
        UpdateStatus();
    }

    /// <summary>处理预览暂停，不改变任何战斗参数。</summary>
    /// <param name="input">Godot输入事件。</param>
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space }) _paused = !_paused;
    }

    /// <summary>显示战斗时刻及当前激光阶段。</summary>
    private void UpdateStatus()
    {
        // 从实体读取状态，不用渲染时钟推算阶段。
        string stage = _battle.Bullets.ActiveBullets.OfType<VLaser>().FirstOrDefault()?.Stage.ToString() ?? "Between waves";
        _status.Text = $"BOSS 03   {_battle.Elapsed:F3} s   |   Lasers: {stage}";
    }

    /// <summary>按固定年龄导出预警、生效、消退及下一轮截图到忽略目录。</summary>
    private async void Capture()
    {
        try
        {
            // 每个指定帧先完成逻辑，再等待渲染刷新，截图不影响战斗随机或时间。
            int tick = 0;
            foreach (int target in _phase02 ? new[] { 120, 180, 216, 270, 420, 720 } : new[] { 75, 150, 210, 270, 390 })
            {
                while (tick < target) { _battle.StepFixed(Vector2.Zero, false); tick++; }
                UpdateStatus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var screenshot = GetViewport().GetTexture().GetImage();
                string file = ProjectSettings.GlobalizePath($"res://.tools/boss03-preview-{(_phase02 ? "phase02-" : "")}{target}.png");
                if (screenshot.SavePng(file) != Error.Ok) throw new InvalidOperationException("激光预览截图保存失败：" + file);
            }
            GD.Print("PASS: Boss03 preview captured");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
