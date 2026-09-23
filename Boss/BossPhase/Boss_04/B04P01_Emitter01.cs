using Godot;

/// <summary>Boss_04阶段01的空发射器，暂不生成弹幕。</summary>
public sealed class B04P01_Emitter01 : BulletEmitter
{
    /// <summary>保留发射入口，不生成子弹。</summary>
    /// <param name="manager">当前战斗的弹幕容器。</param>
    /// <param name="owner">所属Boss；空定义不读取其位置。</param>
    protected override void Build(BulletManager manager, BossController owner) { }
}
