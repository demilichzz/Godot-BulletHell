using Godot;

/// <summary>Boss_02阶段01的空发射器，暂不生成弹幕。</summary>
public sealed class B02P01_Emitter01 : BulletEmitter
{
    /// <summary>保留发射入口，不生成子弹。</summary>
    /// <param name="manager">当前战斗的弹幕容器。</param>
    /// <param name="origin">全局发射位置，单位为逻辑像素。</param>
    protected override void Build(BulletManager manager, Vector2 origin) { }
}
