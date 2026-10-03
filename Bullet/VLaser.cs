using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>激光的显示与伤害阶段；End为释放时点，不另占持续时间。</summary>
public enum VLaserStage
{
    // 细线预警，不参与碰撞。
    Warning,
    // 展开到正式宽度，不参与碰撞。
    Expand,
    // 正式生效，命中后仍继续存在。
    Active,
    // 收束消退，不参与碰撞。
    Fade,
    // 达到配置持续时间，等待管理器释放。
    End
}

/// <summary>共用实体实现定点四阶段激光与沿固定世界路径移动的有限长度光束。</summary>
public partial class VLaser : VBullet
{
    /// <summary>出生后不可变的激光定义。</summary>
    public VLaserAttribute Settings { get; private set; } = new();
    /// <summary>固定世界折线的累计总长度，逻辑像素。</summary>
    public double PathLength => _distances[^1];
    /// <summary>未截断的亮芯头部路径距离，像素；越过路径终点后仍增长。</summary>
    public double HeadDistance => Settings.Mode == "Fixed" ? PathLength : Units / (double)VTimerProcessor.UnitsPerSecond * Settings.TravelSpeed;
    /// <summary>亮芯尾部的路径距离，像素，至少为0。</summary>
    public double TailDistance => Settings.Mode == "Fixed" ? 0 : Math.Max(0, HeadDistance - Settings.Length);
    /// <summary>当前主体显示全宽，像素，不含外发光，仅由自身年龄计算。</summary>
    public double VisualWidth { get; private set; }
    /// <summary>实体达到配置结束时刻；路径终点不触发提前结束。</summary>
    public bool HasFinished => Units >= Settings.DurationMs * VTimerProcessor.UnitsPerMillisecond;
    /// <summary>当前逻辑阶段，路径模式只经历Active和End。</summary>
    public VLaserStage Stage
    {
        get
        {
            if (HasFinished) return VLaserStage.End;
            if (Settings.Mode == "Path") return VLaserStage.Active;
            // 在整数时间单位中比较阶段，零时长阶段自动跳过。
            long warning = Settings.WarningMs * VTimerProcessor.UnitsPerMillisecond;
            long expanded = warning + Settings.ExpandMs * VTimerProcessor.UnitsPerMillisecond;
            long active = expanded + Settings.ActiveMs * VTimerProcessor.UnitsPerMillisecond;
            return Units < warning ? VLaserStage.Warning : Units < expanded ? VLaserStage.Expand
                : Units < active ? VLaserStage.Active : VLaserStage.Fade;
        }
    }
    /// <summary>当前实体年龄，内部每秒60000单位，与VNode时间线一致。</summary>
    private long Units => Timeline?.ElapsedUnits ?? VTimeline.SecondsToUnits(Age);
    // 世界路径与累计弧长只在初始化时复制，显示点不会成为逻辑几何来源。
    private Vector2[] _path = Array.Empty<Vector2>();
    private double[] _distances = Array.Empty<double>();
    // 移动激光追加末段延长线供碰撞与危险查询使用；定点直接复用原路径。
    private Vector2[] _collisionPath = Array.Empty<Vector2>();
    private double[] _collisionDistances = Array.Empty<double>();
    // 外光由6层半透明色带近似柔和渐变；固定宽度比例仅影响显示，不参与判定。
    private const int GlowLayerCount = 6;
    private const double GlowWidthRatio = 5;
    private const double CoreWidthRatio = 0.75;
    private readonly Line2D[] _glowLines = new Line2D[GlowLayerCount];
    // 亮芯由路径起终点确定；主体尖端向外延伸，不使用贴图或渲染时钟。
    private readonly Line2D _line = new() { Name = "Laser", JointMode = Line2D.LineJointMode.Round, Antialiased = true, UseParentMaterial = true };
    private readonly Line2D _coreLine = new() { Name = "LaserCore", JointMode = Line2D.LineJointMode.Round, Antialiased = true, UseParentMaterial = true };
    private readonly Curve _taper = new();
    private readonly Curve _coreTaper = new();
    // 出生时解析独立层颜色，避免每次刷新重新解析HTML。
    private Color _coreColor;
    private Color _glowColor;
    // 当前可见的局部折线点，供所有显示层复用。
    private readonly List<Vector2> _visiblePoints = new();
    // 尖端额外插入顶点，让Line2D在收束边界实际采样宽度。
    private readonly List<double> _visibleDistances = new();
    // 只允许内部几何更新修改原生Transform，防止外部直接写Position/Rotation/Scale改变路径。
    private Vector2 _lockedWorld;
    private bool _updatingTransform;
    private bool _configured;

    /// <summary>校验并复制完整几何；定点可用完整路径，省略路径时由起点、角度和长度生成两点。</summary>
    /// <param name="settings">不可为空的激光参数。</param>
    /// <param name="position">有限世界参考点，像素。</param>
    /// <param name="angle">有限弧度，提供路径时必须为0。</param>
    /// <param name="path">固定世界折线，移动模式必填、定点模式可选。</param>
    /// <param name="damage">正整数伤害。</param>
    /// <returns>移除相邻重复点后的独立世界路径。</returns>
    internal static Vector2[] Validate(VLaserAttribute settings, Vector2 position, double angle,
        IReadOnlyList<Vector2>? path, int damage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (!position.IsFinite() || !double.IsFinite(angle) || damage <= 0)
            throw new ArgumentException("激光出生位置、角度或伤害无效。");
        if (settings.Mode == "Fixed" && path is null)
            return new[] { position, VMath.PolarMove(position, angle, settings.Length) };
        if (angle != 0 || path is null || path.Count < 2 || path.Count > 4096)
            throw new ArgumentException("路径激光要求2至4096个固定点，且不能叠加方向角。", nameof(path));
        // 采样曲线可能出现相邻重复位置，仅去除零长度段，不合并非相邻交点。
        var result = new List<Vector2>(path.Count);
        foreach (var point in path)
        {
            if (!point.IsFinite()) throw new ArgumentException("激光路径必须为有限世界坐标。", nameof(path));
            if (result.Count == 0 || result[^1] != point) result.Add(point);
        }
        if (result.Count < 2) throw new ArgumentException("激光路径长度必须大于0。", nameof(path));
        return result.ToArray();
    }

    /// <summary>复用普通弹幕的战斗属性初始化，保持普通速度为0，几何只由本类更新。</summary>
    /// <param name="settings">经过校验的不可变配置。</param>
    /// <param name="position">出生世界参考点，像素。</param>
    /// <param name="angle">定点方向，弧度。</param>
    /// <param name="points">已复制并校验的完整世界路径。</param>
    /// <param name="damage">正整数伤害。</param>
    internal void ConfigureLaser(VLaserAttribute settings, Vector2 position, double angle, Vector2[] points, int damage)
    {
        Settings = settings;
        _coreColor = Color.FromHtml(settings.CoreColor);
        _glowColor = Color.FromHtml(settings.GlowColor ?? settings.Color);
        _path = points;
        _distances = new double[points.Length];
        // 累计距离仅在生成时计算，内部保留双精度。
        for (int index = 1; index < points.Length; index++)
            _distances[index] = _distances[index - 1] + VMath.GetDistanceBetween2Points(points[index - 1], points[index]);
        // 只在出生时扩展到寿命内头部可达的最大弧长，不改变公开的原路径长度。
        _collisionPath = _path;
        _collisionDistances = _distances;
        double travelLimit = settings.DurationMs / 1000.0 * settings.TravelSpeed;
        if (settings.Mode == "Path" && travelLimit > PathLength)
        {
            _collisionPath = _path.Append(ExtendedPointAt(travelLimit)).ToArray();
            _collisionDistances = _distances.Append(travelLimit).ToArray();
        }
        var spawn = VBulletDefaultSet.Get(VBulletType.PlayerSet) with
        {
            Position = position,
            AngleRadians = angle,
            Speed = 0,
            LifeTimeMs = settings.DurationMs,
            Radius = settings.HitWidth * 0.5,
            Damage = damage,
            Team = VBulletTeam.Enemy,
            CircleColor = Color.FromHtml(settings.Color),
            BlendMode = settings.BlendMode == "CoreAdd" ? "Mix" : settings.BlendMode
        };
        VBullet.Validate(spawn);
        Configure(spawn);
        // CoreAdd保留主体与外光的原色；统一提高亮芯绘制顺序，避免后出生激光覆盖先出生的白芯。
        _coreLine.UseParentMaterial = settings.BlendMode != "CoreAdd";
        _coreLine.Material = settings.BlendMode == "CoreAdd" ? AdditiveMaterial : null;
        _coreLine.ZIndex = settings.BlendMode == "CoreAdd" ? 1 : 0;
        _lockedWorld = points[0];
        _configured = true;
    }

    /// <summary>激光出生后拒绝参数动作，确保路径、方向、速度和阶段不被外部改写。</summary>
    /// <param name="parameters">不允许应用的普通节点参数动作。</param>
    public override void ApplyParameters(ParameterActionAttribute parameters)
        => throw new InvalidOperationException("激光出生后不允许修改运动、位置、方向或寿命；请在Laser和PathQueue中定义。");

    /// <summary>依次添加外发光、主体和亮芯；继承的Sprite保持隐藏，不新增独立物理时钟。</summary>
    public override void _Ready()
    {
        base._Ready();
        // 最宽外光最先绘制，随后叠加较窄色带，主体和亮芯覆盖在最上层。
        for (int index = 0; index < _glowLines.Length; index++)
        {
            _glowLines[index] = new Line2D { Name = $"LaserGlow{index}", JointMode = Line2D.LineJointMode.Round, Antialiased = true, UseParentMaterial = true };
            AddChild(_glowLines[index]);
        }
        AddChild(_line);
        AddChild(_coreLine);
        SetNotifyLocalTransform(true);
        RefreshGeometry();
    }

    /// <summary>原生位置、旋转或缩放被外部改变时恢复固定路径上的姿态。</summary>
    /// <param name="what">Godot节点通知编号。</param>
    public override void _Notification(int what)
    {
        if (what == NotificationLocalTransformChanged && _configured && !_updatingTransform) LockTransform();
    }

    /// <summary>屏蔽普通弹幕圆点绘制，激光由分层Line2D显示。</summary>
    public override void _Draw() { }

    /// <summary>将实体世界姿态固定在当前头部，父容器坐标变换不改变世界路径。</summary>
    private void LockTransform()
    {
        // 场景局部位置由父容器反变换求得，避免非零容器位置重复叠加。
        var desired = new Transform2D(0, _lockedWorld);
        if (GetParent() is Node2D parent) desired = parent.GlobalTransform.AffineInverse() * desired;
        if (Transform == desired) return;
        _updatingTransform = true;
        try { Transform = desired; }
        finally { _updatingTransform = false; }
    }

    /// <summary>按逻辑年龄更新裁剪路径和分层显示，不改变业务年龄或随机序列。</summary>
    internal void RefreshGeometry()
    {
        // 定点实体保持在起点，移动实体位置代表沿路径前进的头部。
        double head = HeadDistance, tail = TailDistance;
        _lockedWorld = Settings.Mode == "Fixed" ? _path[0] : ExtendedPointAt(head);
        LockTransform();
        _line.Visible = _coreLine.Visible = !HasFinished && head > tail;
        // 结束或头尾重合时，各层同时隐藏，不留下外发光残影。
        foreach (var glow in _glowLines) glow.Visible = _line.Visible;
        if (!_line.Visible) { VisualWidth = 0; return; }
        // 阶段边界使用整数年龄；平滑函数只决定视觉，不控制碰撞。
        double opacity = 1, width = Settings.Width;
        double warningWidth = Math.Min(2, Settings.Width);
        if (Stage == VLaserStage.Warning) { width = warningWidth; opacity = 0.45; }
        else if (Stage == VLaserStage.Expand)
        {
            double progress = Smooth((Units / (double)VTimerProcessor.UnitsPerMillisecond - Settings.WarningMs) / Settings.ExpandMs);
            width = warningWidth + (Settings.Width - warningWidth) * progress;
            opacity = 0.45 + 0.55 * progress;
        }
        else if (Stage == VLaserStage.Fade)
        {
            double progress = Smooth((Units / (double)VTimerProcessor.UnitsPerMillisecond
                - Settings.WarningMs - Settings.ExpandMs - Settings.ActiveMs) / Settings.FadeMs);
            width *= 1 - progress;
            opacity = 1 - progress;
        }
        // 亮芯两端固定在可见路径起终点；短亮芯按长度限制宽度以保持15度尖角。
        double tip = 0;
        if (Settings.EndCap == "Point")
        {
            tip = Math.Min(width * CoreWidthRatio * 0.5 / Math.Tan(VMath.DegreesToRadians(7.5)), (head - tail) * 0.5);
            width = Math.Min(width, 2 * tip * Math.Tan(VMath.DegreesToRadians(7.5)) / CoreWidthRatio);
        }
        VisualWidth = width;
        // 主体向亮芯两端外延，端部增长只影响显示，不改变固定路径和碰撞几何。
        double bodyTip = tip / CoreWidthRatio;
        double extension = bodyTip - tip;
        var points = BuildVisualPoints(tail - extension, head + extension, bodyTip, _taper);
        double previousOpacity = 0;
        for (int index = 0; index < _glowLines.Length; index++)
        {
            // 六层覆盖主体的5倍至1倍全宽，保留原来由外到内的透明度梯度。
            double layerRatio = GlowWidthRatio - (GlowWidthRatio - 1) * index / (_glowLines.Length - 1.0);
            double progress = (1 + 11.0 * index / (_glowLines.Length - 1)) / 12;
            double cumulativeOpacity = 0.6 * progress * progress * opacity;
            double layerOpacity = (cumulativeOpacity - previousOpacity) / (1 - previousOpacity);
            UpdateVisualLayer(_glowLines[index], points, width * layerRatio, _glowColor, layerOpacity, _taper);
            previousOpacity = cumulativeOpacity;
        }
        UpdateVisualLayer(_line, points, width, CircleColor, opacity, _taper);
        // 动画只改变亮芯宽度和透明度，不内缩其端点；移动模式仍按既定头尾进度裁剪。
        var corePoints = tip > 0 ? BuildVisualPoints(tail, head, tip, _coreTaper) : points;
        UpdateVisualLayer(_coreLine, corePoints, width * CoreWidthRatio, _coreColor, opacity, _coreTaper);
    }

    /// <summary>裁剪显示折线并插入尖端根部顶点，沿弧长近似曲线尖端。</summary>
    /// <param name="tail">显示尾部的路径弧长，像素；主体可延伸至路径起点之前。</param>
    /// <param name="head">大于tail的显示头部弧长，像素；主体可延伸至路径终点之后。</param>
    /// <param name="tip">单端收束长度，非负像素，圆端为0。</param>
    /// <param name="taper">接收该层归一化宽度曲线，不影响业务几何。</param>
    /// <returns>含路径折点和尖端根部的局部像素坐标。</returns>
    private Vector2[] BuildVisualPoints(double tail, double head, double tip, Curve taper)
    {
        _visiblePoints.Clear();
        _visibleDistances.Clear();
        _visibleDistances.Add(tail);
        // 保留范围内的路径折点，避免曲线裁剪后切弦。
        for (int index = 0; index < _path.Length; index++)
            if (_distances[index] > tail && _distances[index] < head) _visibleDistances.Add(_distances[index]);
        if (tip > 0)
        {
            _visibleDistances.Add(tail + tip);
            _visibleDistances.Add(head - tip);
            // 曲线参数按当前裁剪后的弧长归一化，直线两侧与中心线各成7.5度。
            float fraction = (float)Math.Min(0.5, tip / (head - tail));
            taper.ClearPoints();
            taper.AddPoint(Vector2.Zero, 0, 0, Curve.TangentMode.Linear, Curve.TangentMode.Linear);
            taper.AddPoint(new Vector2(fraction, 1), 0, 0, Curve.TangentMode.Linear, Curve.TangentMode.Linear);
            if (fraction < 0.5f) taper.AddPoint(new Vector2(1 - fraction, 1), 0, 0, Curve.TangentMode.Linear, Curve.TangentMode.Linear);
            taper.AddPoint(new Vector2(1, 0), 0, 0, Curve.TangentMode.Linear, Curve.TangentMode.Linear);
        }
        _visibleDistances.Add(head);
        _visibleDistances.Sort();
        // 相邻重复距离只输出一次，零长度显示边不交给Line2D。
        double previous = double.NegativeInfinity;
        foreach (double distance in _visibleDistances)
        {
            if (distance != previous) _visiblePoints.Add(ToLocal(ExtendedPointAt(distance)));
            previous = distance;
        }
        return _visiblePoints.ToArray();
    }

    /// <summary>沿原折线查询，越过两端时沿首末非零段外延；末端同时供移动、显示和碰撞使用。</summary>
    /// <param name="distance">累计弧长，像素；负数仅用于起点前的显示尖端。</param>
    /// <returns>有限世界像素坐标，超过坐标范围时拒绝。</returns>
    private Vector2 ExtendedPointAt(double distance)
    {
        if (distance >= 0 && distance <= PathLength) return PointAt(distance);
        // 使用累计弧长归一化双精度方向，避免单精度方向相减或距离乘法溢出。
        int index = distance < 0 ? 1 : _path.Length - 1;
        double span = _distances[index] - _distances[index - 1];
        double offset = distance < 0 ? distance : distance - PathLength;
        Vector2 origin = distance < 0 ? _path[0] : _path[^1];
        var point = VMath.ValidatePathPoint((
            origin.X + ((double)_path[index].X - _path[index - 1].X) / span * offset,
            origin.Y + ((double)_path[index].Y - _path[index - 1].Y) / span * offset));
        return new Vector2((float)point.X, (float)point.Y);
    }
    /// <summary>设置单层光束的裁剪路径、颜色、宽度和端部形状。</summary>
    /// <param name="line">对应外光、主体或亮芯的显示节点。</param>
    /// <param name="points">当前裁剪路径的局部像素坐标。</param>
    /// <param name="width">该层全宽，非负像素。</param>
    /// <param name="color">该层不透明颜色。</param>
    /// <param name="opacity">阶段与层级共同决定的透明度，范围0至1。</param>
    /// <param name="taper">该层的尖端宽度曲线；圆端不使用。</param>
    private void UpdateVisualLayer(Line2D line, Vector2[] points, double width, Color color, double opacity, Curve taper)
    {
        line.Points = points;
        line.Width = (float)Math.Min(width, float.MaxValue);
        line.DefaultColor = new Color(color, (float)opacity);
        line.BeginCapMode = line.EndCapMode = Settings.EndCap == "Round" ? Line2D.LineCapMode.Round : Line2D.LineCapMode.None;
        line.WidthCurve = Settings.EndCap == "Point" ? taper : null;
    }

    /// <summary>查询固定折线上指定弧长的位置，首末距离自动截断。</summary>
    /// <param name="distance">路径累计距离，逻辑像素。</param>
    /// <returns>世界逻辑像素位置。</returns>
    public Vector2 PointAt(double distance)
    {
        if (!double.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        if (distance <= 0) return _path[0];
        if (distance >= PathLength) return _path[^1];
        // 二分查找对应段；初始化已去除相邻重复点。
        int index = Array.BinarySearch(_distances, distance);
        if (index >= 0) return _path[index];
        int upper = ~index;
        return _path[upper - 1].Lerp(_path[upper], (float)((distance - _distances[upper - 1]) / (_distances[upper] - _distances[upper - 1])));
    }

    /// <summary>仅在本步与生效区间重合的时间检测玩家和光束，不追溯预警期间的穿越。</summary>
    /// <param name="previousUnits">推进前的实体整数年龄。</param>
    /// <param name="targetStart">玩家本步起点，世界像素。</param>
    /// <param name="targetEnd">玩家本步终点，世界像素。</param>
    /// <param name="targetRadius">玩家判定半径，非负像素。</param>
    /// <returns>生效期间存在接触时为真。</returns>
    internal bool Intersects(long previousUnits, Vector2 targetStart, Vector2 targetEnd, double targetRadius)
    {
        // 消退起点和EndMs不包含在危险区间中；保留结束前本步的有效轨迹。
        long start = Settings.Mode == "Fixed" ? (Settings.WarningMs + Settings.ExpandMs) * VTimerProcessor.UnitsPerMillisecond : 0;
        long end = Settings.Mode == "Fixed" ? start + Settings.ActiveMs * VTimerProcessor.UnitsPerMillisecond
            : Settings.DurationMs * VTimerProcessor.UnitsPerMillisecond;
        if (Units < start || previousUnits >= end) return false;
        double from = Math.Max(previousUnits, start), to = Math.Min(Units, end);
        if (to >= end) to = Math.BitDecrement((double)end);
        if (to < from) return false;
        double step = Units - previousUnits;
        Vector2 p0 = targetStart.Lerp(targetEnd, step == 0 ? 1 : (float)((from - previousUnits) / step));
        Vector2 p1 = targetStart.Lerp(targetEnd, step == 0 ? 1 : (float)((to - previousUnits) / step));
        double h0 = Settings.Mode == "Fixed" ? PathLength : from / VTimerProcessor.UnitsPerSecond * Settings.TravelSpeed;
        double h1 = Settings.Mode == "Fixed" ? PathLength : to / VTimerProcessor.UnitsPerSecond * Settings.TravelSpeed;
        // 尖角只在足够宽的主体内判定，额外收进半宽，避免圆判定伸入透明尖端。
        double inset = Settings.EndCap == "Point" ? Settings.TipLength + Settings.HitWidth * 0.5 : 0;
        return VMath.SweptPolylineWindowHit(_collisionPath, _collisionDistances, p0, p1, h0, h1,
            Settings.Mode == "Fixed" ? PathLength : Settings.Length, inset, Settings.HitWidth * 0.5 + targetRadius);
    }

    /// <summary>将视觉进度限制在0至1并平滑端点，不影响阶段时刻。</summary>
    /// <param name="value">归一化阶段进度。</param>
    /// <returns>0至1的平滑值。</returns>
    private static double Smooth(double value)
    {
        // 端点导数为0，预警、展开和消退之间不会宽度跳变。
        double t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
