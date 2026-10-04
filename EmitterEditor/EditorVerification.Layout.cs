using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>针对画布过滤、位置事务、队列菜单与路径显示的集成验证。</summary>
public partial class EditorVerification
{
    /// <summary>通过实际画布输入拖动；移动阶段不得修改文档。</summary>
    /// <param name="editor">编辑场景。</param>
    /// <param name="marker">目标基础项。</param>
    /// <param name="delta">世界逻辑像素位移。</param>
    /// <param name="cancel">是否通过Esc取消。</param>
    private void Drag(EmitterEditor editor, EditorCanvas.Marker marker, Vector2 delta, bool cancel = false)
    {
        string original = editor.Document.Text;
        Vector2 start = editor.Canvas.ToCanvas(marker.Position), end = editor.Canvas.ToCanvas(marker.Position + delta);
        editor.Canvas._GuiInput(new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
        editor.Canvas._GuiInput(new InputEventMouseMotion { Position = end, ButtonMask = MouseButtonMask.Left });
        Check(editor.Document.Text == original, "拖动预览不产生文档历史");
        if (cancel) editor.Canvas._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        else editor.Canvas._GuiInput(new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    /// <summary>验证直接拖动、子树补偿、显示过滤与新属性控件。</summary>
    /// <param name="editor">已就绪编辑器。</param>
    /// <returns>交互测试完成任务。</returns>
    private async Task VerifyLayoutTools(EmitterEditor editor)
    {
        // 三层树包含极坐标和世界目标位移，用于验证非线性父子参考。
        editor.NewEmitter();
        editor.Document.Edit(root => root["VNodes"] = JsonNode.Parse("""
        {"Core":{"Type":"VNode","Name":"Root"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":"PI * 10","Y":0}]}],"Timeline":[{"StartMs":0}],
        "Children":[{"Core":{"Type":"VNode","Name":"Child"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"PMove","Angle":"PI/2","Dist":90}]}],"Timeline":[{"StartMs":0}],
        "Children":[{"Core":{"Type":"VBullet","LifeTimeMs":2000},"Display":{},"BaseAttributes":[{"RefMoveQueue":[{"Type":"TarMove","X":900,"Y":600,"Dist":130}]}],"Timeline":[{"StartMs":0}]}]},
        {"Core":{"Type":"VNode","Name":"Sibling"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":180,"Y":0}]}],"Timeline":[{"StartMs":0}]}]}
        """)); editor.Refresh(); await Settle();
        // 左侧显示选择器不隐藏树，只控制画布命中与绘制。
        var visibility = Descendants<OptionButton>(editor).Single(menu => menu.ItemCount == 3 && menu.GetItemText(0) == "显示全部节点");
        editor.Canvas.SelectedPath = "/VNodes/Children/0";
        visibility.Select(1); visibility.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Check(editor.Canvas.Markers.Count(marker => editor.Canvas.IsDisplayed(marker.Path)) == 2, "仅显示当前和全部后代");
        Check(editor.Canvas.HitMarkers(editor.Canvas.ToCanvas(editor.Canvas.Markers[0].Position)).Count == 0, "隐藏节点不能被命中");
        visibility.Select(2); visibility.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        Check(editor.Canvas.Markers.Count(marker => editor.Canvas.IsDisplayed(marker.Path)) == 1, "仅显示当前生成器");
        visibility.Select(0); visibility.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); editor.Refresh();
        var follow = Descendants<CheckButton>(editor).Single(button => button.Text == "同时更改子节点位置");
        Check(follow.ButtonPressed, "子节点联动默认开启");
        string original = editor.Document.Text;
        var before = editor.Canvas.Markers.ToArray();
        Drag(editor, before[0], new Vector2(40, 20)); await Settle();
        Check(editor.Canvas.Markers[0].Position.DistanceTo(before[0].Position + new Vector2(40, 20)) < 0.002, "拖动按逻辑像素移动父节点");
        Check(editor.Canvas.Markers[1].Position.DistanceTo(before[1].Position + new Vector2(40, 20)) < 0.002, "开启联动时子节点按相对位置变化");
        Check(editor.Document.Text.Contains("PI * 10"), "拖动保留原始位置表达式");
        Press(editor, "撤销"); await Settle(); Check(editor.Document.Text == original, "一次撤销恢复整个拖动");
        follow.ButtonPressed = false;
        Drag(editor, editor.Canvas.Markers[0], new Vector2(50, -30)); await Settle();
        Check(editor.Canvas.Markers[0].Position.DistanceTo(before[0].Position + new Vector2(50, -30)) < 0.002, "关闭联动仍移动父节点");
        Check(editor.Canvas.Markers.Skip(1).Zip(before.Skip(1)).All(pair => pair.First.Position.DistanceTo(pair.Second.Position) < 0.002), "关闭联动保持所有后代世界坐标，含TarMove");
        Press(editor, "撤销"); await Settle();
        Drag(editor, editor.Canvas.Markers[0], new Vector2(80, 10), true);
        Check(editor.Document.Text == original && editor.Canvas.Markers[0].Position.DistanceTo(before[0].Position) < 0.002, "Esc取消还原临时图标与文档");
        // 右侧坐标编辑与拖动共用子树补偿规则。
        Press(editor, "全部展开");
        var x = Descendants<Label>(editor).First(label => label.Text == "横坐标（X）").GetParent().GetChildren().OfType<LineEdit>().Single();
        x.Text = "120"; x.EmitSignal(LineEdit.SignalName.TextSubmitted, x.Text); await Settle();
        Check(editor.Canvas.Markers[0].Position.X == 760, "右侧输入更新父节点位置");
        Check(editor.Canvas.Markers.Skip(1).Zip(before.Skip(1)).All(pair => pair.First.Position.DistanceTo(pair.Second.Position) < 0.002), "右侧输入同样保持后代位置");
        // 固定字符串下拉框保留原JSON取值，不影响自由名称输入。
        var type = Descendants<Label>(editor).First(label => label.Text == "类型（Type）").GetParent().GetChildren().OfType<OptionButton>().Single();
        Check(Enumerable.Range(0, type.ItemCount).Select(type.GetItemText).Contains("VPath"), "生成器类型下拉包含协议四种类型");
        var basis = Descendants<Button>(editor).Single(button => button.Text == "基础项 [0] · 已选中");
        Check(basis.GetParent().GetChildren().OfType<Button>().Select(button => button.Text).SequenceEqual(new[] { "基础项 [0] · 已选中", "复制", "↑", "↓", "×" }), "队列标题和操作按钮严格同一行排列");
        Check(Descendants<Button>(editor).Any(button => button.Text.StartsWith("基础属性(队列)")), "数组属性显示队列后缀");
        if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("editor-tree-drag");
        follow.ButtonPressed = true;
        VerifyPositionData();
        VerifyStaticPaths(editor);
        await Settle(); if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("editor-paths");
        editor.NewEmitter(); editor.Refresh(); await Settle();
    }

    /// <summary>验证队列对齐、复制覆盖、保存往返和协议顺序。</summary>
    private void VerifyPositionData()
    {
        var document = new EmitterDocument();
        document.Edit(root =>
        {
            root["VNodes"]!["BaseAttributes"] = JsonNode.Parse("[{\"RefMoveQueue\":[{\"Type\":\"PMove\",\"Dist\":50}]},{\"RefMoveQueue\":[{\"Type\":\"PMove\",\"Dist\":100}]}]");
            root["VNodes"]!["AddAttributes"] = JsonNode.Parse("{\"RefMoveQueue\":[{\"Type\":\"PMove\",\"Angle\":0.1}]}");
            root["VNodes"]!["RandDiffAttributes"] = JsonNode.Parse("{\"Member\":{\"RefMoveQueue\":[{\"Type\":\"PMove\",\"Dist\":10}]}}");
        });
        var before = new EditorLayout(document.Validate());
        document.Edit(_ => EditorPositionTools.Translate(document, "/VNodes", 0, new Vector2(30, 20)));
        var after = new EditorLayout(document.Validate());
        Check(after.Markers[0].Position == before.Markers[0].Position + new Vector2(30, 20) && after.Markers[1].Position == before.Markers[1].Position, "非空增量随机队列补零对齐且不移动其他基础项");
        Check(document.Root["VNodes"]!["RandDiffAttributes"]!["Member"]!["RefMoveQueue"]!.AsArray().Count == 2, "随机位移队列同步补零槽");
        // 按下标覆盖复制基础项，深层编辑按协议显式覆盖Children。
        document.New();
        document.Edit(root => root["VNodes"]!["Children"] = JsonNode.Parse("""
        [{"Core":{"Type":"VNode","Name":"Source"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":"PI*10"}]}],
        "Children":[{"Core":{"Type":"VNode","Name":"Inner"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","Y":70}]}]}]},
        {"Core":{"CopySource":"Source","Name":"B"}}]
        """));
        document.Validate();
        document.Edit(_ => EditorPositionTools.Translate(document, "/VNodes/Children/1", 0, new Vector2(60, 0)));
        document.Validate();
        Check(document.Root["VNodes"]!["Children"]![1]!["Core"]!["CopySource"]!.ToString() == "Source" && document.Text.Contains("PI*10"), "复制节点拖动保留源引用和表达式");
        document.Edit(_ => EditorPositionTools.Translate(document, "/VNodes/Children/1/Children/0", 0, new Vector2(0, 25)));
        document.Validate();
        Check(document.Root["VNodes"]!["Children"]![1]!["Children"]![0]!["Core"]!["Name"]!.ToString() == "Inner_copy_B", "继承深层节点编辑保留加载器定义的名称后缀");
        string path = ProjectSettings.GlobalizePath("res://.tools/editor-drag-roundtrip.json"); _temporary.Add(path);
        var layout = new EditorLayout(document.Validate()); document.Save(path); var reopened = new EmitterDocument(); reopened.Open(path);
        Check(new EditorLayout(reopened.Validate()).Markers.Select(marker => marker.Position).SequenceEqual(layout.Markers.Select(marker => marker.Position)), "拖动保存重开布局一致");
        // 新字段按协议排序，即使现有对象键故意倒序。
        var obj = JsonNode.Parse("{\"ASpeed\":3,\"Speed\":100}")!.AsObject();
        var fields = EditorSchema.Fields(typeof(VNodeSpawnAttribute), obj, "VNode", "BaseAttributes");
        EditorSchema.InsertField(obj, fields, fields.Single(field => field.Name == "Angle"));
        Check(obj.Select(pair => pair.Key).SequenceEqual(new[] { "Angle", "Speed", "ASpeed" }), "添加属性按源码协议顺序而非添加顺序写回");
    }

    /// <summary>验证曲线、瞄准及激光路径显示，且不消耗随机或绑定战斗。</summary>
    /// <param name="editor">用于展示路径的编辑器。</param>
    private void VerifyStaticPaths(EmitterEditor editor)
    {
        editor.NewEmitter(); editor.Document.Edit(root => root["VNodes"] = EditorSchema.Creator("VPath"));
        editor.Document.Edit(root => root["VNodes"]!["PathQueue"] = JsonNode.Parse("""
        [{"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":250}],"ControlPoints":[{"X":100,"Y":-130}]},{"Type":"AimPlayer","X":30,"Y":-20}]
        """));
        VMath.setRandomSeed(45); double expected = VMath.getRandomDouble(0, 1); VMath.setRandomSeed(45);
        editor.Refresh();
        Check(editor.Canvas.Paths.Count == 1 && editor.Canvas.Paths[0].Points.Length > 100, "Bezier曲线和瞄准段组成可见路径");
        Check(editor.Canvas.Paths[0].Points[^1].DistanceTo(BattleConfig.PlayerSpawn + new Vector2(30, -20)) < 0.001, "瞄准路径冻结编辑器玩家参考坐标");
        Check(VMath.getRandomDouble(0, 1) == expected, "路径采样不改变随机序列");
        Reject(() => GlobalEvent.GetBoss(), "路径显示不创建全局战斗环境");
        editor.OpenEmitter(ProjectSettings.GlobalizePath("res://Data/Emitters/Examples/PathLaser.json")); editor.Refresh();
        Check(editor.Canvas.Paths.Count == 1 && editor.Canvas.Paths[0].Points.Length == 257, "路径激光复用Function采样并显示完整曲线");
        // CopySource展开后的运行定义足以显示激光，无需再提供原始JSON或创建路径生成器。
        var original = editor.Document.At("/VNodes")!.AsObject(); original["Core"]!["Name"] = "Source";
        var copy = JsonNode.Parse("""{"Core":{"Name":"Copy","CopySource":"Source"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":25,"Y":30}]}]}""");
        var copiedDocument = new EmitterDocument();
        copiedDocument.Edit(root => root["VNodes"] = new JsonObject
        {
            ["Core"] = new JsonObject { ["Type"] = "VNode" },
            ["BaseAttributes"] = new JsonArray(new JsonObject()),
            ["Children"] = new JsonArray(original, copy)
        });
        var layout = new EditorLayout(copiedDocument.Validate());
        Check(layout.Paths.Count == 2 && layout.Paths.All(path => path.Points.Length == 257), "复制的激光仅凭运行定义保留完整函数曲线");
        Check(layout.Paths.All(path => path.Points[0] == layout.Markers.Single(marker => marker.Path == path.Path).Position),
            "每个复制激光分别使用自身出生参考点");
    }
}
