using Godot;

/// <summary>以预警激光网和大小圆弹近似神罚「幼小的恶魔领主」，波次间换位。</summary>
public sealed class B03_Phase01 : BossPhase
{
    /// <summary>波次间移动速度，240逻辑像素/秒，最长280像素路段在下一轮前完成。</summary>
    protected override float MoveSpeed => 240;
    /// <summary>阶段显示名称。</summary>
    public override string Name => "神罚「幼小的恶魔领主」 · 阶段01";

    /// <summary>绑定数据发射器，仅在一轮激光消退后移动到下一个固定目标。</summary>
    /// <param name="boss">所属Boss，位置为战场局部像素。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(new B03P01_Emitter01(), boss);
        // 4700ms起每5秒换位；无随机消耗，下一轮出生前完成移动。
        var targets = new[] { new Vector2(500, 230), new Vector2(780, 230), new Vector2(640, 240) };
        int next = 0;
        Timeline!.Repeat(4700, 5000, null, () =>
        {
            SetMoveTarget(boss, targets[next]);
            next = (next + 1) % targets.Length;
        });
    }

    /// <summary>累计损失100点生命后进入预留的第2阶段。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>达到阶段结束血线时为真。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 100;
}
