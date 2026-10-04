using Godot;

/// <summary>战斗状态视图，只读取快照并格式化文字，不执行输入或战斗行为。</summary>
public partial class BattleHud : Label
{
    /// <summary>由场景装配的只读数据来源。</summary>
    public IBattleStatusSource Source { get; init; } = null!;
    /// <summary>设置与原HUD一致的位置、字体和输入穿透。</summary>
    public override void _Ready()
    {
        Position = new Vector2(12, 10); MouseFilter = MouseFilterEnum.Ignore;
        AddThemeFontOverride("font", GD.Load<Font>("res://Assets/fonts/lxgl/LXGWWenKaiGBScreen.ttf"));
        AddThemeFontSizeOverride("font_size", 18);
        AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.06f, 0.09f));
        AddThemeConstantOverride("outline_size", 4);
    }
    /// <summary>渲染刷新只读取状态，不影响固定步。</summary>
    /// <param name="delta">渲染秒数，不用于任何业务计时。</param>
    public override void _Process(double delta) => ShowStatus(Source.CaptureStatus());
    /// <summary>显示独立状态快照，保持现有提示与数字格式。</summary>
    /// <param name="status">本次显示值。</param>
    public void ShowStatus(BattleStatus status)
    {
        // 结束提示只由显示层格式化，不产生重开请求。
        string result = status.State == BattleState.Victory ? "胜利！按 R 重新开始" : "战斗中";
        Text = $"玩家 HP {status.PlayerHp}/{status.PlayerMaxHp}    Boss HP {status.BossHp}/{status.BossMaxHp}    阶段 {status.PhaseIndex + 1}/{status.PhaseCount} HP {status.PhaseHp}/{status.PhaseMaxHp}    {status.PhaseName}\n"
            + $"闪避冷却 {status.DodgeCooldown:0.0} 秒    时间 {status.Elapsed:0.0} 秒\n"
            + "WASD / 方向键移动 · 空格闪避 · Q/E 切换阶段 · 自动攻击 · Esc 返回选择\n" + result;
        if (status.AIHitCount is long hits) Text += $"\nAI 被击中次数：{hits}";
    }
}
