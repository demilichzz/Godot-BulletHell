using Godot;
using System;
using System.Collections.Generic;

/// <summary>以四列网格展示目录，统一管理鼠标选择、方向导航和空格确认。</summary>
public partial class BossSelectUI : Control
{
    // 网格列数，索引按从左至右、从上至下递增。
    public const int Columns = 4;
    /// <summary>当前选中项，空目录时为负一。</summary>
    public int SelectedIndex { get; private set; } = -1;
    /// <summary>选择变化通知，参数为 Boss 标识。</summary>
    public event Action<string>? SelectionChanged;
    /// <summary>空格确认通知，参数为 Boss 标识。</summary>
    public event Action<string>? Confirmed;
    // 卡片列表及关联的有序目录。
    private readonly List<BossSelectItem> _items = new();
    private BossCatalog _catalog = null!;
    // 选中项说明与滚动容器。
    private readonly Label _details = new() { Position = new Vector2(100, 710), Size = new Vector2(1080, 40) };
    private readonly ScrollContainer _scroll = new() { Position = new Vector2(100, 170), Size = new Vector2(1080, 500) };
    /// <summary>构建当前目录的选择画面。</summary>
    /// <param name="catalog">已验证的 Boss 有序目录。</param>
    /// <param name="selectedId">返回场景时恢复的 Boss 标识，未找到则选中第一项。</param>
    public void Initialize(BossCatalog catalog, string selectedId)
    {
        _catalog = catalog;
        Size = BattleConfig.Bounds.Size;
        MouseFilter = MouseFilterEnum.Ignore;
        // 共用字体主题，子卡片继承字体和字号。
        Theme = new Theme { DefaultFont = GD.Load<Font>("res://Assets/fonts/lxgl/LXGWWenKaiGBScreen.ttf"), DefaultFontSize = 22 };
        AddChild(new ColorRect { Size = Size, Color = new Color(0.035f, 0.05f, 0.08f), MouseFilter = MouseFilterEnum.Ignore });
        var title = new Label { Text = "选择 Boss", Position = new Vector2(100, 55), MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 36);
        AddChild(title);
        AddChild(new Label { Text = "鼠标点击或方向键选择 · 空格开始挑战", Position = new Vector2(100, 115), MouseFilter = MouseFilterEnum.Ignore });
        AddChild(_scroll);
        AddChild(_details);
        // 固定四列，目录增加时自动形成更多行并允许纵向滚动。
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 24);
        _scroll.AddChild(grid);
        var initial = 0;
        for (int index = 0; index < catalog.Entries.Count; index++)
        {
            // 当前目录配置与新建卡片。
            var data = catalog.Entries[index];
            var item = new BossSelectItem();
            item.Initialize(data, index);
            item.Chosen += Select;
            grid.AddChild(item);
            _items.Add(item);
            if (data.Id == selectedId) initial = index;
        }
        if (_items.Count == 0) _details.Text = "暂无可挑战的 Boss，请先添加 Boss 配置。";
        else Select(initial);
    }
    /// <summary>选择一个有效卡片，保持高亮、说明和滚动位置一致。</summary>
    /// <param name="index">目录零基索引，越界时忽略。</param>
    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        if (SelectedIndex >= 0) _items[SelectedIndex].SetSelected(false);
        SelectedIndex = index;
        _items[index].SetSelected(true);
        _details.Text = $"{_catalog.Entries[index].DisplayName}    HP {_catalog.Entries[index].MaxHp}    [空格] 开始挑战";
        SelectionChanged?.Invoke(_catalog.Entries[index].Id);
        Callable.From(() => { if (IsInsideTree()) _scroll.EnsureControlVisible(_items[SelectedIndex]); }).CallDeferred();
    }
    /// <summary>按网格方向移动，边缘不环绕，末行缺项时选择最后一项。</summary>
    /// <param name="direction">单位网格方向，X向右、Y向下；只接受四轴方向。</param>
    public void Navigate(Vector2I direction)
    {
        if (SelectedIndex < 0) return;
        // 当前行列和候选索引，用于阻止左右跨行。
        var column = SelectedIndex % Columns;
        var target = SelectedIndex;
        if (direction == Vector2I.Left && column > 0) target--;
        else if (direction == Vector2I.Right && column < Columns - 1 && target + 1 < _items.Count) target++;
        else if (direction == Vector2I.Up && target >= Columns) target -= Columns;
        else if (direction == Vector2I.Down && (target / Columns + 1) * Columns < _items.Count) target = Math.Min(target + Columns, _items.Count - 1);
        Select(target);
    }
    /// <summary>确认当前有效选项，空目录时不触发。</summary>
    public void Confirm()
    {
        if (SelectedIndex >= 0) Confirmed?.Invoke(_catalog.Entries[SelectedIndex].Id);
    }
    /// <summary>处理未被界面消费的方向键和空格，确认不接受长按重复。</summary>
    /// <param name="inputEvent">当前键盘事件。</param>
    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true } key) return;
        if (key.IsActionPressed("stage_left", true)) Navigate(Vector2I.Left);
        else if (key.IsActionPressed("stage_right", true)) Navigate(Vector2I.Right);
        else if (key.IsActionPressed("stage_up", true)) Navigate(Vector2I.Up);
        else if (key.IsActionPressed("stage_down", true)) Navigate(Vector2I.Down);
        else if (key.IsActionPressed("stage_confirm") && !key.Echo) Confirm();
        else return;
        GetViewport().SetInputAsHandled();
    }
    /// <summary>离开节点树时解除卡片事件订阅。</summary>
    public override void _ExitTree()
    {
        // 各卡片随场景释放，同时移除指向本界面的回调。
        foreach (var item in _items) item.Chosen -= Select;
    }
}
