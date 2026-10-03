using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>在真实战场渲染中测量Boss01三个阶段与2000颗弹幕负载，不改变正式战斗规则。</summary>
public partial class PerformanceVerification : Node
{
    // 默认场景顺序；压力场景只由本验证入口创建。
    private string[] _cases = { "phase01", "phase02", "phase03", "moving2000", "churn2000" };
    // 正式采样与预热墙钟秒数，命令行可覆盖。
    private double _seconds = 30, _warmup = 12;
    // 当前战场及场景索引，场景切换时暂时停测。
    private Main? _scene;
    // 可选树压力场景每颗一批，覆盖2000个活批次的遍历开销。
    private VBulletEmitter? _pressureEmitter;
    private VBulletCreator? _pressureCreator;
    // 压力网格覆盖整个画面，用矩形保持移动负载；反射场景另用所选形状。
    private readonly IRegionShape _pressureOutside = new RectangleRegionShape(BattleConfig.Bounds);
    private VReflectionRegion? _pressureReflection;
    private int _caseIndex, _birthIndex, _steps;
    // 墙钟只用于基准统计，战斗仍然调用固定60Hz入口。
    private long _started, _previous, _allocated;
    private double _warmupStepMax, _initializationMs, _fillMs;
    private int[] _gcStart = new int[3];
    private bool _sampling, _uncapped, _defaultPresent;
    // 预留容量以减少测量自身扩容；结果写盘放在场景测量结束后。
    private readonly List<double> _frames = new(20000), _logic = new(10000), _work = new(10000);
    private readonly List<double> _enginePhysics = new(20000), _engineProcess = new(20000), _drawCalls = new(20000);
    private readonly List<int> _counts = new(10000);
    private readonly List<object> _results = new();
    private string _output = "res://.tools/performance-current.json";
    private object _environment = null!;
    private int _minimizedFrames;

    /// <summary>读取基准参数并延迟启动独立战场。</summary>
    public override void _Ready()
    {
        // 参数仅影响基准时长、渲染帧率上限和结果文件。
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--perf-case=")) _cases = argument[12..].Split(',');
            if (argument.StartsWith("--perf-seconds=")) _seconds = double.Parse(argument[15..], System.Globalization.CultureInfo.InvariantCulture);
            if (argument.StartsWith("--perf-warmup=")) _warmup = double.Parse(argument[14..], System.Globalization.CultureInfo.InvariantCulture);
            if (argument.StartsWith("--perf-output=")) _output = argument[14..];
            if (argument == "--perf-uncapped") _uncapped = true;
            if (argument == "--perf-default-present") _defaultPresent = true;
        }
        if (_seconds <= 0 || _warmup < 0) throw new ArgumentOutOfRangeException(nameof(_seconds));
        Engine.MaxFps = _uncapped || _defaultPresent ? 0 : 60;
        if (!_defaultPresent && DisplayServer.GetName() != "headless") DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _environment = new
        {
            Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            Godot = Engine.GetVersionInfo()["string"].AsString(),
            Display = DisplayServer.GetName(),
            Cpu = OS.GetProcessorName(),
            Gpu = RenderingServer.GetVideoAdapterName(),
            Resolution = GetViewport().GetVisibleRect().Size.ToString(),
            MaxFps = Engine.MaxFps,
            PhysicsHz = Engine.PhysicsTicksPerSecond,
            Vsync = DisplayServer.GetName() == "headless" ? "unavailable" : DisplayServer.WindowGetVsyncMode().ToString(),
            RefreshHz = DisplayServer.GetName() == "headless" ? 0 : DisplayServer.ScreenGetRefreshRate(),
            Build = OS.IsDebugBuild() ? "Godot debug / .NET Debug" : "release",
            SampleSeconds = _seconds,
            WarmupSeconds = _warmup
        };
        GD.Print(JsonSerializer.Serialize(_environment));
        Callable.From(BeginCase).CallDeferred();
    }

    /// <summary>创建真实Main场景，锁定阶段或构造可见的2000颗压力弹幕。</summary>
    private void BeginCase()
    {
        // 每组全新战斗及默认种子，关闭玩家攻击以免提前切阶段。
        long before = Stopwatch.GetTimestamp();
        _fillMs = 0;
        _pressureEmitter = null;
        _pressureCreator = null;
        _pressureReflection = _cases[_caseIndex] switch
        {
            "reflectcircle2000" => new VReflectionRegion(new CircleRegionShape(new Vector2(640, 400), 300)),
            "reflectrect2000" => new VReflectionRegion(new RectangleRegionShape(new Rect2(340, 100, 600, 600))),
            _ => null
        };
        _scene = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        AddChild(_scene);
        var battle = _scene.Battle;
        battle.SetPhysicsProcess(false);
        battle.Player.Attack.Stop();
        string name = _cases[_caseIndex];
        if (name == "phase02") battle.Boss.TrySwitchAdjacentPhase(1);
        else if (name == "phase03") { battle.Boss.TrySwitchAdjacentPhase(1); battle.Boss.TrySwitchAdjacentPhase(1); }
        else if (name == "idle") battle.Boss.Stop();
        else if (name == "delayed2000")
        {
            battle.Boss.Stop();
            battle.Player.Position = new Vector2(640, 780);
            // 2000个基础点按1ms错开出生，2秒寿命与2秒周期维持约2000颗。
            var definition = new
            {
                Core = new { RefObject = (string?)null },
                VNodes = new
                {
                    Core = new { Type = "VBullet", Amount = 1, LifeTimeMs = 2000, Radius = 4, VisualScale = 2 },
                    Display = new { TextureName = "Scale", TextureIndex = 0 },
                    BaseAttributes = Enumerable.Range(0, 2000).Select(index => new
                    {
                        Speed = 60, SpawnDelayMs = index + 1,
                        RefMoveQueue = new[] { new { Type = "XYMove", X = 100 + index % 50 * 20, Y = 80 + index / 50 * 14 } }
                    }).ToArray(),
                    Timeline = new[] { new { StartMs = 0, IntervalMs = 2000 } },
                    MemberTimeline = new[] { new { StartMs = 1000, Set = new { Angle = 0, Speed = 60 } } }
                }
            };
            _pressureEmitter = VBulletEmitter.FromJson(JsonSerializer.Serialize(definition));
            _pressureEmitter.Start(battle.Boss, battle.Bullets);
        }
        else if (name is "moving2000" or "churn2000" or "tree2000" or "treechurn2000" or "reflectcircle2000" or "reflectrect2000")
        {
            battle.Boss.Stop();
            battle.Player.Position = new Vector2(640, 780);
            if (name is "tree2000" or "treechurn2000")
            {
                _pressureEmitter = VBulletEmitter.FromJson("""
        {"Core": {"RefObject": null}, "VNodes": {"Core": {"Type": "VBullet", "LifeTimeMs": 10000000, "OutsideRegion": {"Type":"Rectangle","X":0,"Y":0,"Width":1280,"Height":800}, "Radius": 4, "VisualScale": 2}, "Display": {}, "BaseAttributes": [{"Speed": 60}], "AddAttributes": {}}}
        """);
                _pressureEmitter.Start(battle.Boss, battle.Bullets);
                _pressureCreator = (VBulletCreator)_pressureEmitter.Root;
            }
            long fillStarted = Stopwatch.GetTimestamp();
            for (int index = 0; index < 2000; index++) SpawnPressure(name is "churn2000" or "treechurn2000" ? (index + 1) / 1000.0 : 10000);
            _fillMs = Stopwatch.GetElapsedTime(fillStarted).TotalMilliseconds;
        }
        else if (name != "phase01") throw new ArgumentException("未知性能场景：" + name);
        _initializationMs = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
        _frames.Clear(); _logic.Clear(); _work.Clear(); _counts.Clear();
        _enginePhysics.Clear(); _engineProcess.Clear(); _drawCalls.Clear();
        _steps = _minimizedFrames = 0;
        _allocated = 0;
        _warmupStepMax = 0;
        _sampling = false;
        _started = _previous = Stopwatch.GetTimestamp();
        GD.Print($"PERF_BEGIN {name} init_ms={_initializationMs:F3}");
    }

    /// <summary>生成始终位于可见区域的压力测试子弹，沿用正式创建及时间线入口。</summary>
    /// <param name="life">出生后的正数总寿命，秒。</param>
    private void SpawnPressure(double life)
    {
        // 50×40格点跨越画面；横向运动出界后由基准绕回，避免离屏剔除降低负载。
        int index = _birthIndex++ % 2000;
        VBullet bullet;
        if (_pressureCreator is not null)
        {
            // 和直接创建场景相同格点、寿命、速度，只改变运行存储及遍历入口。
            _pressureCreator.Emit(_pressureEmitter!, _scene!.Battle.Bullets, null, null,
                new Vector2(100 + index % 50 * 20, 80 + index / 50 * 14));
            bullet = (VBullet)_pressureCreator.Batches[^1][0];
            bullet.ApplyParameters(new ParameterActionAttribute { LifeTimeMs = VTimerProcessor.SecondsToMilliseconds(life) });
        }
        else bullet = _scene!.Battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.ScaleSet) with
        {
            Position = _pressureReflection is null
                ? new Vector2(100 + index % 50 * 20, 80 + index / 50 * 14)
                : new Vector2(450 + index % 50 * 7, 240 + index / 50 * 8),
            AngleRadians = _pressureReflection is null ? 0 : index * Math.Tau / 2000,
            Speed = _pressureReflection is null ? 60 : 600,
            OutsideRegion = _pressureReflection?.Shape ?? _pressureOutside,
            Reflectable = _pressureReflection is not null,
            ReflectionRegion = _pressureReflection,
            LifeTimeMs = VTimerProcessor.SecondsToMilliseconds(life),
            VisualScale = 2,
            Radius = 4
        })!;
        if ((_cases[_caseIndex] is "churn2000" or "treechurn2000") && life > 1)
            bullet.Timeline!.At(1000, () => bullet.ApplyParameters(new ParameterActionAttribute { Speed = 60, Angle = 0 }));
    }

    /// <summary>测量正式固定步和测试补弹开销，保持每次模拟推进1/60秒。</summary>
    /// <param name="delta">引擎物理间隔；模拟不使用墙钟步长。</param>
    public override void _PhysicsProcess(double delta)
    {
        if (_scene is null) return;
        // 分配统计只覆盖固定步及压力补弹，不包含结果数组与JSON写盘。
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long begin = Stopwatch.GetTimestamp();
        var battle = _scene.Battle;
        battle.StepFixed(Vector2.Zero, false);
        double logic = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        if (_cases[_caseIndex] is "moving2000" or "churn2000" or "tree2000" or "treechurn2000" or "reflectcircle2000" or "reflectrect2000")
        {
            foreach (var bullet in battle.Bullets.ActiveBullets)
                if (bullet.Position.X > 1180) bullet.Position = new Vector2(100, bullet.Position.Y);
            while (battle.Bullets.ActiveCount < 2000) SpawnPressure(_cases[_caseIndex] is "churn2000" or "treechurn2000" ? 2 : 10000);
        }
        double work = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (_sampling)
        {
            _logic.Add(logic); _work.Add(work); _counts.Add(battle.Bullets.ActiveCount);
            _allocated += bytes;
            _steps++;
        }
        else _warmupStepMax = Math.Max(_warmupStepMax, work);
    }

    /// <summary>按真实墙钟采集呈现循环间隔与引擎监控值，不把headless结果视为渲染帧率。</summary>
    /// <param name="delta">引擎过程间隔秒数，统计使用单调墙钟。</param>
    public override void _Process(double delta)
    {
        if (_scene is null) return;
        long now = Stopwatch.GetTimestamp();
        double elapsed = Stopwatch.GetElapsedTime(_started, now).TotalSeconds;
        if (_sampling)
        {
            _frames.Add(Stopwatch.GetElapsedTime(_previous, now).TotalMilliseconds);
            _enginePhysics.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
            _engineProcess.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            _drawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            if (DisplayServer.GetName() != "headless" && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized) _minimizedFrames++;
        }
        else if (elapsed >= _warmup)
        {
            _sampling = true;
            for (int index = 0; index < 3; index++) _gcStart[index] = GC.CollectionCount(index);
        }
        _previous = now;
        if (elapsed < _warmup + _seconds) return;
        FinishCase();
    }

    /// <summary>计算分位数与预算超时次数，不在测量窗口内调用。</summary>
    /// <param name="values">毫秒耗时样本或监控计数。</param>
    /// <returns>均值、中位数、尾部分位数与超过20/25/33.33毫秒次数。</returns>
    private static object Stats(List<double> values)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new { Count = 0 };
        return new
        {
            Count = sorted.Length,
            Mean = sorted.Average(),
            P50 = sorted[(int)((sorted.Length - 1) * 0.50)],
            P95 = sorted[(int)((sorted.Length - 1) * 0.95)],
            P99 = sorted[(int)((sorted.Length - 1) * 0.99)],
            Max = sorted[^1],
            Over20 = sorted.Count(value => value > 20),
            Over25 = sorted.Count(value => value > 25),
            Over33 = sorted.Count(value => value > 1000.0 / 30)
        };
    }

    /// <summary>保存完整原始样本与统计，再释放本组战斗并继续下一组。</summary>
    private void FinishCase()
    {
        var result = new
        {
            Case = _cases[_caseIndex],
            Fps = 1000 / _frames.Average(),
            ActualSampleSeconds = _frames.Sum() / 1000,
            InitializationMs = _initializationMs,
            PressureFillMs = _fillMs,
            WarmupStepMaxMs = _warmupStepMax,
            FrameMs = Stats(_frames),
            LogicMs = Stats(_logic),
            IncludingHarnessMs = Stats(_work),
            EnginePhysicsMs = Stats(_enginePhysics),
            EngineProcessMs = Stats(_engineProcess),
            DrawCalls = Stats(_drawCalls),
            BulletsMin = _counts.Min(), BulletsMax = _counts.Max(), BulletsMean = _counts.Average(),
            Steps = _steps,
            AllocatedBytesPerStep = _allocated / (double)_steps,
            GcCollections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - _gcStart[index]).ToArray(),
            ManagedMemoryBytes = GC.GetTotalMemory(false),
            ProcessWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
            MinimizedFrames = _minimizedFrames,
            RawFrameMs = _frames.ToArray(), RawLogicMs = _logic.ToArray()
        };
        _results.Add(result);
        File.WriteAllText(ProjectSettings.GlobalizePath(_output), JsonSerializer.Serialize(new { Environment = _environment, Results = _results }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PERF_RESULT {result.Case} fps={result.Fps:F2} bullets={result.BulletsMin}-{result.BulletsMax} frame={JsonSerializer.Serialize(result.FrameMs)} logic={JsonSerializer.Serialize(result.LogicMs)}");
        _scene!.Free();
        _scene = null;
        _caseIndex++;
        _birthIndex = 0;
        if (_caseIndex == _cases.Length) GetTree().Quit();
        else Callable.From(BeginCase).CallDeferred();
    }
}
