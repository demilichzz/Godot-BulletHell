using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>Creator的单批延迟出生和统一属性求值。</summary>
public partial class VNodeCreator
{
    // 单调批次序号及当前公开批次的顺序索引，不保留已取消的待生任务。
    private long _nextBatchOrder;
    private readonly Dictionary<IReadOnlyList<VNode>, long> _batchOrders = new();

    /// <summary>被本批待生回调共享的状态，全部任务结束后自然释放。</summary>
    private sealed class SpawnBatch
    {
        // 成员列表与包装只有一份；Order在暂时空批次后仍保持不变。
        internal readonly List<VNode> Members = new();
        internal readonly IReadOnlyList<VNode> View;
        internal readonly long Order;
        internal VNodeSpawnAttribute? Shared;
        // 仅路径批次保存出生偏移，延迟成员共享本批缓存。
        internal readonly IReadOnlyList<Vector2>? Positions;
        /// <summary>建立尚无实际成员的批次。</summary>
        /// <param name="order">Creator内部的固定建立顺序。</param>
        /// <param name="positions">相对批次原点的固定偏移，普通生成器为空。</param>
        internal SpawnBatch(long order, IReadOnlyList<Vector2>? positions)
        {
            Order = order;
            View = Members.AsReadOnly();
            Positions = positions;
        }
    }

    /// <summary>解析、校验并冻结一个出生、增量或随机属性组。</summary>
    /// <param name="element">当前格式JSON对象。</param>
    /// <param name="random">随机宽度必须非负，且不能包含时间。</param>
    /// <returns>只读属性组。</returns>
    private static VNodeSpawnAttribute ReadSpawn(JsonElement element, bool random)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("出生属性必须为对象。");
        if (random && element.TryGetProperty("SpawnDelayMs", out _)) throw new JsonException("随机组不支持SpawnDelayMs。");
        var spawn = Read<VNodeSpawnAttribute>(element);
        if (spawn.SpawnDelayMs < 0 || spawn.RefMoveQueue is null) throw new JsonException("延迟须非负，位移动作不可为null。");
        foreach (double value in new[] { spawn.Angle, spawn.Speed, spawn.AAngle, spawn.ASpeed })
            ValidateNumber(value, random);
        return spawn with
        {
            RefMoveQueue = element.TryGetProperty("RefMoveQueue", out var actions)
                ? ReadMoveQueue(actions, random) : Array.Empty<VNodeMoveActionAttribute>()
        };
    }

    /// <summary>读取三种共用位移动作，冻结列表并拒绝混用字段。</summary>
    /// <param name="element">不可为null的动作数组。</param>
    /// <param name="random">是否表示非负随机总宽度，路径端点队列使用false。</param>
    /// <returns>经过校验的只读动作队列。</returns>
    internal static IReadOnlyList<VNodeMoveActionAttribute> ReadMoveQueue(JsonElement element, bool random = false)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new JsonException("位移队列必须为数组且不可为null。");
        // 每个动作缺省数值为0，显式null仍视为无效配置。
        var actions = Read<VNodeMoveActionAttribute[]>(element);
        foreach (var action in actions)
        {
            if (action is null) throw new JsonException("位移动作不可为null。");
            if (action.Type == "PMove" && action.X is null && action.Y is null)
            { ValidateNumber(action.Angle ?? 0, random); ValidateNumber(action.Dist ?? 0, random); }
            else if (action.Type == "XYMove" && action.Angle is null && action.Dist is null)
            { ValidateNumber(action.X ?? 0, random); ValidateNumber(action.Y ?? 0, random); }
            else if (action.Type == "TarMove" && action.Angle is null)
            { ValidateNumber(action.X ?? 0, random); ValidateNumber(action.Y ?? 0, random); ValidateNumber(action.Dist ?? 0, random); }
            else throw new JsonException("位移Type与字段不匹配。");
        }
        foreach (var action in element.EnumerateArray())
            foreach (var field in action.EnumerateObject())
                if (field.Value.ValueKind == JsonValueKind.Null) throw new JsonException("位移字段不可显式为null。");
        return Array.AsReadOnly(actions);
    }
    /// <summary>验证有限值和随机宽度。</summary>
    /// <param name="value">属性值或总宽度。</param>
    /// <param name="random">是否为非负宽度。</param>
    private static void ValidateNumber(double value, bool random)
    {
        if (!double.IsFinite(value) || (random && value < 0)) throw new JsonException("属性值必须有限，随机宽度必须非负。");
    }

    /// <summary>按运动字段、位移动作顺序抽取一份偏移；同向加速度忽略AAngle。</summary>
    /// <param name="width">非负Center总宽度。</param>
    /// <returns>不含时间的已求值偏移。</returns>
    private VNodeSpawnAttribute Sample(VNodeSpawnAttribute width)
    {
        // 明确字段次序，JSON键顺序不影响业务随机。
        double angle = Random(width.Angle);
        double speed = Random(width.Speed);
        double accelerationAngle = Core.AAngleIsSameAsAngle ? 0 : Random(width.AAngle);
        double acceleration = Random(width.ASpeed);
        var actions = new VNodeMoveActionAttribute[width.RefMoveQueue.Count];
        for (int index = 0; index < actions.Length; index++)
        {
            var action = width.RefMoveQueue[index];
            actions[index] = action.Type == "PMove"
                ? new VNodeMoveActionAttribute { Type = "PMove", Angle = Random(action.Angle ?? 0), Dist = Random(action.Dist ?? 0) }
                : action.Type == "TarMove"
                    ? new VNodeMoveActionAttribute { Type = "TarMove", X = Random(action.X ?? 0), Y = Random(action.Y ?? 0), Dist = Random(action.Dist ?? 0) }
                    : new VNodeMoveActionAttribute { Type = "XYMove", X = Random(action.X ?? 0), Y = Random(action.Y ?? 0) };
        }
        return new VNodeSpawnAttribute
        {
            Angle = angle,
            Speed = speed,
            AAngle = accelerationAngle,
            ASpeed = acceleration,
            RefMoveQueue = actions
        };
    }

    /// <summary>从固定基础项求值并创建单个成员，不继承前一轮随机或运动状态。</summary>
    /// <param name="batch">本轮生成规则对应的唯一批次。</param>
    /// <param name="index">固定出生索引。</param>
    /// <param name="emitter">所属发射器。</param>
    /// <param name="manager">本场容量与碰撞管理器。</param>
    /// <param name="reference">用于Follow的实际参考对象。</param>
    /// <param name="parent">实际生命周期父节点。</param>
    /// <param name="snapshotOrigin">批次建立时保存的世界原点。</param>
    private void SpawnMember(SpawnBatch batch, int index, VBulletEmitter emitter, VBulletManager manager,
        Node2D? reference, VNode? parent, Vector2 snapshotOrigin)
    {
        if (!CanCreate(manager)) return;
        // 首次有容量才抽共享随机，后续每颗独立抽取。
        batch.Shared ??= Sample(RandDiffAttributes.Batch);
        var shared = batch.Shared;
        var individual = Sample(RandDiffAttributes.Member);
        int group = index / BaseAttributes.Count;
        var basis = BaseAttributes[index % BaseAttributes.Count];
        Vector2 source = Follow ? reference is VNode node ? node.WorldPosition : reference?.GlobalPosition ?? snapshotOrigin : snapshotOrigin;
        double aim = Core.AngleMode == "AimPlayer" ? VMath.GetAngleBetween2Points(source, GlobalEvent.GetPlayer().GlobalPosition) : 0;
        double angle = aim + Sum(basis.Angle, AddAttributes.Angle, group, shared.Angle, individual.Angle);
        var move = new VNodeMoveAttribute
        {
            Angle = angle,
            Speed = Sum(basis.Speed, AddAttributes.Speed, group, shared.Speed, individual.Speed),
            AAngle = Core.AAngleIsSameAsAngle ? angle : Sum(basis.AAngle, AddAttributes.AAngle, group, shared.AAngle, individual.AAngle),
            ASpeed = Sum(basis.ASpeed, AddAttributes.ASpeed, group, shared.ASpeed, individual.ASpeed)
        };
        // 派生路径先提供排列位置，再应用既有逐颗位移动作。
        Vector2 position = batch.Positions is null ? source : source + batch.Positions[index];
        if (!position.IsFinite()) throw new OverflowException("出生位置溢出。");
        for (int actionIndex = 0; actionIndex < basis.RefMoveQueue.Count; actionIndex++)
        {
            var action = basis.RefMoveQueue[actionIndex];
            var add = ActionAt(AddAttributes, actionIndex);
            var common = ActionAt(shared, actionIndex);
            var memberRandom = ActionAt(individual, actionIndex);
            double first = action.Type == "PMove"
                ? Sum(action.Angle ?? 0, add?.Angle ?? 0, group, common?.Angle ?? 0, memberRandom?.Angle ?? 0)
                : Sum(action.X ?? 0, add?.X ?? 0, group, common?.X ?? 0, memberRandom?.X ?? 0);
            double second = action.Type == "PMove"
                ? Sum(action.Dist ?? 0, add?.Dist ?? 0, group, common?.Dist ?? 0, memberRandom?.Dist ?? 0)
                : Sum(action.Y ?? 0, add?.Y ?? 0, group, common?.Y ?? 0, memberRandom?.Y ?? 0);
            // TarMove的前两项是世界目标坐标，第三项才是距离。
            if (action.Type == "TarMove")
            {
                double distance = Sum(action.Dist ?? 0, add?.Dist ?? 0, group, common?.Dist ?? 0, memberRandom?.Dist ?? 0);
                position = VMath.TargetMove(position, new Vector2((float)first, (float)second), distance);
            }
            else position = action.Type == "PMove" ? VMath.PolarMove(position, first, second)
                : new Vector2((float)(position.X + first), (float)(position.Y + second));
            if (!position.IsFinite()) throw new OverflowException("出生位置溢出。");
        }
        var member = CreateMember(emitter, manager, position, move);
        if (member is null) return;
        if (batch.Members.Count == 0)
        {
            // 迟出生和空批次恢复仍按批次建立序排列。
            int insertion = _batches.Count == 0 || _batchOrders[_batches[^1]] < batch.Order
                ? _batches.Count : _batches.FindIndex(view => _batchOrders[view] > batch.Order);
            if (insertion < 0) insertion = _batches.Count;
            _batches.Insert(insertion, batch.View);
            _batchOrders.Add(batch.View, batch.Order);
        }
        member.Creator = this;
        member.Batch = batch.Members;
        member.BatchView = batch.View;
        member.BirthIndex = index;
        batch.Members.Add(member);
        member.BindReference(reference, parent, Follow);
        foreach (var rule in MemberTimeline)
            rule.Register(member.Timeline!, index, () => member.ApplyParameters(rule.Set!));
        emitter.AttachChildren(member, this);
    }

    /// <summary>读取可省略的对应位置增量或随机偏移。</summary>
    /// <param name="attributes">求值属性组。</param>
    /// <param name="index">位置动作下标。</param>
    /// <returns>省略动作列表时返回null。</returns>
    private static VNodeMoveActionAttribute? ActionAt(VNodeSpawnAttribute attributes, int index)
        => attributes.RefMoveQueue.Count == 0 ? null : attributes.RefMoveQueue[index];

    /// <summary>计算基础、轮次增量及两类随机之和，并防止运行时溢出。</summary>
    /// <param name="basis">基础值。</param>
    /// <param name="add">逐轮增量。</param>
    /// <param name="group">零基轮次。</param>
    /// <param name="batchRandom">批次偏移。</param>
    /// <param name="memberRandom">独立偏移。</param>
    /// <returns>有限求值结果。</returns>
    private static double Sum(double basis, double add, int group, double batchRandom, double memberRandom)
    {
        double value = basis + group * add + batchRandom + memberRandom;
        return double.IsFinite(value) ? value : throw new OverflowException("生成属性溢出。");
    }
}
