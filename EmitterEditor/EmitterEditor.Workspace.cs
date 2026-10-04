using System;

/// <summary>将既有Emitter属性、布局和预览嵌入目录工作区，文档状态由会话持有。</summary>
public partial class EmitterEditor
{
    /// <summary>当前Creator相对Emitter文件的JSON指针。</summary>
    public string SelectedCreator => _selection;
    /// <summary>切换共享文档并定位Creator，不丢失原文草稿或撤销记录。</summary>
    /// <param name="document">会话中唯一的Emitter文档。</param>
    /// <param name="creatorPath">需要选择的Creator路径，空字符串表示Emitter共用属性。</param>
    public void UseDocument(EmitterDocument document, string creatorPath = "")
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        StopPreview();
        Document.Selection = _selection;
        Document = document;
        _selection = creatorPath;
        _refreshing = true;
        try { SyncJson(); RebuildTree(); BuildInspector(); ValidateLayout(); UpdateTitle(); }
        finally { _refreshing = false; }
    }

    /// <summary>切出工作区时停止预览，保留所有已应用编辑与草稿。</summary>
    public void StopWorkspacePreview() => StopPreview();

    /// <summary>将目录操作诊断显示在当前可见的Emitter状态栏。</summary>
    /// <param name="message">完整诊断文本。</param>
    /// <param name="error">是否为错误。</param>
    internal void ReportWorkspaceStatus(string message, bool error) => SetStatus(message, error);

    /// <summary>外部目录保存操作前提交失焦字段，并保护尚未应用的JSON草稿。</summary>
    public void PrepareWorkspaceSave()
    {
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        RequireAppliedDraft();
    }
}
