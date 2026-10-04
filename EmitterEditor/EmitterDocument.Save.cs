using System;
using System.IO;
using System.Text;

/// <summary>文档保存先冻结并校验内容，再原子替换单个文件。</summary>
public sealed partial class EmitterDocument
{
    /// <summary>按文档模式校验后原子写入JSON；失败不更新保存状态。</summary>
    /// <param name="path">目标文件绝对路径。</param>
    public void Save(string path) => PrepareSave(path).Write();

    /// <summary>冻结待写入的已应用文本；调用方可提供同一保存批次的校验入口。</summary>
    /// <param name="path">目标文件绝对路径。</param>
    /// <param name="validate">可选完整文本校验；仅供会话复用本批次检查结果。</param>
    /// <returns>校验通过且尚未写盘的单次保存操作。</returns>
    internal PreparedSave PrepareSave(string path, Action<string>? validate = null)
    {
        EnsureIdle();
        if (Draft is not null) throw new InvalidOperationException("请先应用JSON草稿。");
        // 文本、路径和修订在校验开始前冻结，不能写入校验后的另一个版本。
        var operation = new PreparedSave(this, Path.GetFullPath(path));
        if (validate is not null) validate(_text);
        else ValidateCurrent();
        operation.EnsureCurrent();
        return operation;
    }

    /// <summary>已校验的不可变文件内容，不包含可复用的战斗运行对象。</summary>
    internal sealed class PreparedSave
    {
        // 保留原文件身份和修订，阻止陈旧保存覆盖新编辑或另存后的文件身份。
        private readonly EmitterDocument _document;
        private readonly string _text, _sourcePath;
        private readonly long _revision;
        private bool _completed;
        /// <summary>此次写入的规范化绝对路径。</summary>
        internal string Target { get; }
        /// <summary>捕获文档状态，校验由外层准备入口执行。</summary>
        /// <param name="document">要保存的唯一会话文档。</param>
        /// <param name="target">已经规范化的绝对目标路径。</param>
        internal PreparedSave(EmitterDocument document, string target)
        {
            _document = document; _text = document._text; _sourcePath = document.FilePath;
            _revision = document.Revision; Target = target;
        }
        /// <summary>确认准备后的文档未被修改、另存、重新进入事务或添加草稿。</summary>
        internal void EnsureCurrent()
        {
            _document.EnsureIdle();
            if (_completed || _document.Revision != _revision || _document.FilePath != _sourcePath || _document.Draft is not null)
                throw new InvalidOperationException("文档状态已变化，请重新执行保存：" + Target);
        }
        /// <summary>写入已校验文本，成功后只更新该文件的保存状态；不再次解析资源。</summary>
        internal void Write()
        {
            EnsureCurrent();
            // 临时文件和目标位于同目录，避免跨卷移动；不覆盖其他临时文件。
            string temporary = Target + ".editor-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // 无BOM的UTF-8文本刷新磁盘后再替换正式文件。
                    byte[] bytes = new UTF8Encoding(false).GetBytes(_text + Environment.NewLine);
                    stream.Write(bytes); stream.Flush(true);
                }
                File.Move(temporary, Target, true);
                _document.FilePath = Target; _document._saved = _text; _completed = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
