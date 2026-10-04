using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>验证独立Boss文件与路径列表的真实编辑工作流。</summary>
public partial class EditorVerification
{
    /// <summary>建立只位于忽略目录的Boss、目录和Emitter夹具。</summary>
    /// <param name="prefix">本测试唯一文件名前缀。</param>
    /// <returns>目录、Boss、Emitter的资源路径。</returns>
    private (string Catalog, string Boss, string Emitter) CreateBossWorkspace(string prefix)
    {
        string catalog = "res://.tools/" + prefix + "-catalog.json", boss = "res://.tools/" + prefix + "-boss.json", emitter = "res://.tools/" + prefix + "-emitter.json";
        _temporary.AddRange(new[] { catalog, boss, emitter }.Select(ProjectSettings.GlobalizePath));
        new EditorDocument().Save(ProjectSettings.GlobalizePath(emitter));
        var data = new EditorDocument(EditorDocumentKind.Boss);
        data.Edit(root => root["Phases"]![0]!["Emitters"] = new JsonArray(emitter)); data.Save(ProjectSettings.GlobalizePath(boss));
        var list = new EditorDocument(EditorDocumentKind.BossCatalog); list.Edit(root => root["Bosses"] = new JsonArray(boss)); list.Save(ProjectSettings.GlobalizePath(catalog));
        return (catalog, boss, emitter);
    }
    /// <summary>实际文件窗口选择成功；所有路径仅指向本次临时夹具。</summary>
    /// <param name="editor">编辑器宿主。</param>
    /// <param name="path">测试目标。</param>
    /// <param name="save">是否为保存窗口。</param>
    private static void ChooseFile(EmitterEditor editor, string path, bool save)
    {
        var dialog = Descendants<FileDialog>(editor).Single(window => window.FileMode == (save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile));
        dialog.EmitSignal(FileDialog.SignalName.FileSelected, ProjectSettings.GlobalizePath(path)); dialog.Hide();
    }
    /// <summary>验证目录操作与Boss操作分别保存和撤销，单文件不制造目录。</summary>
    /// <returns>所有界面刷新完成的任务。</returns>
    private async Task VerifyCatalogWorkspace()
    {
        var fixture = CreateBossWorkspace("split-workspace");
        var editor = new EmitterEditor(); AddChild(editor); editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); await Settle();
        var panel = editor.BossPanel!;
        Check(editor.IsBossMode && panel.Document.Root["Bosses"]!.AsArray().Count == 23, "默认打开路径目录");
        panel.Open(fixture.Catalog); panel.SelectBoss(0, 0); await Settle();
        var boss = panel.Document; var catalog = panel.Session.Root!;
        Check(boss.Kind == EditorDocumentKind.Boss && !ReferenceEquals(boss, catalog), "Boss与目录具有独立文档");
        Check(Descendants<EmitterPanel>(panel).Count() == 0 && Descendants<Tree>(panel).Count() == 1, "Boss模式没有嵌入Emitter且只有三级主树");
        var tree = Descendants<Tree>(panel).Single(); var bossItem = tree.GetRoot().GetFirstChild();
        Check(bossItem.GetFirstChild().GetFirstChild() is null, "阶段是叶节点，没有Emitter或Creator子树");
        SetBossField(panel, "/Phases/0/Name", "独立阶段"); await Settle();
        Check(boss.Dirty && !catalog.Dirty, "阶段编辑只修改Boss文件");
        PressBoss(panel, "保存当前文件"); await Settle();
        Check(!boss.Dirty && BossData.Load(fixture.Boss).Phases[0].Name == "独立阶段", "保存选中阶段写入其Boss文件");
        var code = Descendants<CodeEdit>(panel).Single(); code.Text += " "; string draft = code.Text;
        panel.SelectBoss(-1); await Settle(); Check(ReferenceEquals(panel.Document, catalog) && code.Text.Contains("Bosses"), "根JSON页只编辑目录路径");
        panel.SelectBoss(0, 0); await Settle(); Check(boss.Draft == draft && code.Text == draft, "切换文件保留原始草稿");
        PressBoss(panel, "应用 JSON 草稿"); await Settle();
        // 同一路径从目录和单文件入口打开，保持同一文档和撤销栈。
        panel.Open(fixture.Boss); panel.SelectPhase(0); await Settle();
        Check(ReferenceEquals(panel.Document, boss) && panel.Session.Root!.Kind == EditorDocumentKind.Boss, "直接打开Boss复用已打开身份");
        Check(tree.GetRoot().GetText(0).Contains("单文件") && Descendants<Button>(panel).Single(button => button.Text == "+ 引用Boss文件…").Disabled, "单文件虚拟根禁用目录引用操作");
        panel.SelectBoss(0); await Settle();
        Check(new[] { "复制", "删除", "↑", "↓" }.All(text => Descendants<Button>(panel).Single(button => button.Text == text).Disabled), "单文件Boss节点禁用目录复制删除及排序");
        panel.SelectPhase(0); await Settle();
        Check(!Descendants<Button>(panel).Single(button => button.Text == "复制").Disabled, "单文件阶段仍允许内容复制");
        panel.Open(fixture.Catalog); panel.SelectBoss(0); await Settle();
        // 复制需要文件窗口，取消不增加引用，成功创建后才加入列表。
        PressBoss(panel, "复制");
        var save = Descendants<FileDialog>(editor).Single(window => window.FileMode == FileDialog.FileModeEnum.SaveFile);
        Check(save.Visible && catalog.Root["Bosses"]!.AsArray().Count == 1, "复制前没有隐式引用");
        save.EmitSignal(FileDialog.SignalName.Canceled); save.Hide();
        Check(catalog.Root["Bosses"]!.AsArray().Count == 1, "取消复制不修改目录");
        PressBoss(panel, "复制"); ChooseFile(editor, fixture.Boss, true); await Settle();
        Check(catalog.Root["Bosses"]!.AsArray().Count == 1 && BossData.Load(fixture.Boss).Phases[0].Name == "独立阶段", "复制保存失败不增加引用或覆盖原Boss");
        string copy = "res://.tools/split-copy.json"; _temporary.Add(ProjectSettings.GlobalizePath(copy));
        PressBoss(panel, "复制"); ChooseFile(editor, copy, true); await Settle();
        Check(catalog.Root["Bosses"]!.AsArray().Count == 2 && BossData.Load(copy).Id != BossData.Load(fixture.Boss).Id, "复制创建独立文件和唯一ID");
        PressBoss(panel, "↑"); await Settle();
        Check(ReferenceEquals(panel.Document, catalog) && catalog.Root["Bosses"]![0]!.ToString() == copy, "排序只修改目录并定位到列表保存");
        PressBoss(panel, "撤销"); await Settle(); Check(catalog.Root["Bosses"]![1]!.ToString() == copy, "目录撤销恢复顺序");
        PressBoss(panel, "重做"); await Settle();
        Check(ReferenceEquals(panel.Document, catalog) && catalog.Root["Bosses"]![0]!.ToString() == copy, "目录重做仍作用于目录文件");
        PressBoss(panel, "撤销"); await Settle();
        panel.SelectBoss(1); PressBoss(panel, "删除"); await Settle();
        Check(File.Exists(ProjectSettings.GlobalizePath(copy)) && catalog.Root["Bosses"]!.AsArray().Count == 1, "移除引用不删除Boss文件");
        PressBoss(panel, "保存全部"); await Settle();
        Check(!panel.Session.HasUnsaved && BossCatalog.Load(fixture.Catalog).Entries.Count == 1, "保存全部处理Boss文档及目录");
        // 另存为进入独立副本，不重定向目录。
        panel.SelectBoss(0); string alternate = "res://.tools/split-alternate.json"; _temporary.Add(ProjectSettings.GlobalizePath(alternate));
        PressBoss(panel, "另存为…"); ChooseFile(editor, alternate, true); await Settle();
        Check(panel.Session.Root!.FilePath == EditorDocument.FullPath(alternate) && catalog.Root["Bosses"]![0]!.ToString() == fixture.Boss, "另存成功独立打开且保留原目录引用");
        panel.Open(fixture.Catalog); panel.SelectBoss(-1); catalog.Edit(root => root["Bosses"]!.AsArray().Add("res://.tools/missing-boss.json")); panel.Refresh(); await Settle();
        panel.SelectBoss(1); await Settle();
        Check(tree.GetRoot().GetFirstChild().GetNext().GetText(0).Contains("无效Boss"), "失效引用显示可修复占位");
        panel.SelectBoss(0, 0); SetBossField(panel, "/Phases/0/Name", "仍可编辑"); await Settle();
        Check(boss.Root["Phases"]![0]!["Name"]!.ToString() == "仍可编辑", "其他失效引用不阻止有效Boss编辑");
        if (_capture) await Capture("split-boss-workspace");
        // 错误类型不能成为隐藏会话文档；被替换的未命名文件仅在确认且打开成功后移除。
        var isolated = new BossEditorSession();
        Reject(() => isolated.OpenBoss(fixture.Catalog), "目录不能作为Boss引用");
        Check(!isolated.Documents.Any(), "错误类型不登记到Boss会话");
        PressBoss(panel, "新建 Boss"); await Settle();
        var unnamed = panel.Session.Root!;
        var confirm = Descendants<ConfirmationDialog>(editor).Single(window => window.Title == "未保存的修改");
        code.Text = "{"; await Settle();
        panel.Open(fixture.Catalog); await Settle();
        Check(confirm.Visible && ReferenceEquals(panel.Session.Root, unnamed), "替换未命名Boss先保护草稿");
        confirm.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirm.Hide(); await Settle();
        Check(ReferenceEquals(panel.Session.Root, unnamed) && unnamed.Draft == "{", "取消保留未命名草稿");
        panel.Open("res://.tools/missing-boss.json"); await Settle();
        confirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); confirm.Hide(); await Settle();
        Check(ReferenceEquals(panel.Session.Root, unnamed) && panel.Session.Documents.Contains(unnamed) && unnamed.Draft == "{", "确认后打开失败仍保留原文档");
        panel.Open(fixture.Catalog); await Settle();
        confirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); confirm.Hide(); await Settle();
        Check(ReferenceEquals(panel.Session.Root, catalog) && !panel.Session.Documents.Contains(unnamed), "确认并成功打开后移除已丢弃的未命名文件");
        // 关闭检查涵盖其他模式隐藏文档；取消不会退出或清空历史。
        editor.SwitchMode(false); await Settle();
        editor._Notification((int)Node.NotificationWMCloseRequest); await Settle();
        Check(confirm.Visible && panel.Session.HasUnsaved, "关闭时保护隐藏Boss文件修改");
        confirm.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirm.Hide();
        editor.Free(); await Settle(); GD.Print($"PASS: {_checks} split catalog workspace assertions");
    }
}
