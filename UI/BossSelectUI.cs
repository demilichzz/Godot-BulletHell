using Godot;
using System;
using System.Collections.Generic;

/// <summary>以四列网格展示目录，提交语义请求并显示控制器决定的选择。</summary>
public partial class BossSelectUI : Control, IBossSelectionView
{
    // 网格列数，索引按从左至右、从上至下递增。
    public const int Columns = 4;
    /// <summary>当前选中项，空目录时为负一。</summary>
    public int SelectedIndex { get; private set; } = -1;
    /// <summary>请求选择目录下标，视图不决定是否有效。</summary>
    public event Action<int>? SelectionRequested;
    /// <summary>请求确认当前选项，不直接切换场景。</summary>
    public event Action? ConfirmationRequested;
    /// <summary>AI加入开关变化通知，参数为当前勾选状态。</summary>
    public event Action<bool>? AISelectionRequested;
    /// <summary>请求网格方向导航，具体边界由控制器处理。</summary>
    public event Action<Vector2I>? NavigationRequested;
    /// <summary>鼠标勾选的AI加入选项，不抢占方向键及空格导航。</summary>
    public CheckBox AIOption { get; } = new()
    {
        Name = "AIOption", Text = "加入 AI", Position = new Vector2(960, 105),
        FocusMode = FocusModeEnum.None
    };
    // 卡片列表及关联的有序目录。
    private readonly List<BossSelectItem> _items = new();
    private BossCatalog _catalog = null!;
    // 选中项说明与滚动容器。
    private readonly Label _details = new() { Position = new Vector2(100, 710), Size = new Vector2(1080, 40) };
    private readonly ScrollContainer _scroll = new() { Position = new Vector2(100, 170), Size = new Vector2(1080, 500) };
    /// <summary>构建当前目录的选择画面。</summary>
    /// <param name="catalog">已验证的 Boss 有序目录。</param>
    /// <param name="aiEnabled">恢复的AI选项，默认关闭。</param>
    public void Initialize(BossCatalog catalog, bool aiEnabled = false)
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
        AIOption.ButtonPressed = aiEnabled;
        AIOption.Toggled += OnAIToggled;
        AddChild(AIOption);
        AddChild(_scroll);
        AddChild(_details);
        // 固定四列，目录增加时自动形成更多行并允许纵向滚动。
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 24);
        _scroll.AddChild(grid);
        for (int index = 0; index < catalog.Entries.Count; index++)
        {
            // 当前目录配置与新建卡片。
            var data = catalog.Entries[index];
            var item = new BossSelectItem();
            item.Initialize(data, index);
            item.Chosen += Select;
            grid.AddChild(item);
            _items.Add(item);
        }
        if (_items.Count == 0) _details.Text = "暂无可挑战的 Boss，请先添加 Boss 配置。";
    }
    /// <summary>选择一个有效卡片，保持高亮、说明和滚动位置一致。</summary>
    /// <param name="index">目录零基索引，越界时忽略。</param>
    public void ShowSelection(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        if (SelectedIndex >= 0) _items[SelectedIndex].SetSelected(false);
        SelectedIndex = index;
        _items[index].SetSelected(true);
        _details.Text = $"{_catalog.Entries[index].DisplayName}    HP {_catalog.Entries[index].MaxHp}    [空格] 开始挑战";
        Callable.From(() => { if (IsInsideTree()) _scroll.EnsureControlVisible(_items[SelectedIndex]); }).CallDeferred();
    }
    /// <summary>请求选择指定卡片；鼠标和外部调用共用语义入口。</summary>
    /// <param name="index">期望的目录零基下标。</param>
    public void Select(int index) => SelectionRequested?.Invoke(index);
    /// <summary>提交方向请求，不在视图执行网格业务。</summary>
    /// <param name="direction">右下为正的单位网格方向。</param>
    public void Navigate(Vector2I direction) => NavigationRequested?.Invoke(direction);
    /// <summary>提交确认请求，空目录许可由控制器决定。</summary>
    public void Confirm() => ConfirmationRequested?.Invoke();
    /// <summary>转发鼠标AI选项变化，不触发关卡确认。</summary>
    /// <param name="enabled">是否加入陪练AI。</param>
    private void OnAIToggled(bool enabled) => AISelectionRequested?.Invoke(enabled);

    /// <summary>离开节点树时解除卡片事件订阅。</summary>
    public override void _ExitTree()
    {
        // 各卡片随场景释放，同时移除指向本界面的回调。
        foreach (var item in _items) item.Chosen -= Select;
        AIOption.Toggled -= OnAIToggled;
    }
}
