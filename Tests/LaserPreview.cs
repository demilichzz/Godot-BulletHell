using Godot;
using System;
using System.Linq;

/// <summary>独立激光预览场景；演示定点直线、定点曲线和移动路径，按Space暂停，命令行可导出阶段截图。</summary>
public partial class LaserPreview : Node2D
{
    // 预览仍使用唯一BattleManager的固定步，关闭默认输入与自动攻击。
    private BattleManager _battle = null!;
    private VBulletEmitter _fixed = null!, _path = null!;
    private readonly Label _status = new() { Position = new Vector2(160, 32) };
    private bool _capture, _paused;

    /// <summary>装配隔离战斗及两种激光示例，不改动正式Boss阶段。</summary>
    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().Contains("--laser-capture");
        _battle = new BattleManager();
        AddChild(_battle);
        _battle.Initialize(this);
        _battle.SetPhysicsProcess(false);
        _battle.Boss.Stop();
        _battle.Player.Attack.Stop();
        _battle.Boss.Visible = false;
        _battle.Player.Visible = false;
        _fixed = VBulletEmitter.Load("res://Data/Emitters/Examples/FixedLaser.json");
        _path = VBulletEmitter.Load("res://Data/Emitters/Examples/PathLaser.json");
        _fixed.Start(_battle.Boss, _battle.Bullets);
        _path.Start(_battle.Boss, _battle.Bullets);
        _battle.Timers.AdvanceByUnits(0, dispatchLocal: _battle.Bullets.DispatchTimelines);
        // 标签使用独立HUD画布，避免被CoreAdd亮芯覆盖，不进入游戏UI。
        var hud = new CanvasLayer();
        AddChild(hud);
        hud.AddChild(_status);
        hud.AddChild(new Label { Text = "FIXED / ROUND", Position = new Vector2(160, 110) });
        hud.AddChild(new Label { Text = "FIXED / POINT", Position = new Vector2(160, 230) });
        hud.AddChild(new Label { Text = "FIXED / VPATH CURVE / POINT", Position = new Vector2(160, 350) });
        hud.AddChild(new Label { Text = "PATH / 280 px/s / END AT 3500 ms", Position = new Vector2(160, 530) });
        hud.AddChild(new Label { Text = "Space: pause / resume     |     Examples repeat automatically", Position = new Vector2(160, 750) });
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
        string stage = _fixed.Bullets.OfType<VLaser>().FirstOrDefault()?.Stage.ToString() ?? "End";
        _status.Text = $"LASER PREVIEW   {_battle.Elapsed:F3} s   |   Fixed: {stage}";
    }

    /// <summary>按固定年龄导出预警、展开、生效、消退和结束截图到忽略目录。</summary>
    private async void Capture()
    {
        try
        {
            // 每个指定帧先完成逻辑，再等待渲染刷新，截图不影响战斗随机或时间。
            int tick = 0;
            foreach (int target in new[] { 18, 42, 60, 111, 126, 209, 210 })
            {
                while (tick < target) { _battle.StepFixed(Vector2.Zero, false); tick++; }
                UpdateStatus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var screenshot = GetViewport().GetTexture().GetImage();
                string file = ProjectSettings.GlobalizePath($"res://.tools/laser-preview-{target}.png");
                if (screenshot.SavePng(file) != Error.Ok) throw new InvalidOperationException("激光预览截图保存失败：" + file);
            }
            GD.Print("PASS: laser preview captured");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
