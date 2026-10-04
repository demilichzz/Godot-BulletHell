using Godot;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>验证统一目录树、共享文件会话及冻结的未保存预览。</summary>
public partial class EditorVerification
{
    /// <summary>通过真实目录界面和临时文件检查跨文档工作流。</summary>
    /// <returns>全部界面操作稳定后的任务。</returns>
    private async Task VerifyCatalogWorkspace()
    {
        // 默认启动必须进入正式目录，测试中的写入仅使用临时目录。
        var editor = new EmitterEditor(); AddChild(editor);
        editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        await Settle();
        var panel = editor.BossPanel!;
        Check(editor.IsBossMode && panel.Document.Root["Bosses"]!.AsArray().Count == 23, "默认进入完整Boss目录");
        Check(!panel.HasUnsaved && !panel.HasDraft, "刚打开目录没有伪草稿");
        panel.SelectBoss(1);
        PressBoss(panel, "↑"); await Settle();
        Check(panel.SelectedBossIndex == 0 && panel.SelectedBossRoot["Core"]!["Id"]!.ToString() == "Boss_02", "排序跟随Boss身份");
        PressBoss(panel, "撤销"); await Settle();
        Check(panel.SelectedBossIndex == 1 && panel.SelectedBossRoot["Core"]!["Id"]!.ToString() == "Boss_02", "撤销排序恢复选择");
        PressBoss(panel, "复制"); await Settle();
        Check(panel.SelectedBossRoot["Core"]!["Id"]!.ToString() == "Boss_02_2", "复制Boss分配唯一ID");
        PressBoss(panel, "删除"); await Settle();
        PressBoss(panel, "撤销"); await Settle();
        Check(panel.SelectedBossRoot["Core"]!["Id"]!.ToString() == "Boss_02_2", "撤销删除恢复原Boss选择");
        PressBoss(panel, "重做"); await Settle();
        Check(panel.Document.Root["Bosses"]!.AsArray().Count == 23, "重做只删除复制的Boss");

        // 同一外部文件在两个阶段中引用，另一个文件用于检查独立草稿。
        string resource = "res://.tools/catalog-shared-emitter.json";
        string secondResource = "res://.tools/catalog-other-emitter.json";
        string emitterPath = ProjectSettings.GlobalizePath(resource), otherPath = ProjectSettings.GlobalizePath(secondResource);
        string catalogPath = ProjectSettings.GlobalizePath("res://.tools/catalog-workspace.json");
        _temporary.AddRange(new[] { emitterPath, otherPath, catalogPath });
        var fixture = new EmitterDocument(); fixture.Save(emitterPath); fixture.Save(otherPath);
        panel.Document.New();
        panel.Document.Edit(root =>
        {
            // 两个独立阶段共享文件引用，不共享阶段配置对象。
            var boss = root["Bosses"]![0]!.AsObject();
            var phases = boss["Phases"]!.AsArray();
            phases[0]!["Emitters"] = new JsonArray(resource, secondResource);
            phases[0]!["DurationMs"] = null; phases[0]!["EndCondition"] = "Health";
            phases.Add(phases[0]!.DeepClone()); BossEditorSchema.SumHealth(boss);
        });
        panel.Document.Save(catalogPath); panel.SelectBoss(0, 0);
        panel.SelectEmitter(0, 0, 0, "/VNodes"); await Settle();
        var shared = panel.EmitterPanel!.Document;
        Check(Descendants<Tree>(panel).Count(tree => tree.IsVisibleInTree()) == 1, "嵌入Emitter后只有统一左树可见");
        shared.Edit(root => root["Core"]!["Damage"] = 7); panel.EmitterPanel.Refresh(); await Settle();
        panel.SelectEmitter(0, 1, 0, "/VNodes"); await Settle();
        Check(ReferenceEquals(shared, panel.EmitterPanel.Document) && shared.Validate().Core.Damage == 7, "跨阶段引用共享未保存修改");
        Check(File.ReadAllText(emitterPath).Contains("\"Damage\": 1"), "编辑未自动写入外部文件");
        // 草稿来自真实CodeEdit，切换后仍能恢复。
        var code = Descendants<CodeEdit>(panel.EmitterPanel).Single();
        code.Text += " "; code.EmitSignal(CodeEdit.SignalName.TextChanged); await Settle();
        string draft = code.Text;
        panel.SelectEmitter(0, 0, 1); await Settle();
        Check(!ReferenceEquals(shared, panel.EmitterPanel.Document), "不同文件使用独立文档");
        panel.SelectEmitter(0, 0, 0); await Settle();
        Check(shared.Draft == draft && code.Text == draft, "跨文件切换保留未应用草稿");
        Reject(() => panel.Document.ValidateBoss(0, panel.Session.CaptureEmitters()), "未应用引用草稿阻止预览解析");
        Press(panel.EmitterPanel, "应用 JSON 草稿"); await Settle();
        shared.Undo();
        Check(shared.Validate().Core.Damage == 1, "共享文件保留原撤销历史");
        shared.Redo(); panel.EmitterPanel.Refresh(); await Settle();

        // 捕获一次预览后修改文档，现有阶段与下一阶段都继续使用冻结文本。
        panel.SelectBoss(0, 0); await Settle();
        PressBoss(panel, "单步 1/60s");
        Check(panel.Preview.Boss!.CurrentPhase!.Emitters[0].Core.Damage == 7, "Boss预览读取未保存Emitter");
        Check(GlobalEvent.GetBulletManager().ActiveBullets.Any(bullet => bullet.Damage == 7), "实际出生子弹使用未保存伤害");
        shared.Edit(root => root["Core"]!["Damage"] = 9);
        panel.Preview.Boss.TrySwitchAdjacentPhase(1); panel.Preview.Advance();
        Check(panel.Preview.Boss.CurrentPhase!.Emitters[0].Core.Damage == 7, "后续阶段仍使用原预览快照");
        PressBoss(panel, "重置预览");
        Check(panel.Preview.Boss!.CurrentPhase!.Emitters[0].Core.Damage == 9, "重置预览使用最新草稿结果");
        panel.StopPreview();

        // 保存前统一校验，任一文档无效时其他文件不能先被覆盖。
        var other = panel.Session.OpenEmitter(secondResource);
        string diskBefore = File.ReadAllText(emitterPath), catalogBefore = File.ReadAllText(catalogPath);
        other.Edit(root => root["Core"]!["Damage"] = -1);
        Reject(panel.Session.SaveAll, "无效Emitter阻止保存全部");
        Check(File.ReadAllText(emitterPath) == diskBefore && File.ReadAllText(catalogPath) == catalogBefore, "预校验失败没有部分写入");
        other.Undo(); panel.Session.SaveAll();
        Check(VBulletEmitter.Load(resource).Core.Damage == 9 && !panel.Session.HasUnsaved, "保存全部写入共享文件并清除脏状态");
        Check(BossCatalog.Load(catalogPath).Entries[0].Phases.Count == 2, "保存后的目录与引用可由正式入口加载");

        // 左树上的Creator操作只修改Creator；引用删除不删除文件。
        panel.SelectEmitter(0, 0, 0, "/VNodes"); await Settle();
        Press(panel.EmitterPanel!, "添加子节点"); await Settle();
        PressBoss(panel, "复制"); await Settle();
        Check(shared.Root["VNodes"]!["Children"]!.AsArray().Count == 2
            && panel.SelectedBossRoot["Phases"]![0]!["Emitters"]!.AsArray().Count == 2, "统一树复制Creator不复制Emitter引用");
        PressBoss(panel, "删除"); await Settle();
        Check(shared.Root["VNodes"]!["Children"]!.AsArray().Count == 1, "统一树删除Creator子树");
        panel.SelectEmitter(0, 0, 0); PressBoss(panel, "删除"); await Settle();
        Check(File.Exists(emitterPath) && panel.SelectedBossRoot["Phases"]![0]!["Emitters"]!.AsArray().Count == 1, "删除引用保留共享文件");
        PressBoss(panel, "撤销"); await Settle();
        Check(ReferenceEquals(shared, panel.EmitterPanel!.Document), "撤销引用删除恢复共享文档身份");
        // 切换目录只替换目录文档，文件草稿继续保留在当前会话。
        panel.Open(catalogPath); panel.SelectEmitter(0, 0, 0); await Settle();
        Check(ReferenceEquals(shared, panel.EmitterPanel!.Document) && shared.Dirty, "重开目录保留外部文件未保存编辑");
        if (_capture) await Capture("catalog-emitter-workspace");
        panel.SelectBoss(0); PressBoss(panel, "删除"); await Settle();
        Check(panel.Document.ValidateCatalog().Entries.Count == 0 && panel.SelectedBossIndex == -1, "删除最后Boss后保留可编辑空目录");
        PressBoss(panel, "+ 添加Boss"); await Settle(); panel.SelectPhase(0);
        PressBoss(panel, "删除"); await Settle();
        Check(panel.Document.ValidateBoss().PhaseCount == 1, "删除操作不能移除Boss最后阶段");
        panel.Document.ApplyText("{\"Bosses\":[42]}"); panel.SelectBoss(-1); await Settle();
        Check(panel.Document.Root["Bosses"]![0]!.GetValue<int>() == 42, "无效结构保留原文供修复且不破坏导航");
        Reject(() => panel.Document.ValidateCatalog(), "无效结构不能保存为正式目录");
        editor.Free(); await Settle();
        GD.Print($"PASS: {_checks} targeted catalog/editor assertions");
    }
}
