using Godot;
using System;

/// <summary>切换整个Emitter/Boss工作区，分别保留未保存文档、草稿与历史。</summary>
public partial class EmitterEditor
{
    /// <summary>当前是否显示Boss编辑工作区。</summary>
    public bool IsBossMode { get; private set; }
    /// <summary>首次进入Boss模式时建立的独立工作区。</summary>
    public BossEditorPanel? BossPanel { get; private set; }
    // 模式栏、Emitter主体和Boss主体共享的布局宿主。
    private VBoxContainer _modeHost = null!, _emitterBody = null!;
    private OptionButton _modeMenu = null!;

    /// <summary>建立始终可见的整体编辑模式切换栏。</summary>
    /// <param name="host">顶部布局宿主。</param>
    private void SetupModes(VBoxContainer host)
    {
        // 嵌入面板不创建未挂入场景树的菜单及弹出窗口。
        _modeMenu = new OptionButton();
        _modeHost = host;
        var bar = new HBoxContainer(); host.AddChild(bar);
        bar.AddChild(new Label { Text = "编辑模式", CustomMinimumSize = new Vector2(80, 0) });
        _modeMenu.AddItem("Emitter 弹幕编辑"); _modeMenu.AddItem("Boss 目录与阶段编辑");
        _modeMenu.TooltipText = "整体切换工作区；两个模式分别保留文档、未应用草稿和撤销历史。";
        bar.AddChild(_modeMenu);
        _modeMenu.ItemSelected += index => SwitchMode(index == 1);
    }
    /// <summary>停止旧预览后切换完整工作区，不丢弃任一模式文档。</summary>
    /// <param name="boss">为真时进入Boss模式。</param>
    public void SwitchMode(bool boss)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        StopPreview(); Canvas.CancelDrag(); BossPanel?.StopPreview();
        if (boss && BossPanel is null)
        {
            BossPanel = new BossEditorPanel();
            _modeHost.AddChild(BossPanel);
        }
        IsBossMode = boss; _modeMenu.Select(boss ? 1 : 0);
        _emitterBody.Visible = !boss;
        if (BossPanel is not null) BossPanel.Visible = boss;
        GetWindow().Title = boss ? "Boss编辑器 · BulletHell" : "弹幕编辑器 · BulletHell";
    }
}
