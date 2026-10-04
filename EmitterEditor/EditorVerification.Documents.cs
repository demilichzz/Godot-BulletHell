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
        var document = new EmitterDocument(); document.Save(path);
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

    /// <summary>检查跨文件预校验、部分写盘失败及下一次保存对外部引用的重新检查。</summary>
    private void VerifySaveSnapshots()
    {
        // 第一、第二文件打开编辑，第三文件保留为外部引用。
        string firstResource = "res://.tools/save-batch-first.json", secondResource = "res://.tools/save-batch-second.json";
        string externalResource = "res://.tools/save-batch-external.json";
        string firstPath = ProjectSettings.GlobalizePath(firstResource), secondPath = ProjectSettings.GlobalizePath(secondResource);
        string externalPath = ProjectSettings.GlobalizePath(externalResource), catalogPath = ProjectSettings.GlobalizePath("res://.tools/save-batch-catalog.json");
        _temporary.AddRange(new[] { firstPath, secondPath, externalPath, catalogPath });
        var fixture = new EmitterDocument(); fixture.Save(firstPath); fixture.Save(secondPath); fixture.Save(externalPath);
        var catalog = new EmitterDocument(true);
        catalog.Edit(root => root["Bosses"]![0]!["Phases"]![0]!["Emitters"] = new JsonArray(firstResource, secondResource, externalResource, firstResource));
        catalog.Save(catalogPath);
        var session = new EditorSession(); session.OpenCatalog(catalogPath);
        var first = session.OpenEmitter(firstResource); var second = session.OpenEmitter(secondResource);
        first.Edit(root => root["Core"]!["Damage"] = 7); second.Edit(root => root["Core"]!["Damage"] = 9);
        session.Catalog.Edit(root => root["Bosses"]![0]!["Core"]!["DisplayName"] = "已修改目录");
        string catalogBefore = File.ReadAllText(catalogPath), secondBefore = File.ReadAllText(secondPath);
        // Windows文件共享限制模拟第二次原子替换失败；第一文件已成功写入。
        using (var locked = new FileStream(secondPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
        {
            string diagnostic = "";
            try { session.SaveAll(); } catch (IOException error) { diagnostic = error.Message; }
            Check(diagnostic.StartsWith("保存全部未完成。已保存文件：" + Path.GetFullPath(firstPath) + "。", StringComparison.Ordinal),
                "部分保存失败明确报告已经写入的文件");
            Check(!first.Dirty && second.Dirty && session.Catalog.Dirty && session.HasUnsaved,
                "部分失败保留准确的逐文件保存状态");
            Check(VBulletEmitter.Load(firstResource).Core.Damage == 7 && File.ReadAllText(secondPath) == secondBefore
                && File.ReadAllText(catalogPath) == catalogBefore, "第二文件失败后目录尚未被写入");
        }
        session.SaveAll();
        Check(!session.HasUnsaved && VBulletEmitter.Load(secondResource).Core.Damage == 9
            && BossCatalog.Load(catalogPath).Entries[0].DisplayName == "已修改目录", "释放文件占用后可继续完成保存");
        // 上次成功校验不能掩盖磁盘引用的后续修改；失败发生在任何写入之前。
        first.Edit(root => root["Core"]!["Damage"] = 11);
        string firstBefore = File.ReadAllText(firstPath), externalBefore = File.ReadAllText(externalPath);
        fixture.Edit(root => root["Core"]!["Damage"] = -1); File.WriteAllText(externalPath, fixture.Text);
        Reject(session.SaveAll, "新保存批次须重新检查已变为无效的外部Emitter");
        Check(File.ReadAllText(firstPath) == firstBefore && first.Dirty, "外部引用校验失败不部分写盘");
        File.Delete(externalPath);
        Reject(session.SaveAll, "新保存批次须发现已删除的外部引用");
        File.WriteAllText(externalPath, externalBefore);
        first.Draft = "{";
        Reject(session.SaveAll, "新出现的引用草稿不能复用先前校验结果");
        first.Draft = null; session.SaveAll();
        Check(!session.HasUnsaved && VBulletEmitter.Load(firstResource).Core.Damage == 11, "修复依赖后能正常重新校验和保存");
        // 预览仍冻结文本且每次创建独立运行树，不与保存用的校验结果共享对象。
        var frozen = session.CaptureEmitters();
        var original = frozen(externalResource);
        fixture.Undo(); fixture.Edit(root => root["Core"]!["Damage"] = 4); fixture.Save(externalPath);
        var sameSnapshot = frozen(externalResource); var fresh = session.CaptureEmitters()(externalResource);
        Check(original.Core.Damage == 1 && sameSnapshot.Core.Damage == 1 && fresh.Core.Damage == 4,
            "现有预览冻结外部文本，新的预览读取最新文件");
        Check(!ReferenceEquals(original, sameSnapshot) && !ReferenceEquals(original.Root, sameSnapshot.Root),
            "同一冻结文本也必须创建独立Emitter与Creator树");
        // 纯校验回调不能跳过Boss自身字段、阶段或重复引用的协议检查。
        int references = 0;
        BossCatalog.CheckJson(session.Catalog.Text, catalogPath, _ => references++);
        Check(references == 4, "目录纯检查入口覆盖每个阶段引用，批次自行去重资源解析");
        session.Catalog.Edit(root => root["Bosses"]![0]!["Phases"]![0]!["Unexpected"] = true);
        Reject(session.SaveAll, "批次复用不能放宽目录未知字段检查");
        GD.Print($"PASS: {_checks} targeted document/save assertions");
    }
}
