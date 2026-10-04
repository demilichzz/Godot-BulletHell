using Godot;

/// <summary>顶层模式导航，两个内容面板共用会话，不互相创建工作区。</summary>
public partial class EmitterEditor
{
    /// <summary>当前是否显示Boss目录工作区。</summary>
    public bool IsBossMode { get; private set; }
    /// <summary>首次进入目录模式时创建的内容面板。</summary>
    public BossEditorPanel? BossPanel { get; private set; }
    // 唯一模式菜单只属于顶层。
    private readonly OptionButton _modeMenu = new();

    /// <summary>建立始终可见的模式导航。</summary>
    /// <param name="host">顶层纵向宿主。</param>
    private void SetupModes(VBoxContainer host)
    {
        // 模式菜单固定在两个内容面板之外。
        var bar = new HBoxContainer(); host.AddChild(bar);
        bar.AddChild(new Label { Text = "编辑模式", CustomMinimumSize = new Vector2(80, 0) });
        _modeMenu.AddItem("Emitter 弹幕编辑"); _modeMenu.AddItem("Boss 目录与阶段编辑");
        _modeMenu.TooltipText = "两种模式共享同一路径的文档、JSON草稿和撤销历史。";
        bar.AddChild(_modeMenu);
        _modeMenu.ItemSelected += index => Guard(() => SwitchMode(index == 1));
    }
    /// <summary>停止旧预览并刷新目标面板，共享文档修改在模式切换时可见。</summary>
    /// <param name="boss">为真时显示Boss目录，否则显示独立Emitter。</param>
    public void SwitchMode(bool boss)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (IsBossMode) BossPanel?.Suspend(); else _emitter?.Suspend();
        if (boss && BossPanel is null)
        {
            BossPanel = new BossEditorPanel { Session = Session, Files = _files };
            BossPanel.SaveEmitterRequested += (panel, choosePath) => Guard(() => SaveEmitter(panel, choosePath));
            _host.AddChild(BossPanel);
        }
        if (!boss) EnsureEmitter();
        IsBossMode = boss; _modeMenu.Select(boss ? 1 : 0);
        if (_emitter is not null) _emitter.Visible = !boss;
        if (BossPanel is not null) BossPanel.Visible = boss;
        if (boss) BossPanel!.Refresh();
        else _emitter!.UseDocument(_emitter.Document, _emitter.Document.Selection);
        GetWindow().Title = boss ? "Boss编辑器 · BulletHell" : "弹幕编辑器 · BulletHell";
    }
}
