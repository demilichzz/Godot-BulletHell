using Godot;
using System;

/// <summary>展示一个 Boss 的图片和名称，点击仅改变选择。</summary>
public partial class BossSelectItem : PanelContainer
{
    /// <summary>该选项在目录中的零基索引。</summary>
    public int Index { get; private set; }
    /// <summary>鼠标选择通知，参数为零基索引。</summary>
    public event Action<int>? Chosen;
    /// <summary>构建不抢占导航焦点的 Boss 卡片。</summary>
    /// <param name="data">需要展示的 Boss 配置。</param>
    /// <param name="index">目录中的零基索引。</param>
    public void Initialize(BossData data, int index)
    {
        Index = index;
        Name = $"BossItem{index}";
        CustomMinimumSize = new Vector2(240, 230);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = data.DisplayName;
        // 纵向排列图片和名称，子控件忽略鼠标以使整张卡片可点击。
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(column);
        column.AddChild(new TextureRect
        {
            Texture = data.Portrait ?? data.Texture,
            CustomMinimumSize = new Vector2(200, 170),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        });
        column.AddChild(new Label { Text = data.DisplayName, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
        SetSelected(false);
    }
    /// <summary>刷新高亮边框。</summary>
    /// <param name="selected">是否为当前选中项。</param>
    public void SetSelected(bool selected)
    {
        // 所有卡片保持相同边框厚度，切换选择不引起布局跳动。
        var style = new StyleBoxFlat
        {
            BgColor = selected ? new Color(0.13f, 0.21f, 0.3f) : new Color(0.07f, 0.1f, 0.15f),
            BorderColor = selected ? new Color(0.4f, 0.85f, 1f) : new Color(0.2f, 0.27f, 0.35f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12
        };
        AddThemeStyleboxOverride("panel", style);
    }
    /// <summary>鼠标左键按下只通知选择，不直接开始战斗。</summary>
    /// <param name="inputEvent">当前界面输入事件。</param>
    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Chosen?.Invoke(Index);
            AcceptEvent();
        }
    }
}
