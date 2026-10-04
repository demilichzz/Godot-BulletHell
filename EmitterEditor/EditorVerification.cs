using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>编辑器针对性验证：原文往返、交互连接、基础坐标、预览确定性和实际界面截图。</summary>
public partial class EditorVerification : Node
{
    // 断言数及需要清理的本次测试文件，均位于项目忽略目录。
    private int _checks;
    private readonly List<string> _temporary = new();
    /// <summary>等待场景就绪后开始验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>验证一个必要条件。</summary>
    /// <param name="condition">应成立的条件。</param>
    /// <param name="message">失败诊断。</param>
    private void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; }
    /// <summary>验证非法操作被拒绝。</summary>
    /// <param name="action">应抛出异常的操作。</param>
    /// <param name="message">条件说明。</param>
    private void Reject(Action action, string message)
    {
        // 记录非法操作是否按预期抛出异常。
        bool rejected = false;
        try { action(); } catch { rejected = true; }
        Check(rejected, message);
    }
    /// <summary>等待两帧处理布局与延迟释放。</summary>
    /// <returns>下一次布局稳定后的任务。</returns>
    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    /// <summary>取得控件树中的指定类型后代。</summary>
    /// <typeparam name="T">目标Godot节点类型。</typeparam>
    /// <param name="node">遍历起点。</param>
    /// <returns>包括自身的前序节点。</returns>
    private static IEnumerable<T> Descendants<T>(Node node) where T : Node
    {
        if (node is T match) yield return match;
        // 当前子节点，逐个处理以保持原有顺序。
        foreach (Node child in node.GetChildren()) foreach (var nested in Descendants<T>(child)) yield return nested;
    }
    /// <summary>通过实际按钮信号执行UI操作。</summary>
    /// <param name="editor">已装配编辑器。</param>
    /// <param name="text">唯一按钮文本。</param>
    private static void Press(Node editor, string text) => Descendants<Button>(editor).First(button => button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
    /// <summary>采集当前运行对象状态，包含节点、子弹与随机序列下一值。</summary>
    /// <param name="preview">当前预览环境。</param>
    /// <returns>可逐字比较的确定性状态。</returns>
    private static string Snapshot(EditorPreview preview) => JsonSerializer.Serialize(new
    {
        preview.Elapsed,
        Nodes = preview.Emitter!.Nodes.Select(node => new { node.WorldPosition.X, node.WorldPosition.Y, node.Angle, node.Speed, node.Age, node.BirthIndex }).ToArray(),
        Bullets = preview.Emitter.Bullets.Select(node => new { node.WorldPosition.X, node.WorldPosition.Y, node.Angle, node.Speed, node.Age, node.BirthIndex, node.Team }).ToArray(),
        Random = VMath.getRandomDouble(0, 1)
    });
    /// <summary>验证编辑文档和全部当前Emitter输入的无损往返。</summary>
    private void VerifyDocuments()
    {
        // 本次文档验证使用的独立实例。
        var document = new EmitterDocument();
        document.Validate(); Check(document.Dirty, "新建文档必须标为未保存");
        // 编辑前的完整原文，用于验证撤销。
        string original = document.Text;
        document.Edit(root => root["VNodes"]!["BaseAttributes"]![0]!["Angle"] = "PI / 3");
        Check(document.Validate().Root.BaseAttributes[0].Angle == Math.PI / 3, "表达式使用现有解析器");
        document.Undo(); Check(document.Text == original, "撤销应完整恢复");
        document.Redo(); Check(document.Root["VNodes"]!["BaseAttributes"]![0]!["Angle"]!.GetValue<string>() == "PI / 3", "重做保留表达式格式");
        // 当前操作的文件路径或文档定位指针。
        string path = ProjectSettings.GlobalizePath("res://.tools/editor-roundtrip.json"); _temporary.Add(path);
        document.Save(path); Check(!document.Dirty, "成功保存更新脏状态");
        // 从磁盘重新打开的独立文档。
        var reopened = new EmitterDocument(); reopened.Open(path);
        Check(JsonNode.DeepEquals(document.Root, reopened.Root), "保存再打开保留所有JSON值");
        // 失败写入前的文件内容基线。
        string saved = File.ReadAllText(path);
        document.Edit(root => root["VNodes"]!["Timeline"]![0]!["StartMs"] = "PI");
        Reject(() => document.Save(path), "整数毫秒表达式必须拒绝保存");
        Check(File.ReadAllText(path) == saved, "失败保存不能破坏已有文件");
        Reject(() => document.ApplyText("{\"Core\":{},\"Core\":{}}"), "重复字段必须拒绝");
        document.Undo(); document.Edit(root => root["Unknown"] = 1);
        Reject(() => document.Validate(), "未知字段仍由游戏规则拒绝");
        Check(EditorSchema.Scalar("PI/3", typeof(double))!.GetValue<string>() == "PI/3", "数值表单保留表达式");
        Check(EditorSchema.Scalar("PI", typeof(long))!.GetValueKind() == JsonValueKind.String, "非法整数可暂存供修复，不隐式求值");
        // 遍历当前真实数据，不写入或展开复制指令。
        int fileCount = 0;
        // 本次读取的真实Emitter文件。
        foreach (string file in Directory.GetFiles(ProjectSettings.GlobalizePath("res://Data/Emitters"), "*.json", SearchOption.AllDirectories))
        {
            // 当前真实输入文档的独立副本。
            var source = new EmitterDocument(); source.Open(file); source.Validate();
            Check(JsonNode.DeepEquals(EmitterDocument.Parse(File.ReadAllText(file)), EmitterDocument.Parse(source.Text)), "原文往返失真：" + file);
            Check(new EditorLayout(source.Validate(), source.Root).Markers.Count > 0, "真实Emitter静态布局及路径采样：" + file);
            fileCount++;
        }
        GD.Print($"Editor document inputs: {fileCount}");
        // 四类Creator模板必须全部能被游戏加载器接受。
        foreach (string type in new[] { "VNode", "VBullet", "VPath", "VLaser" })
        {
            document.New(); document.Edit(root => root["VNodes"] = EditorSchema.Creator(type)); document.Validate();
            Check(true, "模板加载：" + type);
        }
        // 瞄准简写中的X/Y必须作为数值表达式，Function中的X/Y保留函数原文。
        var aim = EditorSchema.Fields(typeof(VPathSegmentAttribute), JsonNode.Parse("{\"Type\":\"AimPlayer\"}")!.AsObject(), "VPath", "PathQueue");
        Check(aim.Single(field => field.Name == "X").ValueType == typeof(double), "瞄准偏移字段类型");
        // CopySource保存的是原始覆盖声明，不写入展开后的运行树。
        document.New();
        document.Edit(root => root["VNodes"]!["Children"] = JsonNode.Parse("[{\"Core\":{\"Type\":\"VNode\",\"Name\":\"Original\"},\"BaseAttributes\":[{\"Angle\":\"PI/6\"}]},{\"Core\":{\"CopySource\":\"Original\",\"Name\":\"Second\"}}]"));
        document.Validate(); document.Save(path); reopened.Open(path);
        Check(reopened.Root["VNodes"]!["Children"]![1]!["Core"]!["CopySource"]!.GetValue<string>() == "Original", "CopySource原文保存");
        Check(reopened.Root["VNodes"]!["Children"]![1]!["BaseAttributes"] is null, "未将复制继承属性展开写回");
    }
    /// <summary>验证搜索、折叠、基础项定位与历史快捷键组成的实际编辑流程。</summary>
    /// <param name="editor">已就绪的独立编辑器。</param>
    /// <returns>交互和布局验证结束的任务。</returns>
    private async Task VerifyNavigation(EmitterEditor editor)
    {
        // 导航输入及历史按钮；测试从无历史的新文档开始。
        var search = Descendants<LineEdit>(editor).Single(input => input.PlaceholderText.StartsWith("搜索属性"));
        var undo = Descendants<Button>(editor).Single(button => button.Text == "撤销");
        var redo = Descendants<Button>(editor).Single(button => button.Text == "重做");
        string initial = editor.Document.Text;
        Check(undo.Disabled && redo.Disabled, "空历史禁用撤销重做");
        Press(editor, "全部收起");
        Check(Descendants<Button>(editor).Where(button => button.ToggleMode && button is not CheckButton).All(button => !button.ButtonPressed), "全部收起包含深层分组");
        search.Text = "aNgLe"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Settle();
        Check(Descendants<Label>(editor).Any(label => label.Text == "角度（Angle）" && label.IsVisibleInTree()), "英文搜索忽略大小写并展开匹配项");
        Check(!Descendants<Label>(editor).Any(label => label.Text == "速度（Speed）"), "筛选排除无关字段");
        Check(editor.Document.Text == initial && undo.Disabled, "筛选和折叠不修改文档或历史");
        // 在筛选结果中提交表达式，再用文档撤销恢复。
        var angle = Descendants<Label>(editor).First(label => label.Text == "角度（Angle）").GetParent().GetChildren().OfType<LineEdit>().Single();
        angle.Text = "PI/4"; angle.EmitSignal(LineEdit.SignalName.TextSubmitted, angle.Text);
        Check(!undo.Disabled && redo.Disabled, "编辑后历史按钮状态更新");
        undo.GrabFocus();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, CtrlPressed = true, Pressed = true }); await Settle();
        Check(editor.Document.Text == initial && !redo.Disabled, "Ctrl+Z通过界面输入撤销文档修改");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Y, CtrlPressed = true, Pressed = true }); await Settle();
        Check(editor.Document.Root["VNodes"]!["BaseAttributes"]![0]!["Angle"]!.ToString() == "PI/4", "Ctrl+Y恢复表达式");
        search.Text = "加速度"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Settle();
        // 未填写的可选属性也能通过中文搜索找到，添加后保持筛选。
        var menu = Descendants<OptionButton>(editor).First(item => Enumerable.Range(0, item.ItemCount).Any(index => item.GetItemText(index) == "加速度（ASpeed）"));
        menu.Select(Enumerable.Range(0, menu.ItemCount).First(index => menu.GetItemText(index) == "加速度（ASpeed）"));
        menu.GetParent().GetChildren().OfType<Button>().Single(button => button.Text == "+ 属性").EmitSignal(BaseButton.SignalName.Pressed); await Settle();
        Check(Descendants<Label>(editor).Any(label => label.Text == "加速度（ASpeed）" && label.IsVisibleInTree()), "搜索结果可添加缺省属性");
        if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("editor-search");
        // 文本框中的撤销不应回退刚刚完成的文档操作。
        string edited = editor.Document.Text;
        search.GrabFocus();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, CtrlPressed = true, Pressed = true }); await Settle();
        Check(editor.Document.Text == edited, "搜索框输入不触发文档撤销");
        search.Text = "不存在的字段xyz"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Settle();
        Check(Descendants<Label>(editor).Any(label => label.Text.StartsWith("没有匹配的属性")), "无匹配结果提供清空提示");
        search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Settle();
        Check(!Descendants<Button>(editor).Single(button => button.Text == "核心属性（Core）").ButtonPressed, "清空搜索恢复原来的折叠状态");
        // 多基础项分散摆放，点击后应展开并滚动到准确的零基下标。
        editor.NewEmitter();
        editor.Document.Edit(root =>
        {
            // 由默认项派生多个互不重叠的图标，每项横向间隔60逻辑像素。
            var bases = root["VNodes"]!["BaseAttributes"]!.AsArray();
            for (int index = 1; index < 9; index++)
            {
                var basis = bases[0]!.DeepClone(); basis["RefMoveQueue"]![0]!["X"] = index * 60; bases.Add(basis);
            }
        });
        editor.Refresh(); await Settle();
        Press(editor, "全部展开");
        // 选择第八个基础项，避开其他图标命中半径。
        var marker = editor.Canvas.Markers[7];
        search.Text = "Angle"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        editor.Canvas._GuiInput(new InputEventMouseButton { Position = editor.Canvas.ToCanvas(marker.Position), ButtonIndex = MouseButton.Left, Pressed = true });
        await Settle(); await Settle();
        var target = Descendants<Button>(editor).Single(button => button.Text == "基础项 [7] · 已选中");
        var scroll = Descendants<ScrollContainer>(editor).Single();
        Check(editor.Canvas.SelectedBasis == 7 && search.Text == "" && target.ButtonPressed, "图标点击清除筛选并展开对应基础项");
        Check(scroll.ScrollVertical > 0 && scroll.GetGlobalRect().Encloses(target.GetGlobalRect()) &&
            target.GlobalPosition.Y - scroll.GlobalPosition.Y < 30, "属性面板自动滚动让目标标题位于顶部并展示其字段");
        if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("editor-basis-navigation");
        // 快捷键聚焦搜索，JSON草稿存在时禁用文档历史且保留草稿。
        target.GrabFocus();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, CtrlPressed = true, Pressed = true }); await Settle();
        Check(search.HasFocus(), "Ctrl+F聚焦属性搜索");
        var code = Descendants<CodeEdit>(editor).Single(); code.Text += " "; code.EmitSignal(TextEdit.SignalName.TextChanged); await Settle();
        Check(undo.Disabled && redo.Disabled, "未应用JSON草稿禁用文档历史");
        Press(editor, "应用 JSON 草稿"); await Settle();
        editor.NewEmitter(); editor.Refresh(); await Settle();
    }
    /// <summary>执行运行及图形交互集成验证。</summary>
    private async void Run()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains("--targeted-boss"))
            {
                VerifySaveSnapshots();
                await VerifyBossEditor();
                await VerifyCatalogWorkspace();
                await VerifySharedModes();
                GetTree().Quit();
                return;
            }
            VerifyDocumentTransactions();
            VerifyDocuments();
            // 实际实例化的编辑器场景。
            var editor = GD.Load<PackedScene>("res://EmitterEditor/EmitterEditor.tscn").Instantiate<EmitterEditor>();
            editor.StartInCatalog = false; AddChild(editor);
            await Settle();
            Check(editor.Canvas.Markers.Count == 1, "默认根基础项图标");
            // 显式校验按钮必须检查当前文档，不能沿用上次布局或预览的成功状态。
            editor.Document.Edit(root => root["Unknown"] = true); Press(editor, "校验");
            Check(Descendants<Label>(editor).Any(label => label.TooltipText.Contains("Unknown: 未知字段")), "校验按钮报告当前非法字段");
            editor.Document.Undo(); editor.Refresh(); await Settle();
            // 实际画布输入经过命中检测和选择回调。
            var marker = editor.Canvas.Markers[0];
            // 属性和JSON页面容器。
            var tabs = Descendants<TabContainer>(editor).Single(); tabs.CurrentTab = 1;
            editor.Canvas._GuiInput(new InputEventMouseButton { Position = editor.Canvas.ToCanvas(marker.Position), ButtonIndex = MouseButton.Left, Pressed = true });
            Check(editor.Canvas.SelectedPath == marker.Path, "图标选中Creator");
            Check(tabs.CurrentTab == 0, "点击图标打开属性菜单");
            Press(editor, "添加子节点"); await Settle();
            Check(editor.Document.Root["VNodes"]!["Children"]!.AsArray().Count == 1, "树工具添加节点");
            Press(editor, "删除"); await Settle();
            Check(editor.Document.Root["VNodes"]!["Children"]!.AsArray().Count == 0, "树工具删除节点");
            Press(editor, "撤销"); await Settle();
            Check(editor.Document.Root["VNodes"]!["Children"]!.AsArray().Count == 1, "UI撤销恢复子树");
            editor.NewEmitter(); editor.Refresh(); await Settle();
            await VerifyNavigation(editor);
            await VerifyLayoutTools(editor);
            // 找到图形Angle输入，模拟文本提交，检查原文与方向提示同步存在。
            var label = Descendants<Label>(editor).First(item => item.Text == "角度（Angle）");
            // Angle字段的实际文本输入控件。
            var angle = label.GetParent().GetChildren().OfType<LineEdit>().Single();
            angle.Text = "PI/3"; angle.EmitSignal(LineEdit.SignalName.TextSubmitted, angle.Text);
            Check(editor.Document.Root["VNodes"]!["BaseAttributes"]![0]!["Angle"]!.GetValue<string>() == "PI/3", "图形属性输入写入原表达式");
            Check(Descendants<AngleIndicator>(editor).Any(), "角度图示存在");
            Check(label.TooltipText.Contains("弧度"), "属性Tooltip包含单位");
            // 使用真实字段添加菜单验证可选运动属性及嵌套数组的编辑路径。
            var addField = Descendants<OptionButton>(editor).First(menu => Enumerable.Range(0, menu.ItemCount).Any(index => menu.GetItemText(index) == "加速度（ASpeed）"));
            addField.Select(Enumerable.Range(0, addField.ItemCount).First(index => addField.GetItemText(index) == "加速度（ASpeed）"));
            addField.GetParent().GetChildren().OfType<Button>().First(button => button.Text == "+ 属性").EmitSignal(BaseButton.SignalName.Pressed);
            await Settle();
            Check(editor.Document.Root["VNodes"]!["BaseAttributes"]![0]!.AsObject().ContainsKey("ASpeed"), "菜单添加可选属性");
            Press(editor, "+ 添加项"); await Settle();
            Check(editor.Document.Root["VNodes"]!["BaseAttributes"]![0]!["RefMoveQueue"]!.AsArray().Count == 2, "图形追加嵌套位移数组项");
            // 新建前的丢弃确认可取消，不丢失当前图形修改。
            string beforeCancel = editor.Document.Text;
            Press(editor, "新建");
            // 未保存修改的确认窗口。
            var discard = Descendants<ConfirmationDialog>(editor).Single(dialog => dialog.Title == "未保存的修改");
            Check(discard.Visible, "新建前保护未保存内容"); discard.EmitSignal(ConfirmationDialog.SignalName.Canceled); discard.Hide();
            Check(editor.Document.Text == beforeCancel, "取消新建保持原文档");
            // 完整JSON草稿输入控件。
            var code = Descendants<CodeEdit>(editor).Single(); code.Text += " ";
            Press(editor, "▶ 播放"); Check(editor.Preview.Emitter is null, "未应用JSON草稿不能被忽略并播放旧数据");
            Press(editor, "应用 JSON 草稿"); await Settle();
            // 文件对话框的真实回调保存当前文档，并能再次打开。
            string uiPath = ProjectSettings.GlobalizePath("res://.tools/editor-ui-save.json"); _temporary.Add(uiPath);
            Press(editor, "保存");
            // 负责保存回调的文件窗口。
            var saveDialog = Descendants<FileDialog>(editor).Single(dialog => dialog.FileMode == FileDialog.FileModeEnum.SaveFile);
            Check(saveDialog.Visible, "新文档保存打开文件对话框");
            saveDialog.EmitSignal(FileDialog.SignalName.FileSelected, uiPath); saveDialog.Hide();
            Check(File.Exists(uiPath) && !editor.Document.Dirty, "文件对话框保存回调成功");
            Press(editor, "打开…");
            // 负责打开回调的文件窗口。
            var openDialog = Descendants<FileDialog>(editor).Single(dialog => dialog.FileMode == FileDialog.FileModeEnum.OpenFile);
            openDialog.EmitSignal(FileDialog.SignalName.FileSelected, uiPath); openDialog.Hide();
            Check(editor.Document.FilePath == Path.GetFullPath(uiPath), "文件对话框打开回调成功");
            Press(editor, "单步 1/60s"); Check(Math.Abs(editor.Preview.Elapsed - 1.0 / 60) < 1e-10, "单步精确60Hz");
            Check(editor.Preview.BulletCount == 12, "默认环形在第一步生成12颗");
            // 连续播放使用真正的物理回调；暂停后跨多个物理帧保持时钟不变。
            double beforePlay = editor.Preview.Elapsed;
            Press(editor, "▶ 播放");
            for (int tick = 0; tick < 5; tick++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Press(editor, "Ⅱ 暂停");
            Check(editor.Preview.Elapsed > beforePlay, "连续播放由物理回调推进");
            double pausedTime = editor.Preview.Elapsed;
            for (int tick = 0; tick < 5; tick++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(editor.Preview.Elapsed == pausedTime, "暂停冻结战斗时钟");
            Press(editor, "返回布局"); Check(editor.Preview.Emitter is null && editor.Canvas.PreviewTexture is null, "返回布局释放预览");
            // 静态布局不消耗VMath序列，使用与实际生成相同的连续位移顺序。
            VMath.setRandomSeed(23); double expected = VMath.getRandomDouble(0, 1); VMath.setRandomSeed(23);
            editor.Canvas.Rebuild(editor.Document.Validate());
            Check(VMath.getRandomDouble(0, 1) == expected, "基础图标不能消耗战斗随机");
            editor.Document.Edit(root => root["VNodes"]!["BaseAttributes"]![0]!["RefMoveQueue"] = JsonNode.Parse("[{\"Type\":\"PMove\",\"Angle\":\"PI/2\",\"Dist\":100},{\"Type\":\"XYMove\",\"X\":30,\"Y\":20}]"));
            editor.Refresh(); Check(editor.Canvas.Markers[0].Position.DistanceTo(new Vector2(670, 370)) < 0.001, "PMove及XYMove坐标顺序");
            // 同一正式运行代码在简化与原始显示下应得到完全相同状态。
            string fixture = File.ReadAllText(ProjectSettings.GlobalizePath("res://Data/Emitters/B01P03_Emitter01.json"));
            editor.Preview.Simple = true; editor.Preview.Start(fixture);
            // 固定60Hz模拟步下标。
            for (int tick = 0; tick < 240; tick++) editor.Preview.Advance();
            // 简化显示下的运行状态基线。
            string simplified = Snapshot(editor.Preview); Check(editor.Preview.BulletCount > 0, "嵌套延迟弹幕预览有活动成员");
            editor.Preview.Simple = false; editor.Preview.Start(fixture);
            // 固定60Hz模拟步下标。
            for (int tick = 0; tick < 240; tick++) editor.Preview.Advance();
            Check(Snapshot(editor.Preview) == simplified, "简化显示与原显示运动、年龄、顺序及随机完全一致");
            editor.Preview.Start(fixture);
            // 固定60Hz模拟步下标。
            for (int tick = 0; tick < 240; tick++) editor.Preview.Advance();
            Check(Snapshot(editor.Preview) == simplified, "重置后完整初始状态可重现");
            // 现有路径激光同样通过预览入口运行，不用独立近似模拟器。
            editor.Preview.Start(File.ReadAllText(ProjectSettings.GlobalizePath("res://Data/Emitters/Examples/PathLaser.json")));
            // 固定60Hz模拟步下标。
            for (int tick = 0; tick < 60; tick++) editor.Preview.Advance();
            Check(editor.Preview.Emitter!.Bullets.Any(bullet => bullet is VLaser), "路径激光预览");
            editor.Preview.Stop(); editor.OpenEmitter(ProjectSettings.GlobalizePath("res://Data/Emitters/B01P03_Emitter01.json")); editor.Refresh();
            await Settle();
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await Capture("editor-layout");
                // 点击实际子Creator，截图验证多个基础项中的角度控件。
                var childMarker = editor.Canvas.Markers.Last();
                editor.Canvas._GuiInput(new InputEventMouseButton { Position = editor.Canvas.ToCanvas(childMarker.Position), ButtonIndex = MouseButton.Left, Pressed = true });
                // 收起核心与显示组，让实际角度示意进入截图可见区域。
                foreach (var button in Descendants<Button>(editor).Where(button => button.ToggleMode && button.Text is "核心属性（Core）" or "显示属性（Display）")) button.ButtonPressed = false;
                await Settle(); await Capture("editor-properties");
                editor.Preview.Simple = true;
                Press(editor, "单步 1/60s");
                // 固定60Hz模拟步下标。
                for (int tick = 0; tick < 160; tick++) Press(editor, "单步 1/60s");
                editor.Canvas.QueueRedraw(); await Settle(); await Capture("editor-preview");
            }
            editor.Preview.Stop(); RemoveChild(editor); editor.QueueFree(); await Settle();
            Reject(() => GlobalEvent.GetBoss(), "退出后无残留全局战斗绑定");
            GD.Print($"PASS: {_checks} targeted editor assertions");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { foreach (string file in _temporary) if (File.Exists(file)) File.Delete(file); }
    }
    /// <summary>渲染完成后导出实际Godot界面供视觉检查。</summary>
    /// <param name="name">输出基础文件名，仅写入忽略目录。</param>
    /// <returns>导出结束任务。</returns>
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://.tools/" + name + ".png")) == Error.Ok, "界面截图导出");
    }
}
