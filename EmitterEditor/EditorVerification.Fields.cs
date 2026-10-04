using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>验证共用协议菜单、输入事务及真实路径表单，不依赖完整游戏回归。</summary>
public partial class EditorVerification
{
    /// <summary>用独立协议期望检查菜单字段和正式解析器，避免不同模式放宽白名单。</summary>
    private void VerifyProtocolFields()
    {
        // 三类普通路径必须提供不同字段；无效旧字段不能因读取菜单被改写。
        foreach (var item in new[]
        {
            (Mode: "XY", Fields: "PathMode,StartMoveQueue,EndMoveQueue", Invalid: "AxisMode", Value: "Relative"),
            (Mode: "Bezier", Fields: "PathMode,StartMoveQueue,EndMoveQueue,ControlPoints,Samples", Invalid: "AxisMode", Value: "Relative"),
            (Mode: "Function", Fields: "PathMode,StartMoveQueue,EndMoveQueue,AxisMode,X,Y,TMin,TMax,Samples", Invalid: "Type", Value: "AimPlayer")
        })
        {
            var segment = EditorSchema.PathSegment(); EditorSchema.ChangePathMode(segment, item.Mode);
            var expected = item.Fields.Split(',').OrderBy(name => name).ToArray();
            Check(EditorSchema.Fields(typeof(VPathSegmentAttribute), segment, "VPath", "PathQueue").Select(field => field.Name).OrderBy(name => name).SequenceEqual(expected),
                "Emitter路径字段与独立协议期望一致：" + item.Mode);
            Check(BossEditorSchema.Fields(typeof(VPathSegmentAttribute), segment, "PathQueue").Select(field => field.Name).OrderBy(name => name).SequenceEqual(expected),
                "Boss路径字段与独立协议期望一致：" + item.Mode);
            var document = new EmitterDocument(); document.Edit(root => { root["VNodes"] = EditorSchema.Creator("VPath"); root["VNodes"]!["PathQueue"] = new JsonArray(segment); });
            document.Validate();
            document.Edit(root => root["VNodes"]!["PathQueue"]![0]![item.Invalid] = item.Value);
            Reject(() => document.Validate(), "共用模式表仍拒绝跨模式字段：" + item.Mode);
        }
        foreach (var item in new[] { ("XYMove", "Type,X,Y", "Angle"), ("PMove", "Type,Angle,Dist", "X"), ("TarMove", "Type,X,Y,Dist", "Angle") })
        {
            var action = EditorSchema.Item(typeof(VNodeMoveActionAttribute), "RefMoveQueue", item.Item1).AsObject();
            Check(EditorSchema.Fields(typeof(VNodeMoveActionAttribute), action, "VNode", "RefMoveQueue").Select(field => field.Name).OrderBy(name => name)
                .SequenceEqual(item.Item2.Split(',').OrderBy(name => name)), "位移菜单不提供其他模式字段：" + item.Item1);
            var document = new EmitterDocument();
            document.Edit(root => root["VNodes"]!["BaseAttributes"]![0]!["RefMoveQueue"] = new JsonArray(action)); document.Validate();
            document.Edit(root => root["VNodes"]!["BaseAttributes"]![0]!["RefMoveQueue"]![0]![item.Item3] = 0);
            Reject(() => document.Validate(), "位移加载器继续拒绝混用字段：" + item.Item1);
        }
        foreach (string mode in new[] { "Center", "RandomCircle", "RandomRect", "Sequence", "Path" })
        {
            var movement = BossEditorSchema.Movement(mode);
            BossMovement.Read(JsonSerializer.SerializeToElement(movement));
            movement[mode == "Path" ? "Target" : "PointCount"] = mode == "Path" ? JsonNode.Parse("""{"X":640,"Y":240}""") : JsonValue.Create(10);
            Reject(() => BossMovement.Read(JsonSerializer.SerializeToElement(movement)), "Boss移动模式仍拒绝无关字段：" + mode);
        }
        // 两个模板独立，字段查询也不能修改未识别的原文。
        var aim = EditorSchema.PathSegment(true); aim["Legacy"] = "待修复";
        string original = aim.ToJsonString();
        var aimFields = BossEditorSchema.Fields(typeof(VPathSegmentAttribute), aim, "PathQueue");
        Check(aimFields.Select(field => field.Name).OrderBy(name => name).SequenceEqual(new[] { "Type", "X", "Y" })
            && aimFields.Single(field => field.Name == "X").ValueType == typeof(double) && aim.ToJsonString() == original, "瞄准偏移使用数值表达式类型，字段查询保留未知原文");
        aim["X"] = 90;
        Check(EditorSchema.PathSegment(true)["X"]!.GetValue<int>() == 0, "模板不共享可变JSON节点");
    }

    /// <summary>通过实际Godot信号检查共用控件的回滚、重复提交与重建保护。</summary>
    private void VerifySharedFieldControls()
    {
        // 隐藏测试控件仅发信号，不干扰后续编辑器布局。
        var host = new Control { Visible = false }; AddChild(host);
        var document = new EmitterDocument(); document.Edit(root => root["Core"]!["StopMode"] = "RetiredMode");
        bool refreshing = false, fail = false; int errors = 0;
        /// <summary>模拟面板展示错误，文档事务仍由真实Document处理。</summary>
        /// <param name="action">控件提交回调。</param>
        void GuardField(Action action) { try { action(); } catch (InvalidOperationException) { errors++; } }
        try
        {
            string before = document.Text; long revision = document.Revision;
            var choices = new[] { "KeepBullets", "ClearBullets" };
            var menu = EditorFieldControls.Choice(document.At("/Core/StopMode"), choices, "/Core/StopMode", GuardField, () => refreshing,
                value => document.Edit(root => { root["Core"]!["StopMode"] = value; if (fail) throw new InvalidOperationException("预期提交失败"); }), label: value => "显示：" + value);
            host.AddChild(menu); choices[0] = "不应进入文档";
            Check(menu.GetItemText(menu.Selected) == "RetiredMode（当前值）" && document.Text == before, "未知旧值保持可见且不自动规范化");
            menu.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            Check(document.Revision == revision, "选择未知原值占位项不制造修改");
            menu.Select(0); menu.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); menu.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            Check(document.Revision == revision + 1 && document.At("/Core/StopMode")!.ToString() == "KeepBullets", "显示标签、外部选项变动及重复信号不改变实际一次提交");
            fail = true; menu.Select(1); menu.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(errors == 1 && menu.Selected == 0 && document.At("/Core/StopMode")!.ToString() == "KeepBullets", "失败选择回滚文档和菜单显示");
            fail = false; refreshing = true; menu.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); refreshing = false;
            Check(document.Revision == revision + 1, "重建期间选择信号不写入文档");
            document.Undo(); Check(document.Text == before, "共用菜单修改可完整撤销到无效旧值");
            // 布尔开关沿用相同事务边界，不把失败的显示值当作成功基线。
            document.Edit(root => root["VNodes"]!["Core"]!["AAngleIsSameAsAngle"] = false); revision = document.Revision;
            var toggle = EditorFieldControls.Toggle(false, "/VNodes/Core/AAngleIsSameAsAngle", GuardField, () => refreshing,
                value => document.Edit(root => { root["VNodes"]!["Core"]!["AAngleIsSameAsAngle"] = value; if (fail) throw new InvalidOperationException("预期布尔失败"); }));
            host.AddChild(toggle);
            toggle.ButtonPressed = true; toggle.EmitSignal(BaseButton.SignalName.Toggled, true);
            Check(document.Revision == revision + 1 && document.At("/VNodes/Core/AAngleIsSameAsAngle")!.GetValue<bool>(), "布尔开关重复信号仅提交一次");
            fail = true; toggle.ButtonPressed = false;
            Check(errors == 2 && toggle.ButtonPressed && document.Revision == revision + 1, "布尔事务失败保留真实值与控件状态");
        }
        finally { host.Free(); }
    }

    /// <summary>通过真实按钮验证共用数组操作的边界、原文、深复制与事务回滚。</summary>
    private void VerifyArrayControls()
    {
        var document = new EmitterDocument();
        document.Edit(root => root["VNodes"]!["BaseAttributes"] = JsonNode.Parse("""[{"Angle":"PI / 3"},null]"""));
        string original = document.Text;
        long revision = document.Revision;
        bool refreshing = false, fail = false; int errors = 0;
        var row = new HBoxContainer { Visible = false }; AddChild(row);
        try
        {
            EditorArrayControls.AddActions(row, "/VNodes/BaseAttributes/0", 0, 2, GuardArray, () => refreshing,
                direction => EditArray(array => EditorArrayControls.Move(array, 0, direction)),
                duplicate: () => EditArray(array => EditorArrayControls.Duplicate(array, 0)),
                remove: () => EditArray(array => array.RemoveAt(0)));
            var buttons = row.GetChildren().OfType<Button>().ToArray();
            Check(buttons.Select(button => button.Text).SequenceEqual(new[] { "复制", "↑", "↓", "×" })
                && buttons[1].Disabled && !buttons[2].Disabled, "共用数组按钮保持顺序并禁用首项上移");
            buttons[1].EmitSignal(BaseButton.SignalName.Pressed);
            refreshing = true; buttons[2].EmitSignal(BaseButton.SignalName.Pressed); refreshing = false;
            Check(document.Revision == revision, "边界按钮与重建信号不产生数组事务");
            buttons[2].EmitSignal(BaseButton.SignalName.Pressed);
            Check(document.At("/VNodes/BaseAttributes/0") is null && document.At("/VNodes/BaseAttributes/1/Angle")!.ToString() == "PI / 3",
                "数组移动保留显式null和表达式原文");
            document.Undo(); Check(document.Text == original, "数组移动完整撤销");
            buttons[0].EmitSignal(BaseButton.SignalName.Pressed);
            document.Edit(root => root["VNodes"]!["BaseAttributes"]![1]!["Angle"] = 9);
            Check(document.At("/VNodes/BaseAttributes/0/Angle")!.ToString() == "PI / 3"
                && document.At("/VNodes/BaseAttributes")!.AsArray().Count == 3, "复制数组项不共享可变JSON节点");
            document.Undo(); document.Undo();
            fail = true; buttons[2].EmitSignal(BaseButton.SignalName.Pressed); fail = false;
            Check(errors == 1 && document.Text == original, "失败数组动作由原文档事务完整回滚");
            buttons[3].EmitSignal(BaseButton.SignalName.Pressed);
            Check(document.At("/VNodes/BaseAttributes")!.AsArray().Count == 1 && document.At("/VNodes/BaseAttributes/0") is null,
                "删除项只删除指定内容且保持余下null");
            document.Undo(); Check(document.Text == original, "删除数组项可完整撤销");
        }
        finally { row.Free(); }

        /// <summary>执行真实文档事务，模拟动作完成前的失败。</summary>
        /// <param name="change">数组变更。</param>
        void EditArray(Action<JsonArray> change) => document.Edit(root =>
        {
            change(root["VNodes"]!["BaseAttributes"]!.AsArray());
            if (fail) throw new InvalidOperationException("预期数组操作失败");
        });
        /// <summary>模拟所属面板的错误展示，不吞掉断言异常。</summary>
        /// <param name="action">数组控件回调。</param>
        void GuardArray(Action action) { try { action(); } catch (InvalidOperationException) { errors++; } }
    }

    /// <summary>通过Emitter路径表单验证模式切换、表达式保留、未知值和完整撤销。</summary>
    /// <param name="editor">已就绪的实际编辑器。</param>
    /// <returns>全部延迟表单刷新完成后的任务。</returns>
    private async Task VerifyPathFieldControls(EmitterEditor editor)
    {
        editor.NewEmitter();
        editor.Document.Edit(root => { root["VNodes"] = EditorSchema.Creator("VPath"); root["VNodes"]!["PathQueue"]![0]!["EndMoveQueue"]![0]!["X"] = "PI * 40"; });
        editor.Refresh(); await Settle();
        const string pointer = "/VNodes/PathQueue/0/PathMode";
        foreach (int mode in new[] { 1, 2 })
        {
            var menu = Descendants<OptionButton>(editor).Single(control => control.HasMeta("json_path") && control.GetMeta("json_path").AsString() == pointer);
            menu.Select(mode); menu.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode); await Settle();
            editor.Document.Validate();
            Check(editor.Document.At("/VNodes/PathQueue/0/EndMoveQueue/0/X")!.ToString() == "PI * 40", "路径模式切换保留端点表达式");
        }
        editor.Document.Edit(root => root["VNodes"]!["PathQueue"]![0]!["Y"] = "40*sin(PI*t)"); editor.Refresh(); await Settle();
        string original = editor.Document.Text; long revision = editor.Document.Revision;
        var current = Descendants<OptionButton>(editor).Single(control => control.HasMeta("json_path") && control.GetMeta("json_path").AsString() == pointer);
        current.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        Check(editor.Document.Text == original && editor.Document.Revision == revision, "重复选择当前路径模式不覆盖自定义函数");
        Press(editor, "切为瞄准玩家直线"); await Settle();
        Check(editor.Document.At("/VNodes/PathQueue/0/Type")!.ToString() == "AimPlayer", "Emitter与Boss提供相同瞄准简写切换");
        Press(editor, "撤销"); await Settle();
        Check(editor.Document.Text == original, "撤销路径种类切换完整恢复函数原文");
        editor.Document.Edit(root => { root["VNodes"]!["PathQueue"]![0]!["PathMode"] = "RetiredPath"; root["VNodes"]!["PathQueue"]![0]!["Legacy"] = "保留"; });
        editor.Refresh(); await Settle();
        current = Descendants<OptionButton>(editor).Single(control => control.HasMeta("json_path") && control.GetMeta("json_path").AsString() == pointer);
        Check(current.GetItemText(current.Selected).Contains("RetiredPath"), "真实表单显示无效旧模式");
        current.Select(0); current.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); await Settle();
        Check(editor.Document.At("/VNodes/PathQueue/0/Legacy")!.ToString() == "保留", "显式切换仅移除已知模式字段，不清除未知原文");
        Reject(() => editor.Document.Validate(), "未知旧字段继续由正式加载器拒绝");
        editor.NewEmitter();
        editor.Document.Edit(root => root["VNodes"]!["Core"]!["AAngleIsSameAsAngle"] = null); editor.Refresh(); await Settle();
        Check(Descendants<LineEdit>(editor).Any(control => control.HasMeta("json_path") && control.GetMeta("json_path").AsString() == "/VNodes/Core/AAngleIsSameAsAngle" && control.Text == "null"),
            "非法布尔null保留文本，不能被开关伪装为false");
        editor.NewEmitter(); editor.Refresh(); await Settle();
    }
}
