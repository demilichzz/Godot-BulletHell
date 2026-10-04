using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;

/// <summary>验证文档修订边界、冻结保存和批次校验失效，测试文件仅位于忽略目录。</summary>
public partial class EditorVerification
{
    /// <summary>确认所有有效修改进入历史，外借节点与失败回调不能改变已发布版本。</summary>
    private void VerifyDocumentTransactions()
    {
        // 已保存基线及独立另存目标，测试结束统一清理。
        string path = ProjectSettings.GlobalizePath("res://.tools/document-transactions.json");
        string alternate = ProjectSettings.GlobalizePath("res://.tools/document-transactions-copy.json");
        _temporary.AddRange(new[] { path, alternate });
        var document = new EditorDocument(); document.Save(path);
        string original = document.Text;
        long revision = document.Revision;
        // 事务外拿到的根与局部节点都必须是独立快照。
        var external = document.Root;
        external["Core"]!["Damage"] = 40;
        document.At("/Core")!["Damage"] = 50;
        Check(document.Text == original && document.Revision == revision && !document.Dirty && !document.CanUndo,
            "事务外节点不能绕过历史和保存状态修改文档");
        // 即使回调保留了工作节点或插入节点的引用，提交后也不能污染文档。
        JsonObject? retained = null;
        var inserted = JsonValue.Create(7);
        document.Selection = "before";
        document.Edit(root =>
        {
            retained = root; root["Core"]!["Damage"] = inserted;
            document.At("/Core")!["Name"] = "事务内";
            Check(document.Validate().Core.Damage == 7 && document.Text.Contains("事务内"), "事务内校验必须读取最新工作副本");
            document.Selection = "after";
        });
        retained!["Core"]!["Damage"] = 90;
        retained["Core"]!["Name"] = "外部污染";
        Check(document.Validate().Core.Damage == 7 && document.At("/Core/Name")!.ToString() == "事务内"
            && document.Revision == revision + 1, "提交后断开回调持有的可变引用");
        document.Undo();
        Check(document.Text == original && !document.Dirty && document.Selection == "before", "撤销回到保存基线同时恢复选择");
        revision = document.Revision;
        document.Edit(_ => { }); document.ApplyText(original + "\n");
        Check(document.Revision == revision && document.CanRedo && !document.Dirty, "无实际变化的提交保留重做、修订和保存状态");
        document.Redo();
        Check(document.Dirty && document.Validate().Core.Damage == 7 && document.Revision == revision + 1, "重做发布新的有效修订");
        document.Save(path); revision = document.Revision;
        Check(!document.Dirty && document.CanUndo, "保存保留撤销历史");
        // 回调异常及嵌套文件、历史操作都必须完整回滚，已提交文本仍可复用。
        string before = document.Text, selection = document.Selection;
        Action[] invalidOperations =
        {
            () => throw new InvalidOperationException("预期回滚"), document.New, document.Undo, document.Redo,
            () => document.Open(path), () => document.Save(path), () => document.ApplyText(original), () => document.Edit(_ => { })
        };
        foreach (var invalid in invalidOperations)
        {
            Reject(() => document.Edit(root =>
            {
                root["Core"]!["Damage"] = 15; document.Selection = "错误选择"; invalid();
            }), "失败或重入操作必须拒绝");
            Check(document.Text == before && document.Revision == revision && document.Selection == selection && !document.Dirty,
                "失败操作不得发布文本、修订、选择或脏状态");
        }
        // 已校验操作不能写入后来编辑的新内容，也不能在草稿或另存后继续使用。
        var stale = document.PrepareSave(path);
        document.Edit(root => root["Core"]!["Damage"] = 8); document.Undo();
        Reject(stale.Write, "即使撤销回相同内容，旧保存操作也应失效");
        Check(File.ReadAllText(path).TrimEnd() == before, "陈旧保存不能改变磁盘");
        var drafted = document.PrepareSave(path);
        document.Draft = "{";
        revision = document.Revision;
        Reject(drafted.Write, "准备后的草稿阻止写盘");
        Reject(() => document.Save(path), "直接保存也不能跳过未应用草稿");
        Check(document.Revision == revision && document.Text == before, "未应用草稿不改变已应用修订");
        document.Draft = null;
        var relocated = document.PrepareSave(path);
        document.Save(alternate);
        Reject(relocated.Write, "另存后旧文件身份的保存操作失效");
        Check(document.FilePath == Path.GetFullPath(alternate) && !document.Dirty, "被拒绝的旧保存不能改回文件身份");
        Reject(() => document.PrepareSave(alternate, _ => document.Edit(root => root["Core"]!["Damage"] = 9)),
            "校验期间内容改变必须重新准备保存");
        Check(document.Dirty && VBulletEmitter.FromJson(File.ReadAllText(alternate)).Core.Damage == 7,
            "校验阶段的内容变化尚未写盘");
        // 测量频繁标题及原文比较的读取分配，不把耗时阈值写成不稳定断言。
        long allocated = GC.GetAllocatedBytesForCurrentThread(), length = 0;
        for (int index = 0; index < 10000; index++) length += document.Text.Length;
        GD.Print($"Document text reads: 10000, bytes allocated: {GC.GetAllocatedBytesForCurrentThread() - allocated}, characters: {length}");
    }

    /// <summary>检查Boss模式跨文件冻结、部分写盘失败及磁盘Emitter隔离。</summary>
    private void VerifySaveSnapshots()
    {
        // 临时目录、两个Boss及一个共享Emitter，所有写入只在忽略目录。
        string firstResource = "res://.tools/save-boss-first.json", secondResource = "res://.tools/save-boss-second.json";
        string emitterResource = "res://.tools/save-disk-emitter.json";
        string firstPath = ProjectSettings.GlobalizePath(firstResource), secondPath = ProjectSettings.GlobalizePath(secondResource);
        string emitterPath = ProjectSettings.GlobalizePath(emitterResource), catalogPath = ProjectSettings.GlobalizePath("res://.tools/save-boss-catalog.json");
        _temporary.AddRange(new[] { firstPath, secondPath, emitterPath, catalogPath });
        var emitter = new EditorDocument(); emitter.Save(emitterPath);
        var fixture = new EditorDocument(EditorDocumentKind.Boss);
        fixture.Edit(root => root["Phases"]![0]!["Emitters"] = new JsonArray(emitterResource)); fixture.Save(firstPath);
        fixture.Edit(root => root["Core"]!["Id"] = "Second"); fixture.Save(secondPath);
        var catalog = new EditorDocument(EditorDocumentKind.BossCatalog);
        catalog.Edit(root => root["Bosses"] = new JsonArray(firstResource, secondResource)); catalog.Save(catalogPath);
        var session = new BossEditorSession(); session.Open(catalogPath);
        var first = session.OpenBoss(firstResource); var second = session.OpenBoss(secondResource);
        first.Edit(root => root["Core"]!["DisplayName"] = "修改甲"); second.Edit(root => root["Core"]!["DisplayName"] = "修改乙");
        session.Root!.Edit(root => EditorArrayControls.Move(root["Bosses"]!.AsArray(), 1, -1));
        string catalogBefore = File.ReadAllText(catalogPath), secondBefore = File.ReadAllText(secondPath);
        using (var locked = new FileStream(secondPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
        {
            string diagnostic = ""; try { session.SaveAll(); } catch (IOException error) { diagnostic = error.Message; }
            Check(diagnostic.StartsWith("保存全部未完成。已保存文件：" + Path.GetFullPath(firstPath) + "。", StringComparison.Ordinal), "部分保存报告已完成文件：" + diagnostic);
            Check(!first.Dirty && second.Dirty && session.Root.Dirty && session.HasUnsaved, "失败保留逐文件准确状态");
            Check(File.ReadAllText(secondPath) == secondBefore && File.ReadAllText(catalogPath) == catalogBefore, "Boss失败后目录尚未写入");
        }
        session.SaveAll();
        Check(!session.HasUnsaved && BossCatalog.Load(catalogPath).Entries[0].DisplayName == "修改乙", "重试按Boss再目录完成写入");
        first.Edit(root => root["Core"]!["DisplayName"] = "再次修改"); string before = File.ReadAllText(firstPath);
        emitter.Edit(root => root["Core"]!["Damage"] = 0); File.WriteAllText(emitterPath, emitter.Text);
        Reject(session.SaveAll, "磁盘Emitter错误阻止Boss保存"); Check(File.ReadAllText(firstPath) == before && first.Dirty, "预校验失败无部分写入");
        File.Delete(emitterPath); Reject(session.SaveAll, "引用缺失使下一保存失败"); emitter.Undo(); emitter.Save(emitterPath);
        first.Draft = "{"; Reject(session.SaveAll, "Boss草稿阻止批量保存"); first.Draft = null;
        var emitterSession = new EditorSession(); var draft = emitterSession.OpenEmitter(emitterResource);
        draft.Edit(root => root["Core"]!["Damage"] = 9); draft.Draft = "{";
        session.SaveAll(); Check(!session.HasUnsaved && draft.Dirty && draft.Draft == "{" && VBulletEmitter.Load(emitterResource).Core.Damage == 1, "保存全部不读写Emitter草稿");
        // 同次工厂冻结磁盘文本，每次创建独立运行对象。
        var frozen = BossEditorSession.CaptureDiskEmitters(); var original = frozen(emitterResource);
        emitter.Edit(root => root["Core"]!["Damage"] = 4); emitter.Save(emitterPath);
        var same = frozen(emitterResource); var fresh = BossEditorSession.CaptureDiskEmitters()(emitterResource);
        Check(original.Core.Damage == 1 && same.Core.Damage == 1 && fresh.Core.Damage == 4, "预览快照固定磁盘内容，新预览读取最新文件");
        Check(!ReferenceEquals(original.Root, same.Root), "同一冻结文本创建独立树");
        first.Edit(root => root["Phases"]![0]!["Unexpected"] = true); Reject(session.SaveAll, "批次校验不放宽未知字段");
        GD.Print($"PASS: {_checks} targeted document/save assertions");
    }
}
