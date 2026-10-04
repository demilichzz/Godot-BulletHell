using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>验证独立与目录模式共享文件身份、历史、草稿和文件操作。</summary>
public partial class EditorVerification
{
    /// <summary>通过真实模式、保存窗口和关闭确认验证整个会话。</summary>
    /// <returns>界面操作及节点清理完成后的任务。</returns>
    private async Task VerifySharedModes()
    {
        // 所有写入均使用本次测试的临时文件，不改正式目录和弹幕。
        string source = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.tools/modes-source.json"));
        string target = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.tools/modes-save-as.json"));
        string occupied = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.tools/modes-occupied.json"));
        string catalog = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.tools/modes-catalog.json"));
        _temporary.AddRange(new[] { source, target, occupied, catalog });
        var fixture = new EmitterDocument(); fixture.Save(source); fixture.Save(occupied);
        var editor = new EmitterEditor { StartInCatalog = false }; AddChild(editor);
        editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        try
        {
            await Settle();
            editor.OpenEmitter(source);
            var shared = editor.Document;
            shared.Edit(root => root["Core"]!["Damage"] = 7); editor.Refresh();
            editor.SwitchMode(true);
            var panel = editor.BossPanel!;
            panel.Document.New();
            panel.Document.Edit(root => root["Bosses"]![0]!["Phases"]![0]!["Emitters"] = new JsonArray(ProjectSettings.LocalizePath(source)));
            editor.Session.SaveCatalog(catalog);
            panel.SelectEmitter(0, 0, 0); await Settle();
            Check(ReferenceEquals(shared, panel.EmitterPanel!.Document) && shared.Validate().Core.Damage == 7,
                "独立模式与目录引用使用同一文档，保留未保存修改");
            Check(ReferenceEquals(editor.Session, panel.Session) && editor.Session.Emitters.Count() == 1,
                "整个工作区只有一个文件会话");
            Check(Descendants<EmitterEditor>(editor).Count() == 1 && Descendants<FileDialog>(editor).Count() == 2,
                "内容面板不递归创建顶层工作区或第二套文件窗口");
            Press(panel.EmitterPanel, "撤销"); await Settle();
            editor.SwitchMode(false);
            Check(editor.Document.Validate().Core.Damage == 1, "目录模式撤销可从独立模式读取");
            Press(editor, "重做"); await Settle();
            Check(shared.Validate().Core.Damage == 7, "独立模式重做同一撤销历史");

            // 草稿经真实CodeEdit输入，换模式后必须仍可见且阻止直接保存。
            var code = Descendants<CodeEdit>(editor.EmitterContent).Single();
            code.Text = code.Text.Replace("\"Damage\": 7", "\"Damage\": 9");
            code.EmitSignal(CodeEdit.SignalName.TextChanged);
            string draft = code.Text;
            editor.SwitchMode(true); await Settle();
            var embeddedCode = Descendants<CodeEdit>(panel.EmitterPanel!).Single();
            Check(shared.Draft == draft && embeddedCode.Text == draft, "跨模式保留同一未应用JSON草稿");
            Reject(() => editor.Session.SaveEmitter(shared, source), "共享草稿未应用时禁止保存旧内容");
            Press(panel.EmitterPanel!, "应用 JSON 草稿"); await Settle();
            Press(panel.EmitterPanel!, "保存"); await Settle();
            Check(!shared.Dirty && VBulletEmitter.Load(source).Core.Damage == 9, "嵌入保存经统一会话写入当前文件");
            editor.SwitchMode(false);
            Check(editor.Document.Validate().Core.Damage == 9 && !editor.Session.HasUnsaved, "回到独立模式同步保存状态");

            // 另存不能覆盖会话中另一文档；失败保持原路径、磁盘和历史。
            var existing = editor.Session.OpenEmitter(occupied);
            string occupiedBefore = File.ReadAllText(occupied);
            shared.Edit(root => root["Core"]!["Damage"] = 11); editor.Refresh();
            var save = Descendants<FileDialog>(editor).Single(dialog => dialog.FileMode == FileDialog.FileModeEnum.SaveFile);
            Press(editor, "另存为…"); save.EmitSignal(FileDialog.SignalName.FileSelected, occupied); save.Hide();
            Check(shared.FilePath == source && shared.Dirty && File.ReadAllText(occupied) == occupiedBefore
                && ReferenceEquals(existing, editor.Session.OpenEmitter(occupied)), "另存冲突不覆盖已打开文档或改变身份");
            // 目标目录不存在时，I/O失败不得提前改写会话映射。
            string missing = Path.Combine(ProjectSettings.GlobalizePath("res://.tools"), "absent-" + Guid.NewGuid().ToString("N"), "emitter.json");
            Reject(() => editor.Session.SaveEmitter(shared, missing), "失败写盘保持原映射");
            Check(shared.FilePath == source && ReferenceEquals(shared, editor.Session.OpenEmitter(source)) && shared.CanUndo,
                "写盘失败保留文件身份和撤销历史");
            Press(editor, "另存为…"); save.EmitSignal(FileDialog.SignalName.FileSelected, target); save.Hide();
            Check(shared.FilePath == target && !shared.Dirty && ReferenceEquals(shared, editor.Session.OpenEmitter(target)),
                "成功另存后新路径指向原文档实例");
            var original = editor.Session.OpenEmitter(source);
            Check(!ReferenceEquals(original, shared) && original.Validate().Core.Damage == 9
                && shared.Validate().Core.Damage == 11, "旧路径重新打开磁盘原文件，新旧内容不串用");
            Check(shared.CanUndo && ReferenceEquals(shared, editor.Session.OpenEmitter(ProjectSettings.LocalizePath(target))),
                "另存保留历史且资源路径与绝对路径归一");
            Reject(() => editor.Session.SaveCatalog(target), "目录保存不能覆盖已打开Emitter");
            Reject(() => editor.Session.SaveEmitter(shared, catalog), "Emitter保存不能覆盖当前目录");

            // 目录仍引用原路径；隐藏文件中的草稿也参与退出保护。
            editor.SwitchMode(true); await Settle();
            Check(ReferenceEquals(panel.EmitterPanel!.Document, original), "另存后目录原引用恢复原文件身份");
            embeddedCode.Text = "{";
            embeddedCode.EmitSignal(CodeEdit.SignalName.TextChanged);
            editor.SwitchMode(false); await Settle();
            Check(!shared.Dirty && shared.Draft is null && original.Draft == "{" && editor.Session.HasUnsaved,
                "当前文件已保存时仍识别隐藏文件草稿");
            editor.Notification((int)Node.NotificationWMCloseRequest);
            var confirm = Descendants<ConfirmationDialog>(editor).Single(dialog => dialog.Title == "未保存的修改");
            Check(confirm.Visible, "退出检查整个会话并保护隐藏草稿");
            confirm.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirm.Hide();
            Check(original.Draft == "{" && File.ReadAllText(source).Contains("\"Damage\": 9"),
                "取消退出保留草稿且不写盘");
            if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("shared-editor-session");
        }
        finally { editor.Free(); }
        await Settle();
        GD.Print($"PASS: {_checks} shared editor session assertions");
    }
}
