using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>协议模式的合法值与字段集合，供严格读取及编辑菜单共用，不包含界面或运行状态。</summary>
internal sealed class ProtocolModes
{
    /// <summary>普通路径模式与各自允许的JSON字段，保持声明顺序。</summary>
    internal static ProtocolModes Path { get; } = new(new[] { "PathMode", "StartMoveQueue", "EndMoveQueue" },
        ("XY", Array.Empty<string>()),
        ("Bezier", new[] { "ControlPoints", "Samples" }),
        ("Function", new[] { "AxisMode", "X", "Y", "TMin", "TMax", "Samples" }));
    /// <summary>玩家瞄准路径简写独占Type及两个偏移字段。</summary>
    internal static ProtocolModes PathAim { get; } = new(new[] { "Type" }, ("AimPlayer", new[] { "X", "Y" }));
    /// <summary>共用位移动作的合法字段，不改变数值、随机宽度或求值规则。</summary>
    internal static ProtocolModes Displacement { get; } = new(new[] { "Type" },
        ("XYMove", new[] { "X", "Y" }), ("PMove", new[] { "Angle", "Dist" }), ("TarMove", new[] { "X", "Y", "Dist" }));
    /// <summary>Boss移动模式；入场目标在随机及序列模式中仍可省略。</summary>
    internal static ProtocolModes BossMovement { get; } = new(new[] { "Type", "Speed" },
        ("Center", new[] { "Target" }),
        ("RandomCircle", new[] { "Target", "Center", "MinRadius", "MaxRadius", "StartMs", "IntervalMs" }),
        ("RandomRect", new[] { "Target", "Min", "Max", "StartMs", "IntervalMs" }),
        ("Sequence", new[] { "Target", "Targets", "StartMs", "IntervalMs" }),
        ("Path", new[] { "PathQueue", "PointCount", "Loop" }));
    /// <summary>函数路径坐标系，顺序对应编辑菜单。</summary>
    internal static IReadOnlyList<string> PathAxes { get; } = Array.AsReadOnly(new[] { "Absolute", "Relative" });
    /// <summary>阶段切换条件；具体血量和计时语义仍由阶段执行。</summary>
    internal static IReadOnlyList<string> EndConditions { get; } = Array.AsReadOnly(new[] { "Health", "Time", "HealthOrTime" });

    // 字段列表均冻结，私有字典只在构造时填写，不向调用方外借可变集合。
    private readonly Dictionary<string, IReadOnlyList<string>> _fields = new(StringComparer.Ordinal);
    /// <summary>合法模式值，按声明顺序排列且不可改写。</summary>
    internal IReadOnlyList<string> Values { get; }
    /// <summary>冻结通用字段与各模式专属字段，不把编辑模板当作协议白名单。</summary>
    /// <param name="common">所有模式共用的字段名。</param>
    /// <param name="modes">模式名与专属字段，顺序稳定。</param>
    private ProtocolModes(string[] common, params (string Name, string[] Fields)[] modes)
    {
        Values = Array.AsReadOnly(modes.Select(mode => mode.Name).ToArray());
        foreach (var mode in modes) _fields.Add(mode.Name, Array.AsReadOnly(common.Concat(mode.Fields).ToArray()));
    }
    /// <summary>取得模式对应字段；未知值返回空，调用方保留原值并提供所属领域诊断。</summary>
    /// <param name="mode">区分大小写的协议字符串，可为空。</param>
    /// <returns>只读合法字段列表，未知模式返回null。</returns>
    internal IReadOnlyList<string>? Fields(string? mode) => mode is not null && _fields.TryGetValue(mode, out var fields) ? fields : null;
}
