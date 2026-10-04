using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>按批次采样公共路径几何，复用节点、时间线和子生成器流程。</summary>
public sealed class VPathCreator : VNodeCreator
{
    // 仅持有不可变几何；每批采样结果由原有SpawnBatch独立持有。
    private VPathGeometry? _geometry;
    /// <summary>加载时冻结的路径段；起终点在每次批次触发时求值。</summary>
    public IReadOnlyList<VPathSegmentAttribute> PathQueue => _geometry?.Segments ?? Array.Empty<VPathSegmentAttribute>();
    /// <summary>加载后的公共几何，静态查看无需建立另一个Creator。</summary>
    internal VPathGeometry Geometry => _geometry ?? throw new InvalidOperationException("PathQueue尚未加载。");

    /// <summary>建立路径类型的空生成器，使用前须通过JSON加载有效定义。</summary>
    public VPathCreator() => Core = new VNodeCoreAttribute { Type = "VPath" };

    /// <summary>通过共用协议读取器加载几何，不读取战斗对象。</summary>
    /// <param name="element">非空PathQueue数组。</param>
    internal void ReadPathQueue(JsonElement element)
    {
        try { _geometry = VPathJson.Read(element, Core.Amount); }
        catch (JsonException error) { throw WithIdentity(error); }
    }

    /// <summary>验证生成器专属的基础项与数量约束，表达式由公共几何一次编译。</summary>
    protected override void ValidateAttributes()
    {
        base.ValidateAttributes();
        if (BaseAttributes.Count != 1) throw new JsonException("VPath的BaseAttributes必须恰好包含一个元素。");
        Geometry.ValidatePointCount(Core.Amount);
    }

    /// <summary>在实际批次时刻采样，只有瞄准段才读取当前战斗玩家。</summary>
    /// <param name="source">本次触发的父参考世界点，逻辑像素。</param>
    /// <returns>本批独立的只读偏移，不创建成员。</returns>
    internal IReadOnlyList<Vector2> SampleGeometry(Vector2 source)
    {
        try { return Geometry.Sample(source, Core.Amount, () => GlobalEvent.GetPlayer().GlobalPosition); }
        catch (JsonException error) { throw WithIdentity(error); }
    }

    /// <summary>在任何成员出生前计算完整路径；失败不登记部分延迟出生。</summary>
    /// <param name="source">触发时的世界参考点，逻辑像素。</param>
    /// <returns>供本批全部成员共用的固定偏移。</returns>
    protected override IReadOnlyList<Vector2>? PrepareBatchPositions(Vector2 source) => SampleGeometry(source);

    /// <summary>在领域边界补充Creator身份，不让公共数学层依赖运行对象。</summary>
    /// <param name="error">包含路径段位置的几何错误。</param>
    /// <returns>包含Creator与段位置的完整诊断。</returns>
    private JsonException WithIdentity(JsonException error) => new($"{Core.Id}({Core.Name}): {error.Message}", error);
}
