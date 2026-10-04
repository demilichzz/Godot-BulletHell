/// <summary>编辑器已有快捷动作，与键盘组合解耦。</summary>
public enum EditorAction
{
    /// <summary>没有快捷动作。</summary>
    None,
    /// <summary>保存当前文件。</summary>
    Save,
    /// <summary>另存当前文件。</summary>
    SaveAs,
    /// <summary>打开文件。</summary>
    Open,
    /// <summary>搜索属性。</summary>
    Search,
    /// <summary>撤销当前文档。</summary>
    Undo,
    /// <summary>重做当前文档。</summary>
    Redo
}
