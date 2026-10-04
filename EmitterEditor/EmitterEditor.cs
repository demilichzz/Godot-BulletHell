using Godot;
using System;
using System.IO;

/// <summary>编辑器顶层工作区，统一管理会话、文件窗口、模式切换和退出保护。</summary>
public partial class EmitterEditor : Control
{
    /// <summary>仅属于Emitter模式的文件会话。</summary>
    public EditorSession Session { get; } = new();
    /// <summary>与Emitter草稿隔离的Boss文件会话。</summary>
    public BossEditorSession BossSession { get; } = new();
    /// <summary>首次启动是否进入Boss目录，默认是。</summary>
    public bool StartInCatalog { get; set; } = true;
    /// <summary>当前独立Emitter文档；尚未进入该模式时不可访问。</summary>
    public EditorDocument Document => EmitterContent.Document;
    /// <summary>独立Emitter画布，供顶层定位与交互访问。</summary>
    public EditorCanvas Canvas => EmitterContent.Canvas;
    /// <summary>独立Emitter预览，使用正式战斗入口。</summary>
    public EditorPreview Preview => EmitterContent.Preview;
    /// <summary>仅属于Emitter模式的内容面板。</summary>
    public EmitterPanel EmitterContent => _emitter ?? throw new InvalidOperationException("尚未打开独立Emitter工作区。");
    // 窗口服务和内容宿主只属于顶层，不由内容面板递归创建。
    private readonly EditorFileDialogs _files = new();
    private readonly VBoxContainer _host = new();
    private EmitterPanel? _emitter;

    /// <summary>建立工作区外壳，读取启动参数并显示相应模式。</summary>
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        Engine.PhysicsTicksPerSecond = 60;
        GetWindow().MinSize = new Vector2I(1200, 760); GetWindow().Size = new Vector2I(1600, 950);
        Theme = new Theme { DefaultFontSize = 15 };
        // 全窗口底色与统一边距，内部面板仅填充可用内容区。
        var background = new ColorRect { Color = new Color("111b28"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 12);
        AddChild(margin); margin.AddChild(_host);
        _files.Failed = error => Report(error.Message); AddChild(_files);
        SetupModes(_host);
        // 参数只决定打开哪个会话文档，不另建文件状态。
        string? emitterPath = null, catalogPath = null;
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--emitter=", StringComparison.Ordinal)) emitterPath = argument[10..];
            if (argument.StartsWith("--boss=", StringComparison.Ordinal)) catalogPath = argument[7..];
        }
        Guard(() =>
        {
            if (emitterPath is not null) OpenEmitter(emitterPath);
            if (catalogPath is not null) { BossSession.Open(catalogPath); SwitchMode(true); }
            else if (emitterPath is null) SwitchMode(StartInCatalog);
        });
    }

    /// <summary>按需创建独立Emitter面板，文件动作只向工作区回调。</summary>
    /// <param name="document">首次打开的文档；为空时新建未命名文档。</param>
    private void EnsureEmitter(EditorDocument? document = null)
    {
        if (_emitter is not null) return;
        _emitter = new EmitterPanel { ConfirmRequested = _files.Confirm };
        _emitter.UseDocument(document ?? Session.NewEmitter(), "/VNodes");
        _emitter.SaveRequested += choosePath => Guard(() => SaveEmitter(_emitter, choosePath));
        _host.AddChild(_emitter);
        AddFileButton("新建", () => ReplaceUntitledThen(NewEmitter), 0);
        AddFileButton("打开…", ChooseEmitter, 1);
        AddFileButton("另存为…", () => SaveEmitter(_emitter, true), 3);
    }
    /// <summary>在内容工具栏插入顶层文件按钮。</summary>
    /// <param name="text">按钮标题。</param>
    /// <param name="action">文件操作。</param>
    /// <param name="index">在工具栏中的零基位置。</param>
    private void AddFileButton(string text, Action action, int index)
    {
        // 按钮只提交顶层动作，内容面板不认识文件系统。
        var button = new Button { Text = text };
        _emitter!.FileTools.AddChild(button); _emitter.FileTools.MoveChild(button, index);
        button.Pressed += () => Guard(action);
    }
    /// <summary>打开会话文档，成功后才替换当前未命名文档；界面须先确认其丢弃。</summary>
    /// <param name="path">Godot资源路径或文件路径。</param>
    public void OpenEmitter(string path)
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        // 读取失败时不丢弃原文档，重复路径直接使用会话对象。
        var document = Session.OpenEmitter(path);
        if (_emitter is null) EnsureEmitter(document);
        else
        {
            _emitter.Suspend();
            if (_emitter.Document.FilePath.Length == 0) Session.DiscardUntitled(_emitter.Document);
            _emitter.UseDocument(document, document.Selection);
        }
        SwitchMode(false);
    }
    /// <summary>从Boss引用跳转，确认结束后再次检查磁盘，失败不替换当前文档。</summary>
    /// <param name="path">Emitter资源路径。</param>
    private void OpenEmitterReference(string path) => ReplaceUntitledThen(() =>
    {
        if (!EditorDocument.CanOpenEmitter(path)) throw new InvalidOperationException("Emitter文件已失效或类型不正确：" + path);
        OpenEmitter(path);
    });
    /// <summary>用户确认后新建文档；已命名文件仍保留在会话中。</summary>
    public void NewEmitter()
    {
        if (_emitter is not null)
        {
            _emitter.Suspend();
            if (_emitter.Document.FilePath.Length == 0) Session.DiscardUntitled(_emitter.Document);
            _emitter.UseDocument(Session.NewEmitter(), "/VNodes");
        }
        else EnsureEmitter();
    }
    /// <summary>显示打开窗口，选定文件后才确认替换未命名内容。</summary>
    private void ChooseEmitter() => _files.Open("打开 Emitter JSON", "res://Data/Emitters",
        path => ReplaceUntitledThen(() => OpenEmitter(path)));
    /// <summary>只在当前未命名文档会被替换时请求丢弃确认。</summary>
    /// <param name="action">确认后执行的切换操作。</param>
    private void ReplaceUntitledThen(Action action)
    {
        _emitter?.Suspend();
        if (_emitter is not null && _emitter.Document.FilePath.Length == 0
            && (_emitter.Document.Dirty || _emitter.Document.Draft is not null))
            _files.Confirm("当前未命名Emitter尚未保存，继续将放弃其内容及JSON草稿。", action);
        else action();
    }
    /// <summary>保存指定内容面板的文档，路径身份统一由会话维护。</summary>
    /// <param name="panel">独立Emitter面板。</param>
    /// <param name="choosePath">是否请求另存。</param>
    private void SaveEmitter(EmitterPanel panel, bool choosePath)
    {
        panel.PrepareWorkspaceSave();
        // 捕获文档身份，窗口打开期间不会误保存之后选中的另一文件。
        var document = panel.Document;
        /// <summary>写入已捕获的文档并同步保存状态。</summary>
        /// <param name="path">用户选定的目标路径。</param>
        void Write(string path)
        {
            Session.SaveEmitter(document, path);
            panel.NotifySaved(); panel.ReportWorkspaceStatus("已保存：" + document.FilePath, false);
        }
        if (choosePath || document.FilePath.Length == 0)
            _files.Save("保存 Emitter JSON", document.FilePath.Length == 0 ? "res://Data/Emitters" : Path.GetDirectoryName(document.FilePath)!,
                document.FilePath.Length == 0 ? "NewEmitter.json" : Path.GetFileName(document.FilePath), Write);
        else Write(document.FilePath);
    }
    /// <summary>刷新当前独立Emitter内容。</summary>
    public void Refresh() => EmitterContent.Refresh();
    /// <summary>向当前可见工作区显示操作失败。</summary>
    /// <param name="message">完整错误说明。</param>
    private void Report(string message)
    {
        if (IsBossMode && BossPanel is not null) BossPanel.ReportWorkspaceStatus(message, true);
        else if (_emitter is not null) _emitter.ReportWorkspaceStatus(message, true);
        else GD.PushError(message);
    }
    /// <summary>保护工作区入口，操作失败保留当前会话。</summary>
    /// <param name="action">请求执行的操作。</param>
    private void Guard(Action action) { try { action(); } catch (Exception error) { Report(error.Message); } }
    /// <summary>独立Emitter的打开快捷键由工作区处理。</summary>
    /// <param name="input">键盘输入。</param>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (IsBossMode || EditorInput.Shortcut(input) != EditorAction.Open) return;
        Guard(ChooseEmitter); AcceptEvent();
    }
    /// <summary>关闭前收集当前草稿并检查整个会话，而非仅当前面板。</summary>
    /// <param name="what">Godot生命周期通知。</param>
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest || !IsNodeReady()) return;
        if (IsBossMode) BossPanel?.Suspend(); else _emitter?.Suspend();
        if (Session.HasUnsaved || BossSession.HasUnsaved) _files.Confirm("会话中存在未保存的目录、Boss、Emitter或JSON草稿。退出将放弃这些内容。", () => GetTree().Quit());
        else GetTree().Quit();
    }
}
