using Godot;
using System;

/// <summary>由顶层工作区持有的文件与确认窗口，内容面板只提交操作请求。</summary>
public partial class EditorFileDialogs : Node
{
    // 打开、保存与丢弃窗口在整个工作区只创建一套。
    private readonly FileDialog _open = new(), _save = new();
    private readonly ConfirmationDialog _confirm = new();
    private Action<string>? _openAction, _saveAction;
    private Action? _confirmAction;
    /// <summary>窗口操作失败时向当前工作区显示诊断。</summary>
    public Action<Exception>? Failed { get; set; }
    /// <summary>建立窗口并绑定一次性回调。</summary>
    public override void _Ready()
    {
        _open.FileMode = FileDialog.FileModeEnum.OpenFile;
        _save.FileMode = FileDialog.FileModeEnum.SaveFile;
        _open.Access = _save.Access = FileDialog.AccessEnum.Filesystem;
        AddChild(_open); AddChild(_save); AddChild(_confirm);
        _open.FileSelected += path => { var action = _openAction; _openAction = null; Run(() => action?.Invoke(path)); };
        _save.FileSelected += path => { var action = _saveAction; _saveAction = null; Run(() => action?.Invoke(path)); };
        _open.Canceled += () => _openAction = null;
        _save.Canceled += () => _saveAction = null;
        _confirm.Title = "未保存的修改"; _confirm.OkButtonText = "放弃并继续"; _confirm.CancelButtonText = "取消";
        _confirm.Confirmed += () => { var action = _confirmAction; _confirmAction = null; Run(() => action?.Invoke()); };
        _confirm.Canceled += () => _confirmAction = null;
    }
    /// <summary>打开文件或选择资源引用，不直接修改文档。</summary>
    /// <param name="title">窗口标题。</param>
    /// <param name="directory">初始目录。</param>
    /// <param name="selected">选择成功后的操作。</param>
    /// <param name="resource">为真时只选择Godot资源，默认使用文件系统。</param>
    public void Open(string title, string directory, Action<string> selected, bool resource = false)
    {
        _openAction = selected;
        _open.Title = title; _open.Filters = new[] { "*.json ; JSON" };
        _open.Access = resource ? FileDialog.AccessEnum.Resources : FileDialog.AccessEnum.Filesystem;
        _open.CurrentDir = resource ? directory : ProjectSettings.GlobalizePath(directory);
        _open.PopupCentered(new Vector2I(960, 640));
    }
    /// <summary>选择保存路径，写盘及文件身份更新由会话完成。</summary>
    /// <param name="title">窗口标题。</param>
    /// <param name="directory">初始目录。</param>
    /// <param name="fileName">建议文件名。</param>
    /// <param name="selected">路径选定后的保存操作。</param>
    public void Save(string title, string directory, string fileName, Action<string> selected)
    {
        _saveAction = selected;
        _save.Title = title; _save.Filters = new[] { "*.json ; JSON" };
        _save.Access = FileDialog.AccessEnum.Filesystem;
        _save.CurrentDir = ProjectSettings.GlobalizePath(directory); _save.CurrentFile = fileName;
        _save.PopupCentered(new Vector2I(960, 640));
    }
    /// <summary>确认明确会丢失内容的操作，取消时不执行回调。</summary>
    /// <param name="message">丢失范围说明。</param>
    /// <param name="confirmed">用户确认后执行的操作。</param>
    public void Confirm(string message, Action confirmed)
    {
        _confirm.DialogText = message; _confirmAction = confirmed; _confirm.PopupCentered();
    }
    /// <summary>将窗口回调异常转交工作区，保持当前文档。</summary>
    /// <param name="action">已经选定的窗口操作。</param>
    private void Run(Action action) { try { action(); } catch (Exception error) { Failed?.Invoke(error); } }
}
