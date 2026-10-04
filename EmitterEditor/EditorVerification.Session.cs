using Godot;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>验证两种模式隔离、引用跳转与已保存Emitter快照。</summary>
public partial class EditorVerification
{
    /// <summary>通过真实按钮验证跳转、取消、模式切换及关闭保护。</summary>
    /// <returns>交互验证完成任务。</returns>
    private async Task VerifySharedModes()
    {
        var fixture = CreateBossWorkspace("split-modes");
        var editor = new EmitterEditor(); AddChild(editor); editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); await Settle();
        var panel = editor.BossPanel!; panel.Open(fixture.Boss); panel.SelectPhase(0); await Settle();
        var boss = panel.Document;
        boss.Edit(root => { root["Phases"]!.AsArray().Add(root["Phases"]![0]!.DeepClone()); BossEditorSchema.SumHealth(root); }); panel.Refresh();
        PressBoss(panel, "在 Emitter 模式编辑"); await Settle();
        Check(!editor.IsBossMode && editor.Document.FilePath == EditorDocument.FullPath(fixture.Emitter), "引用按钮切换到独立Emitter模式");
        var emitter = editor.Document; emitter.Edit(root => root["Core"]!["Damage"] = 7); editor.Refresh();
        var emitterCode = Descendants<CodeEdit>(editor.EmitterContent).Single(); emitterCode.Text = "{";
        editor.SwitchMode(true); await Settle();
        Check(ReferenceEquals(panel.Document, boss) && panel.SelectedPhase == 0 && panel.Preview.Boss is null, "返回Boss保留选择且不自动播放");
        PressBoss(panel, "单步 1/60s");
        Check(panel.Preview.Boss!.CurrentPhase!.Emitters[0].Core.Damage == 1, "Boss忽略Emitter未保存修改和未应用草稿");
        PressBoss(panel, "保存当前文件");
        Check(emitter.Dirty && emitter.Draft == "{" && VBulletEmitter.Load(fixture.Emitter).Core.Damage == 1, "Boss保存不写入Emitter");
        PressBoss(panel, "单步 1/60s");
        // 运行中的Boss预览不随磁盘变化混入新数据，下一阶段仍使用同一文本快照。
        var disk = new EditorDocument(); disk.Open(ProjectSettings.GlobalizePath(fixture.Emitter));
        disk.Edit(root => root["Core"]!["Damage"] = 4); disk.Save(ProjectSettings.GlobalizePath(fixture.Emitter));
        panel.Preview.Boss.TrySwitchAdjacentPhase(1); panel.Preview.Advance();
        Check(panel.Preview.Boss.CurrentPhase!.Emitters[0].Core.Damage == 1, "后续阶段仍使用预览开始时的磁盘快照");
        PressBoss(panel, "重置预览"); Check(panel.Preview.Boss!.CurrentPhase!.Emitters[0].Core.Damage == 4, "重置预览读取新磁盘文件");
        panel.StopPreview(); PressBoss(panel, "在 Emitter 模式编辑"); await Settle();
        Check(ReferenceEquals(editor.Document, emitter) && emitterCode.Text == "{", "跳转已打开文件保留Emitter草稿和历史");
        emitter.Draft = null; editor.Refresh(); Press(editor.EmitterContent, "保存");
        editor.SwitchMode(true); await Settle(); PressBoss(panel, "单步 1/60s");
        Check(panel.Preview.Boss!.CurrentPhase!.Emitters[0].Core.Damage == 7, "Emitter保存后返回Boss，新预览读取保存结果");
        panel.StopPreview();
        // 切换目标会替换未命名Emitter时，取消确认必须保留两个模式。
        editor.SwitchMode(false); editor.NewEmitter(); var untitled = editor.Document; untitled.Draft = "{"; editor.Refresh();
        editor.SwitchMode(true); PressBoss(panel, "在 Emitter 模式编辑"); await Settle();
        var confirm = Descendants<ConfirmationDialog>(editor).Single(dialog => dialog.Title == "未保存的修改");
        Check(confirm.Visible && editor.IsBossMode, "未命名Emitter替换前需要确认"); confirm.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirm.Hide();
        Check(ReferenceEquals(editor.Document, untitled) && untitled.Draft == "{", "取消跳转保留未命名内容");
        // 文件存在但业务参数错误仍可跳转；错误类型和解析失败不能跳转。
        disk.Edit(root => root["Core"]!["Damage"] = 0); File.WriteAllText(ProjectSettings.GlobalizePath(fixture.Emitter), disk.Text);
        panel.Refresh(); await Settle();
        Check(!Descendants<Button>(panel).Single(button => button.Text == "在 Emitter 模式编辑").Disabled, "业务错误Emitter允许进入目标模式修复");
        boss.Edit(root => root["Phases"]![0]!["Emitters"] = new JsonArray(fixture.Catalog)); panel.Refresh(); await Settle();
        Check(Descendants<Button>(panel).Single(button => button.Text == "在 Emitter 模式编辑").Disabled, "错误文件类型禁用跳转");
        boss.Undo(); disk.Undo(); disk.Save(ProjectSettings.GlobalizePath(fixture.Emitter)); panel.Refresh(); await Settle();
        var jump = Descendants<Button>(panel).Single(button => button.Text == "在 Emitter 模式编辑");
        File.Delete(ProjectSettings.GlobalizePath(fixture.Emitter)); jump.EmitSignal(BaseButton.SignalName.Pressed);
        Check(editor.IsBossMode && ReferenceEquals(editor.Document, untitled), "点击前引用失效不会替换当前模式或文档");
        disk.Save(ProjectSettings.GlobalizePath(fixture.Emitter));
        if (_capture) await Capture("split-mode-isolation");
        editor.Free(); await Settle(); GD.Print($"PASS: {_checks} isolated editor mode assertions");
    }
}
