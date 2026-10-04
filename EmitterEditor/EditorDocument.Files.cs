using Godot;
using System;
using System.IO;

/// <summary>编辑文件身份与类型检查，两种模式共用文件规则但不共享会话。</summary>
public sealed partial class EditorDocument
{
    /// <summary>规范化Godot或系统路径。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>操作系统绝对路径。</returns>
    internal static string FullPath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
    /// <summary>拒绝用一种文档覆盖其他类型的已有文件。</summary>
    /// <param name="target">绝对目标路径。</param>
    /// <param name="kind">允许的文件类型。</param>
    internal static void CheckTargetKind(string target, EditorDocumentKind kind)
    {
        if (File.Exists(target) && DetectKind(Parse(File.ReadAllText(target))) != kind)
            throw new InvalidOperationException("不能覆盖其他类型的文档：" + target);
    }
    /// <summary>跳转前检查磁盘文件及Emitter根结构，业务参数错误允许在目标模式修复。</summary>
    /// <param name="path">项目内Emitter引用。</param>
    /// <returns>可进入Emitter编辑器时为真。</returns>
    internal static bool CanOpenEmitter(string path)
    {
        try
        {
            string full = BossCatalog.PathIdentity(path);
            return DetectKind(Parse(File.ReadAllText(full))) == EditorDocumentKind.Emitter;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { return false; }
    }
}
