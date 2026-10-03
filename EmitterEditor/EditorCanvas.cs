using Godot;
using System;
using System.Collections.Generic;

/// <summary>保持1280×800逻辑坐标的场地视图，静态图标选择与动态预览共用缩放。</summary>
public partial class EditorCanvas : Control
{
    /// <summary>一个Creator基础项的静态图标。</summary>
    /// <param name="Path">编辑文档中的Creator指针。</param>
    /// <param name="Basis">基础项零基索引。</param>
    /// <param name="Position">逻辑世界坐标。</param>
    /// <param name="Label">节点名称。</param>
    public sealed record Marker(string Path, int Basis, Vector2 Position, string Label);
    /// <summary>按树顺序建立的图标。</summary>
    public List<Marker> Markers { get; } = new();
    /// <summary>用户点选图标时回调。</summary>
    public Action<Marker>? Selected;
    /// <summary>当前选中Creator的文档指针。</summary>
    public string SelectedPath { get; set; } = "/VNodes";
    /// <summary>选中基础项下标。</summary>
    public int SelectedBasis { get; set; }
    /// <summary>动态预览纹理；为空时显示静态布局。</summary>
    public Texture2D? PreviewTexture { get; set; }
    // 重叠图标按连续点击循环选择，不丢失零位移父子节点。
    private Vector2 _lastClick = new(-1000, -1000);
    private int _overlapIndex;
    /// <summary>设置画布交互和最小尺寸。</summary>
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(400, 360);
        ClipContents = true;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "点击图标选择Creator及基础项；重叠图标连续点击切换。基础布局不含轮次增量、随机与父对象运动。";
        Resized += QueueRedraw;
    }
    /// <summary>按控件尺寸计算保持比例的显示区域。</summary>
    /// <returns>控件局部矩形。</returns>
    public Rect2 ArenaRect()
    {
        // 留出标签空间，只改变显示比例。
        float scale = Math.Max(0.01f, Math.Min((Size.X - 20) / 1280, (Size.Y - 20) / 800));
        // 逻辑场地转换后的显示尺寸。
        var size = new Vector2(1280, 800) * scale;
        return new Rect2((Size - size) / 2, size);
    }
    /// <summary>将逻辑坐标映射到画布局部坐标。</summary>
    /// <param name="world">逻辑像素位置。</param>
    /// <returns>控件局部位置。</returns>
    public Vector2 ToCanvas(Vector2 world) { var rect = ArenaRect(); return rect.Position + world * (rect.Size.X / 1280); }
    /// <summary>建立基础项图标；子树参考父Creator第一基础项的静态位置。</summary>
    /// <param name="emitter">解析后的定义，CopySource已由游戏加载器展开。</param>
    public void Rebuild(VBulletEmitter emitter)
    {
        Markers.Clear();
        AddMarkers(emitter.Root, "/VNodes", emitter.Core.RefObject == "Boss" ? BattleConfig.BossSpawn : Vector2.Zero);
        QueueRedraw();
    }
    /// <summary>递归计算基础位置，不创建运行成员或抽样随机。</summary>
    /// <param name="creator">当前生成器。</param>
    /// <param name="path">文档指针。</param>
    /// <param name="origin">父基础参考位置。</param>
    private void AddMarkers(VNodeCreator creator, string path, Vector2 origin)
    {
        // 第一基础项供子树静态参考，复杂路径显示参考锚点而非运行采样点。
        Vector2 first = origin;
        // 普通路径复用现有采样结果的首点；瞄准路径的玩家快照属于动态预览。
        Vector2 pathOffset = creator is VPathCreator pathCreator && !System.Linq.Enumerable.Any(pathCreator.PathQueue, segment => segment.AimPlayerOffset is not null)
            ? pathCreator.SampleGeometry(origin)[0] : Vector2.Zero;
        // 按声明顺序处理的零基下标。
        for (int index = 0; index < creator.BaseAttributes.Count; index++)
        {
            // 当前基础项顺序应用位移后的世界坐标。
            Vector2 position = origin + pathOffset;
            // 当前基础位移动作，严格按原顺序求值。
            foreach (var action in creator.BaseAttributes[index].RefMoveQueue)
                position = action.Type == "PMove" ? VMath.PolarMove(position, action.Angle ?? 0, action.Dist ?? 0)
                    : action.Type == "TarMove" ? VMath.TargetMove(position, new Vector2((float)(action.X ?? 0), (float)(action.Y ?? 0)), action.Dist ?? 0)
                    : position + new Vector2((float)(action.X ?? 0), (float)(action.Y ?? 0));
            if (index == 0) first = position;
            Markers.Add(new Marker(path, index, position, (creator.Core.Name ?? creator.Core.Type) + $" [{index}]"));
        }
        // 按声明顺序处理的零基下标。
        for (int index = 0; index < creator.Children.Count; index++) AddMarkers(creator.Children[index], path + "/Children/" + index, first);
    }
    /// <summary>返回点击范围内的图标，允许重叠循环。</summary>
    /// <param name="point">控件局部鼠标坐标。</param>
    /// <returns>命中的图标列表。</returns>
    public List<Marker> HitMarkers(Vector2 point) => Markers.FindAll(marker => ToCanvas(marker.Position).DistanceTo(point) <= 13);
    /// <summary>处理鼠标左键选中图标。</summary>
    /// <param name="input">画布局部输入事件。</param>
    public override void _GuiInput(InputEvent input)
    {
        if (PreviewTexture is not null || input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        // 鼠标命中范围内的全部重叠图标。
        var hits = HitMarkers(click.Position);
        if (hits.Count == 0) return;
        _overlapIndex = click.Position.DistanceTo(_lastClick) < 5 ? (_overlapIndex + 1) % hits.Count : 0;
        _lastClick = click.Position;
        Selected?.Invoke(hits[_overlapIndex]); AcceptEvent();
    }
    /// <summary>绘制静态基础布局或预览纹理，UI文本保持原始分辨率。</summary>
    public override void _Draw()
    {
        // 保持场地比例的画布局部矩形。
        var rect = ArenaRect();
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101923"));
        if (PreviewTexture is not null) { DrawTextureRect(PreviewTexture, rect, false); return; }
        DrawRect(rect, new Color("142233"));
        DrawCircle(ToCanvas(BattleConfig.ArenaCenter), 400 * rect.Size.X / 1280, new Color("1b3043"));
        DrawArc(ToCanvas(BattleConfig.ArenaCenter), 400 * rect.Size.X / 1280, 0, Mathf.Tau, 96, new Color("46677f"), 1.5f);
        // 稀疏网格和角色锚点均以逻辑坐标转换。
        for (int x = 0; x <= 1280; x += 100) DrawLine(ToCanvas(new Vector2(x, 0)), ToCanvas(new Vector2(x, 800)), new Color(0.3f, 0.5f, 0.6f, 0.12f));
        // 横向网格线的逻辑纵坐标。
        for (int y = 0; y <= 800; y += 100) DrawLine(ToCanvas(new Vector2(0, y)), ToCanvas(new Vector2(1280, y)), new Color(0.3f, 0.5f, 0.6f, 0.12f));
        DrawCircle(ToCanvas(BattleConfig.BossSpawn), 5, new Color("ffba71"));
        DrawCircle(ToCanvas(BattleConfig.PlayerSpawn), 5, new Color("74e9d4"));
        // 当前基础位置图标。
        foreach (var marker in Markers)
        {
            // 当前图标转换后的控件局部坐标。
            var point = ToCanvas(marker.Position);
            // 当前选中节点或基础项的匹配状态。
            bool selected = marker.Path == SelectedPath && marker.Basis == SelectedBasis;
            DrawCircle(point, selected ? 10 : 7, selected ? new Color("ffd48a") : new Color("72cfff"));
            DrawArc(point, selected ? 13 : 9, 0, Mathf.Tau, 24, selected ? Colors.White : new Color("2f617c"), 1);
            if (selected) DrawString(ThemeDB.FallbackFont, point + new Vector2(16, -12), marker.Label, HorizontalAlignment.Left, -1, 15, Colors.White);
        }
    }
}
