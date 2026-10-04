using System;

/// <summary>Emitter内容面板的文档切换与工作区通知，不管理文件身份。</summary>
public partial class EmitterPanel
{
    /// <summary>绑定共享文档并恢复选择；支持入树前注入。</summary>
    /// <param name="document">会话中唯一的Emitter文档。</param>
    /// <param name="creatorPath">当前Creator路径，空值选择Emitter属性。</param>
    public void UseDocument(EditorDocument document, string creatorPath = "")
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsNodeReady())
        {
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            StopPreview(); Canvas.CancelDrag();
        }
        Document = document; _selection = creatorPath;
        if (!IsNodeReady()) return;
        _refreshing = true;
        try { SyncJson(); RebuildTree(); BuildInspector(); ValidateLayout(); UpdateTitle(); }
        finally { _refreshing = false; }
    }
    /// <summary>切出当前内容时提交失焦字段并保留实际原文草稿，停止预览。</summary>
    public void Suspend()
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        Document.Draft = _json.Text == _jsonBaseline ? null : _json.Text;
        Document.Selection = _selection;
        StopPreview(); Canvas.CancelDrag();
    }
    /// <summary>工作区保存成功后更新标题，不重建文档或历史。</summary>
    public void NotifySaved() { UpdateTitle(); }
    /// <summary>显示文件或目录操作诊断。</summary>
    /// <param name="message">完整诊断文本。</param>
    /// <param name="error">是否为错误。</param>
    internal void ReportWorkspaceStatus(string message, bool error) => SetStatus(message, error);
    /// <summary>保存前提交失焦字段并保护尚未应用的草稿。</summary>
    public void PrepareWorkspaceSave()
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        RequireAppliedDraft();
    }
}
