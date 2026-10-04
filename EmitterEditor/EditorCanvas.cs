using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>请求把基础项移动到世界逻辑坐标；最后参数表示松手提交。</summary>
    public Func<Marker, Vector2, bool, bool>? MoveRequested;
    /// <summary>取消拖动时恢复原始布局。</summary>
    public Action? MoveCanceled;
    /// <summary>0全部，1当前及后代，2仅当前；仅控制静态画布显示。</summary>
    public int DisplayMode { get; set; }
    /// <summary>当前静态路径折线，坐标为世界逻辑像素。</summary>
    public List<(string Path, int Basis, Vector2[] Points)> Paths { get; } = new();
    /// <summary>当前选中Creator的文档指针。</summary>
    public string SelectedPath { get; set; } = "/VNodes";
    /// <summary>选中基础项下标。</summary>
    public int SelectedBasis { get; set; }
    /// <summary>动态预览纹理；为空时显示静态布局。</summary>
    public Texture2D? PreviewTexture { get; set; }
    // 重叠图标按连续点击循环选择，不丢失零位移父子节点。
    private Vector2 _lastClick = new(-1000, -1000);
    private int _overlapIndex;
    // 一次鼠标手势固定目标与原始世界位置，松手才写入文档。
    private Marker? _pressed;
    private Vector2 _pressWorld, _pressCanvas;
    private bool _dragging;
    /// <summary>设置画布交互和最小尺寸。</summary>
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(400, 360);
        ClipContents = true;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "点击选中，按住左键拖动；Esc取消。重叠图标连续点击切换。路径瞄准以固定玩家位置为参考。";
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
    /// <summary>把画布坐标转换为1280×800逻辑世界坐标。</summary>
    /// <param name="point">画布局部坐标。</param>
    /// <returns>世界逻辑像素坐标。</returns>
    public Vector2 ToWorld(Vector2 point) { var rect = ArenaRect(); return (point - rect.Position) * (1280 / rect.Size.X); }
    /// <summary>建立基础项图标；子树参考父Creator第一基础项的静态位置。</summary>
    /// <param name="emitter">解析后的定义，CopySource已由游戏加载器展开。</param>
    public void Rebuild(VBulletEmitter emitter) => ApplyLayout(new EditorLayout(emitter));
    /// <summary>原子替换完整静态布局，保留显示过滤与选择。</summary>
    /// <param name="layout">已经计算成功的布局。</param>
    public void ApplyLayout(EditorLayout layout)
    {
        Markers.Clear(); Markers.AddRange(layout.Markers); Paths.Clear(); Paths.AddRange(layout.Paths);
        QueueRedraw();
    }
    /// <summary>判断一个Creator是否在当前显示范围内。</summary>
    /// <param name="path">Creator指针。</param>
    /// <returns>是否绘制图标、路径并允许命中。</returns>
    public bool IsDisplayed(string path) => DisplayMode == 0 || path == SelectedPath ||
        DisplayMode == 1 && (SelectedPath.Length == 0 || path.StartsWith(SelectedPath + "/Children/", StringComparison.Ordinal));
    /// <summary>返回点击范围内的图标，允许重叠循环。</summary>
    /// <param name="point">控件局部鼠标坐标。</param>
    /// <returns>命中的图标列表。</returns>
    public List<Marker> HitMarkers(Vector2 point) => Markers.FindAll(marker => IsDisplayed(marker.Path) && ToCanvas(marker.Position).DistanceTo(point) <= 13);
    /// <summary>处理鼠标左键选中图标。</summary>
    /// <param name="input">画布局部输入事件。</param>
    public override void _GuiInput(InputEvent input)
    {
        if (PreviewTexture is not null) return;
        if (input is InputEventMouseMotion motion && _pressed is not null)
        {
            if (!_dragging && motion.Position.DistanceTo(_pressCanvas) < 6) return;
            _dragging = true; MoveRequested?.Invoke(_pressed, _pressed.Position + ToWorld(motion.Position) - _pressWorld, false); AcceptEvent(); return;
        }
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } release) { FinishDrag(release.Position); return; }
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        // 鼠标命中范围内的全部重叠图标。
        var hits = HitMarkers(click.Position);
        if (hits.Count == 0) return;
        _overlapIndex = click.Position.DistanceTo(_lastClick) < 5 ? (_overlapIndex + 1) % hits.Count : 0;
        _lastClick = click.Position;
        _pressed = hits[_overlapIndex]; _pressCanvas = click.Position; _pressWorld = ToWorld(click.Position); _dragging = false;
        Selected?.Invoke(_pressed); AcceptEvent();
    }
    /// <summary>提交一次拖动并清除手势；释放到画布外也使用同一入口。</summary>
    /// <param name="point">松手的画布局部坐标。</param>
    private void FinishDrag(Vector2 point)
    {
        var marker = _pressed; bool dragged = _dragging; _pressed = null; _dragging = false;
        if (marker is not null && dragged) MoveRequested?.Invoke(marker, marker.Position + ToWorld(point) - _pressWorld, true);
    }
    /// <summary>捕获画布外松手和Esc，防止拖动悬挂。</summary>
    /// <param name="input">视口输入。</param>
    public override void _Input(InputEvent input)
    {
        if (_pressed is null) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { CancelDrag(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) { FinishDrag(GetLocalMousePosition()); GetViewport().SetInputAsHandled(); }
    }
    /// <summary>窗口失焦时取消临时移动。</summary>
    /// <param name="what">Godot生命周期通知。</param>
    public override void _Notification(int what) { if (what == NotificationWMWindowFocusOut) CancelDrag(); }
    /// <summary>取消手势；已提交的文档不受影响。</summary>
    public void CancelDrag() { bool restore = _dragging; _pressed = null; _dragging = false; if (restore) MoveCanceled?.Invoke(); }
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
        // 连线只连接同时可见的父第一基础项与子基础项。
        foreach (var marker in Markers.Where(marker => IsDisplayed(marker.Path)))
        {
            int split = marker.Path.LastIndexOf("/Children/", StringComparison.Ordinal);
            if (split < 0) continue;
            var parent = Markers.Find(item => item.Path == marker.Path[..split] && item.Basis == 0);
            if (parent is not null && IsDisplayed(parent.Path)) DrawLine(ToCanvas(parent.Position), ToCanvas(marker.Position), new Color("476d82"), 1.5f);
        }
        // 路径与节点共享过滤；折线只作布局参考，不创建运行弹幕。
        foreach (var path in Paths.Where(path => IsDisplayed(path.Path)))
            if (path.Points.Length > 1) DrawPolyline(path.Points.Select(ToCanvas).ToArray(), path.Path == SelectedPath ? new Color("ffc97a") : new Color("66b3a8"), 2, true);
        // 当前基础位置图标。
        foreach (var marker in Markers)
        {
            if (!IsDisplayed(marker.Path)) continue;
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
