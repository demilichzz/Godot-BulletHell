using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>面板静态布局的有界缓存；只保存显示快照与不可变移动定义，不共享运行树或替代显式校验。</summary>
internal sealed partial class EditorLayoutCache
{
    // 每个面板最多保留当前Emitter和当前Boss各一项；换文档或内容修订后替换。
    private Entry<Snapshot>? _emitter;
    private Entry<BossLayout>? _boss;
    /// <summary>成功构建的静态内容次数，供定向验证和成本测量，不计选择变化。</summary>
    internal int BuildCount { get; private set; }

    /// <summary>校验当前Emitter或复用仍有效的静态结果，再复制到可交互画布。</summary>
    /// <param name="document">当前已应用文档，草稿仍按原界面规则独立保存。</param>
    /// <param name="canvas">接收独立显示数组的画布。</param>
    internal void ApplyEmitter(EmitterDocument document, EditorCanvas canvas)
    {
        if (_emitter is null || !_emitter.Matches(document, -1, null))
        {
            // 临时运行定义只用于正式校验和静态采样，不进入缓存。
            var emitter = document.Validate();
            var dependencies = new Dependencies(); dependencies.AddEmitter(emitter);
            var layout = new EditorLayout(emitter);
            _emitter = new Entry<Snapshot>(document, -1, dependencies, new Snapshot(layout.Markers, layout.Paths));
            BuildCount++;
        }
        _emitter.Value.Apply(canvas);
    }

    /// <summary>校验所选Boss及所有引用；仅切换阶段时复用定义，按需计算该阶段静态几何。</summary>
    /// <param name="document">共享目录文档。</param>
    /// <param name="bossIndex">有效零基Boss下标。</param>
    /// <param name="phaseIndex">有效阶段下标，-1表示Boss共用属性。</param>
    /// <param name="session">当前共享引用及草稿来源。</param>
    /// <param name="canvas">接收布局的画布。</param>
    /// <returns>当前校验内容对应的状态说明。</returns>
    internal string ApplyBoss(EmitterDocument document, int bossIndex, int phaseIndex, EditorSession session, EditorCanvas canvas)
    {
        if (_boss is null || !_boss.Matches(document, bossIndex, session))
        {
            var dependencies = new Dependencies();
            string json = document.At("/Bosses/" + bossIndex)?.ToJsonString() ?? throw new InvalidOperationException("请先选择Boss。");
            // 纯引用校验按规范化路径去重，本次解析完成后不保留Emitter工厂或运行对象。
            var data = JsonData.Parse(json, document.FilePath, root => BossData.Read(root,
                validateEmitter: path => dependencies.CheckEmitter(path, session)));
            dependencies.AddTexture(data.Texture!.ResourcePath);
            if (data.Portrait is not null) dependencies.AddTexture(data.Portrait.ResourcePath);
            _boss = new Entry<BossLayout>(document, bossIndex, dependencies, new BossLayout(data));
            BuildCount++;
        }
        _boss.Value.Get(phaseIndex).Apply(canvas);
        return _boss.Value.Status;
    }

    /// <summary>固定文档身份、修订和依赖来源，不将草稿作为已应用内容。</summary>
    /// <typeparam name="T">不向画布外借可变集合的静态结果。</typeparam>
    private sealed class Entry<T>
    {
        // 修订不能跨文档或另存后的文件身份复用；来源由本次检查冻结。
        private readonly EmitterDocument _document;
        private readonly long _revision;
        private readonly string _path;
        private readonly int _bossIndex;
        private readonly Dependencies _dependencies;
        /// <summary>只由缓存内部消费的静态结果。</summary>
        internal T Value { get; }
        /// <summary>登记已经完成校验的静态结果。</summary>
        /// <param name="document">本次文档身份。</param>
        /// <param name="bossIndex">Boss下标，Emitter为-1。</param>
        /// <param name="dependencies">本次实际读取的外部依赖。</param>
        /// <param name="value">独立静态结果。</param>
        internal Entry(EmitterDocument document, int bossIndex, Dependencies dependencies, T value)
        {
            _document = document; _revision = document.Revision; _path = document.FilePath;
            _bossIndex = bossIndex; _dependencies = dependencies; Value = value;
        }
        /// <summary>检查文档和依赖是否仍匹配，外部异常交还正式校验生成诊断。</summary>
        /// <param name="document">当前文档。</param>
        /// <param name="bossIndex">当前Boss下标。</param>
        /// <param name="session">Boss引用来源；Emitter布局为空。</param>
        /// <returns>全部身份、内容和资源检查一致时为真。</returns>
        internal bool Matches(EmitterDocument document, int bossIndex, EditorSession? session)
            => ReferenceEquals(_document, document) && _revision == document.Revision && _path == document.FilePath
                && _bossIndex == bossIndex && _dependencies.Matches(session);
    }

    /// <summary>独立冻结的显示数组；应用时复制折线，画布拖动或外部改写不能污染下次复用。</summary>
    private sealed class Snapshot
    {
        private readonly EditorCanvas.Marker[] _markers;
        private readonly (string Path, int Basis, Vector2[] Points)[] _paths;
        /// <summary>复制一次计算完成的布局，不保存Creator、节点或资源对象。</summary>
        /// <param name="markers">不可变图标记录。</param>
        /// <param name="paths">世界逻辑像素折线。</param>
        internal Snapshot(IEnumerable<EditorCanvas.Marker> markers, IEnumerable<(string Path, int Basis, Vector2[] Points)> paths)
        {
            _markers = markers.ToArray();
            _paths = paths.Select(path => (path.Path, path.Basis, path.Points.ToArray())).ToArray();
        }
        /// <summary>复制显示数组并请求重画，不改变画布选择或过滤模式。</summary>
        /// <param name="canvas">当前显示目标。</param>
        internal void Apply(EditorCanvas canvas)
        {
            canvas.Markers.Clear(); canvas.Markers.AddRange(_markers); canvas.Paths.Clear();
            foreach (var path in _paths) canvas.Paths.Add((path.Path, path.Basis, path.Points.ToArray()));
            canvas.QueueRedraw();
        }
    }

    /// <summary>一个Boss的静态布局来源，只有选中的阶段才采样，避免其他阶段采样错误影响当前选择。</summary>
    private sealed class BossLayout
    {
        // 只保留值数据，不保留BossData的贴图、加载委托或任何战斗实体。
        private readonly Vector2 _spawn;
        private readonly string _name;
        private readonly BossMovement[] _movements;
        private readonly Dictionary<int, Snapshot> _layouts = new();
        /// <summary>当前内容已通过校验的阶段数量和总血量说明。</summary>
        internal string Status { get; }
        /// <summary>提取正式校验结果中的静态显示数据。</summary>
        /// <param name="data">临时的已校验Boss配置。</param>
        internal BossLayout(BossData data)
        {
            _spawn = data.SpawnPosition; _name = data.DisplayName;
            _movements = data.Phases.Select(phase => phase.Movement).ToArray();
            Status = $"校验通过 · {data.PhaseCount}阶段 · 总HP {data.MaxHp} · Emitter文件独立保存";
        }
        /// <summary>按原画布算法建立某个阶段的静态图标与路径，保持采样和浮点运算顺序。</summary>
        /// <param name="phase">阶段下标，-1仅显示出生点。</param>
        /// <returns>不包含可变运行对象的显示快照。</returns>
        internal Snapshot Get(int phase)
        {
            if (_layouts.TryGetValue(phase, out var existing)) return existing;
            var markers = new List<EditorCanvas.Marker> { new("", 0, _spawn, _name) };
            var paths = new List<(string Path, int Basis, Vector2[] Points)>();
            if (phase >= 0)
            {
                var movement = _movements[phase];
                if (movement.Target is not null) markers.Add(new EditorCanvas.Marker("", 1, BossMovement.Point(movement.Target), "入场目标"));
                if (movement.Type == "RandomRect")
                {
                    Vector2 min = BossMovement.Point(movement.Min), max = BossMovement.Point(movement.Max);
                    paths.Add(("", 0, new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y), min }));
                }
                if (movement.Type == "RandomCircle")
                    foreach (double radius in new[] { movement.MinRadius, movement.MaxRadius }.Distinct())
                        paths.Add(("", 0, Enumerable.Range(0, 65).Select(index => VMath.PolarMove(BossMovement.Point(movement.Center), index * Math.Tau / 64, radius)).ToArray()));
                if (movement.Targets is not null) paths.Add(("", 0, movement.Targets.Select(BossMovement.Point).ToArray()));
                if (movement.Type == "Path")
                {
                    // 瞄准玩家段继续只在正式预览中冻结；静态布局不查询战斗服务。
                    var geometry = VPathJson.Read(movement.PathQueue, movement.PointCount);
                    if (geometry.Segments.All(segment => segment.AimPlayerOffset is null))
                        paths.Add(("", 0, geometry.Sample(_spawn, movement.PointCount).Select(offset => _spawn + offset).ToArray()));
                }
            }
            var result = new Snapshot(markers, paths); _layouts.Add(phase, result); return result;
        }
    }
}
