using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>Boss编辑模式的文档、真实控件、正式预览和模式隔离验证。</summary>
public partial class EditorVerification
{
    /// <summary>查找带JSON指针的实际输入控件。</summary>
    /// <typeparam name="T">输入框或下拉框类型。</typeparam>
    /// <param name="panel">Boss模式根节点。</param>
    /// <param name="path">字段JSON指针。</param>
    /// <returns>唯一匹配的表单控件。</returns>
    private static T BossField<T>(BossEditorPanel panel, string path) where T : Control
        => Descendants<T>(panel).Single(control => control.HasMeta("json_path") && control.GetMeta("json_path").AsString() == path);
    /// <summary>点击Boss模式内的真实按钮。</summary>
    /// <param name="panel">Boss工作区。</param>
    /// <param name="text">按钮文本。</param>
    private static void PressBoss(BossEditorPanel panel, string text)
        => Descendants<Button>(panel).First(button => button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
    /// <summary>通过表单回车修改字段。</summary>
    /// <param name="panel">Boss工作区。</param>
    /// <param name="path">字段指针。</param>
    /// <param name="text">输入文本。</param>
    private static void SetBossField(BossEditorPanel panel, string path, string text)
    {
        var input = BossField<LineEdit>(panel, path);
        input.Text = text; input.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
    }
    /// <summary>通过真实菜单选取一个枚举值。</summary>
    /// <param name="panel">Boss工作区。</param>
    /// <param name="path">字段指针。</param>
    /// <param name="index">菜单零基序号。</param>
    private static void ChooseBossField(BossEditorPanel panel, string path, int index)
    {
        var menu = BossField<OptionButton>(panel, path);
        menu.Select(index); menu.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    /// <summary>验证Boss模式的完整用户工作流，保存只写本次测试目录。</summary>
    /// <returns>界面验证完成后的任务。</returns>
    private async Task VerifyBossEditor()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://.tools"));
        var document = new EmitterDocument(true);
        Check(document.ValidateBoss().PhaseCount == 1, "新Boss模板可运行");
        // 所有真实Boss原文往返，不展开或改写引用的Emitter。
        foreach (string file in Directory.GetFiles(ProjectSettings.GlobalizePath("res://Data/Bosses"), "B*.json"))
        {
            document.Open(file);
            Check(JsonNode.DeepEquals(document.Root, JsonNode.Parse(File.ReadAllText(file))), "Boss原文无损读取");
            document.ValidateBoss();
        }
        string savedPath = ProjectSettings.GlobalizePath("res://.tools/B99.json"); _temporary.Add(savedPath);
        document.Save(savedPath);
        Check(!document.Dirty && BossData.Load(savedPath).PhaseCount == document.ValidateBoss().PhaseCount, "保存Boss由正式加载器读取");
        string saved = File.ReadAllText(savedPath);
        document.Edit(root => root["Phases"]![0]!["Hp"] = -1);
        Reject(() => document.Save(savedPath), "无效Boss不能覆盖文件");
        Check(File.ReadAllText(savedPath) == saved, "失败保存保留原文件");
        document.Undo();
        Reject(() => document.Save(ProjectSettings.GlobalizePath("res://.tools/boss.json")), "强制Bxx命名");
        // 真实编辑器节点提供完整模式栏和工作区。
        var editor = new EmitterEditor(); AddChild(editor); editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        await Settle();
        string emitterText = editor.Document.Text;
        var emitterCode = Descendants<CodeEdit>(editor).Single();
        emitterCode.Text += " ";
        var modes = Descendants<OptionButton>(editor).Single(menu => menu.ItemCount == 2 && menu.GetItemText(0) == "Emitter 弹幕编辑");
        modes.Select(1); modes.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        await Settle();
        var panel = editor.BossPanel!;
        Check(editor.IsBossMode && panel.IsVisibleInTree() && !editor.Canvas.IsVisibleInTree(), "整体切到Boss工作区");
        panel.SelectPhase(0);
        SetBossField(panel, "/Phases/0/Name", "测试阶段甲"); await Settle();
        SetBossField(panel, "/Phases/0/Hp", "120"); await Settle();
        Check(panel.Document.Root["Core"]!["MaxHp"]!.GetValue<int>() == 120, "阶段血量同步Boss总血量：" + panel.Document.Text + "；" + string.Join(" | ", Descendants<Label>(panel).Select(label => label.Text)));
        SetBossField(panel, "/Phases/0/Hp", "invalid"); await Settle();
        Reject(() => panel.Document.ValidateBoss(), "非法血量保留为待修复文档而非静默忽略");
        PressBoss(panel, "撤销"); await Settle();
        Check(panel.Document.ValidateBoss().MaxHp == 120, "撤销非法输入恢复有效Boss");
        SetBossField(panel, "/Phases/0/DurationMs", "100"); await Settle();
        ChooseBossField(panel, "/Phases/0/EndCondition", 1); await Settle();
        Check(panel.Document.ValidateBoss().Phases[0].EndCondition == "Time", "条件下拉写入Time");
        // 文件引用通过FileDialog写入res://路径，保持Emitter独立。
        string emitterPath = "res://Data/Emitters/B01P01_Emitter01.json";
        string emitterBefore = File.ReadAllText(ProjectSettings.GlobalizePath(emitterPath));
        PressBoss(panel, "+ 引用Emitter文件…");
        var picker = Descendants<FileDialog>(panel).Single(dialog => dialog.Access == FileDialog.AccessEnum.Resources);
        picker.EmitSignal(FileDialog.SignalName.FileSelected, emitterPath); picker.Hide(); await Settle();
        Check(panel.Document.ValidateBoss().Phases[0].Emitters.Single() == emitterPath, "界面选择独立Emitter路径");
        PressBoss(panel, "复制"); await Settle();
        Check(panel.Document.ValidateBoss().PhaseCount == 2 && panel.SelectedPhase == 1, "复制阶段深拷贝且同步总HP");
        SetBossField(panel, "/Phases/1/Name", "测试阶段乙"); await Settle();
        PressBoss(panel, "↑"); await Settle();
        Check(panel.SelectedPhase == 0 && panel.Document.Root["Phases"]![0]!["Name"]!.GetValue<string>() == "测试阶段乙", "阶段队列稳定排序");
        PressBoss(panel, "删除"); await Settle();
        Check(panel.Document.ValidateBoss().PhaseCount == 1 && panel.Document.Root["Phases"]![0]!["Name"]!.GetValue<string>() == "测试阶段甲", "删除阶段保留另一独立副本");
        PressBoss(panel, "撤销"); await Settle(); Check(panel.Document.ValidateBoss().PhaseCount == 2, "撤销恢复阶段");
        PressBoss(panel, "重做"); await Settle(); Check(panel.Document.ValidateBoss().PhaseCount == 1, "重做删除");
        PressBoss(panel, "+ 添加阶段"); await Settle();
        Check(panel.Document.ValidateBoss().PhaseCount == 2 && panel.SelectedPhase == 1, "添加阶段模板可运行");
        // 每种移动模式都从实际菜单切换后通过正式加载器校验。
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            ChooseBossField(panel, "/Phases/1/Movement/Type", mode); await Settle();
            Check(panel.Document.ValidateBoss().Phases.Count == 2, "移动模式模板有效");
        }
        ChooseBossField(panel, "/Phases/1/Movement/PathQueue/0/PathMode", 1); await Settle();
        Check(panel.Document.ValidateBoss().Phases[1].Movement.GetProperty("PathQueue")[0].GetProperty("PathMode").GetString() == "Bezier", "Bezier曲线表单");
        ChooseBossField(panel, "/Phases/1/Movement/PathQueue/0/PathMode", 2); await Settle();
        SetBossField(panel, "/Phases/1/Movement/PathQueue/0/Y", "60*sin(PI*t)"); await Settle();
        Check(panel.Document.Text.Contains("60*sin(PI*t)"), "函数表达式保留原文");
        // VPath瞄准玩家简写同样可以通过实际表单编辑。
        PressBoss(panel, "切为瞄准玩家直线"); await Settle();
        SetBossField(panel, "/Phases/1/Movement/PathQueue/0/X", "20"); await Settle();
        Check(panel.Document.ValidateBoss().Phases[1].Movement.GetProperty("PathQueue")[0].GetProperty("X").GetDouble() == 20, "瞄准玩家路径数值偏移表单");
        PressBoss(panel, "切为普通路径"); await Settle();
        // Boss与Emitter草稿在整体模式来回切换中互不覆盖。
        var bossCode = Descendants<CodeEdit>(panel).Single(); bossCode.Text += " ";
        editor.SwitchMode(false);
        Check(editor.Document.Text == emitterText && emitterCode.Text.EndsWith(" "), "返回Emitter保留文档及未应用草稿");
        editor.SwitchMode(true);
        Check(panel.HasDraft && bossCode.Text.EndsWith(" "), "返回Boss保留独立草稿");
        PressBoss(panel, "单步 1/60s"); Check(panel.Preview.Boss is null, "未应用草稿阻止Boss预览");
        PressBoss(panel, "应用 JSON 草稿"); await Settle();
        // 保存按钮回调及文件名规则。
        string uiPath = ProjectSettings.GlobalizePath("res://.tools/B98.json"); _temporary.Add(uiPath);
        PressBoss(panel, "保存Boss");
        var saveDialog = Descendants<FileDialog>(panel).Single(dialog => dialog.FileMode == FileDialog.FileModeEnum.SaveFile);
        Check(saveDialog.Visible, "保存Boss打开Bxx文件窗口");
        saveDialog.EmitSignal(FileDialog.SignalName.FileSelected, uiPath); saveDialog.Hide(); await Settle();
        Check(File.Exists(uiPath) && !panel.Document.Dirty && File.ReadAllText(ProjectSettings.GlobalizePath(emitterPath)) == emitterBefore, "保存Boss不写Emitter引用文件");
        panel.Open(uiPath); await Settle();
        Check(panel.Document.ValidateBoss().PhaseCount == 2, "重开已保存Boss");
        // 通过正式战斗预览验证时间切换、末阶段胜利及模式停止清理。
        panel.Document.Edit(root =>
        {
            foreach (var phase in root["Phases"]!.AsArray()) { phase!["EndCondition"] = "Time"; phase["DurationMs"] = 100; }
        }); panel.Refresh();
        for (int frame = 0; frame < 6; frame++) PressBoss(panel, "单步 1/60s");
        Check(panel.Preview.Boss!.PhaseIndex == 1 && panel.Preview.Boss.PhaseHp == 100, "Boss预览按正式100ms边界切阶段");
        for (int frame = 0; frame < 6; frame++) PressBoss(panel, "单步 1/60s");
        Check(panel.Preview.Victory && panel.Preview.Boss!.Hp == 0, "Boss预览末阶段超时胜利");
        editor.SwitchMode(false);
        Check(panel.Preview.Boss is null, "切模式取消Boss时间线与战斗");
        Reject(() => GlobalEvent.GetBoss(), "切换后无旧Boss全局绑定");
        editor.SwitchMode(true); panel.Open("res://Data/Bosses/B03.json"); panel.SelectPhase(0); await Settle();
        // 布局尺寸验证在无显示设备环境也执行。
        Check(panel.Canvas.Size.X >= 400 && panel.Canvas.Size.Y >= 360, "Boss画布具有可用尺寸");
        if (OS.GetCmdlineUserArgs().Contains("--capture"))
        {
            await Capture("boss-editor-phase");
            panel.SelectPhase(-1); await Settle(); await Capture("boss-editor-core");
            panel.SelectPhase(0); PressBoss(panel, "单步 1/60s");
            for (int frame = 0; frame < 120; frame++) PressBoss(panel, "单步 1/60s");
            await Settle(); await Capture("boss-editor-preview");
        }
        panel.StopPreview(); editor.Free(); await Settle();
        GD.Print($"PASS: {_checks} targeted Boss editor assertions");
    }
}
