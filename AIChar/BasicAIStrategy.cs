using Godot;
using System;
using System.Collections.Generic;

/// <summary>小范围威胁检测策略：原地短时安全则停留，有威胁才侧移，持续安全后缓慢返回中下方。</summary>
public sealed class BasicAIStrategy : IAIStrategy
{
    // 只按当前速度外推120毫秒，不读取未来时间线、加速度变化或反射事件。
    private const double LookAheadSeconds = 0.12;
    // 普通弹安全余量与激光进入、离开余量，单位为逻辑像素。
    private const double BulletMargin = 3, EnterMargin = 8, ExitMargin = 12;
    // 驻留区域按1280×800基准得到X480..800、Y520..700；中心在区域内即可停留。
    private static readonly IRegionShape RestRegion = new RectangleRegionShape(new Rect2(
        BattleConfig.Bounds.Position + BattleConfig.Bounds.Size * new Vector2(0.375f, 0.65f),
        BattleConfig.Bounds.Size * new Vector2(0.25f, 0.225f)));
    // 连续60个固定步没有原地威胁后才允许半速回位。
    private const int QuietStepsBeforeReturn = 60;
    private const float ReturnInputScale = 0.5f;
    // 迟滞只属于当前激光；方向记忆只属于连续避让，不继承回位方向。
    private VLaser? _escaping;
    private Vector2 _evadeDirection;
    private int _quietSteps;

    /// <summary>先判断停留安全，再比较主威胁两侧，基础策略不主动攻击或闪避。</summary>
    /// <param name="state">世界逻辑像素位置、像素每秒速率及固定步秒数。</param>
    /// <param name="nearby">感知框粗筛结果；在框内不等于必须避让。</param>
    /// <param name="random">只在避让方向最终并列时使用的独立随机流。</param>
    /// <returns>长度不超过1的移动输入，右下为正。</returns>
    public AIIntent Decide(in AICharState state, IReadOnlyList<VBullet> nearby, VRandomStream random)
    {
        // 普通弹取最早接近者，激光取当前危险带内最近中心线，等值保持登记顺序。
        VBullet? threat = null;
        VLaser? danger = null;
        double earliest = double.PositiveInfinity, nearestDistance = double.PositiveInfinity;
        Vector2 nearest = Vector2.Zero, tangent = Vector2.Zero;
        // 初次扫描同时保存原地风险，普通弹不再为停留候选重复做预测。
        var stay = new Risk { Clearance = double.PositiveInfinity, EndClearance = double.PositiveInfinity };
        bool hasLasers = false;
        for (int index = 0; index < nearby.Count; index++)
        {
            // 独立调用也过滤失效、待出生及非敌方对象。
            var bullet = nearby[index];
            if (!IsThreatObject(bullet)) continue;
            if (bullet is VLaser laser)
            {
                if (!laser.TryGetNearestHazard(state.Position, out var point, out var along)) continue;
                hasLasers = true;
                // 激光预警和展开采用正式宽度，离开阈值略宽以避免贴线往返。
                double distance = state.Position.DistanceSquaredTo(point);
                double limit = laser.Settings.HitWidth * 0.5 + state.Radius
                    + (ReferenceEquals(laser, _escaping) ? ExitMargin : EnterMargin);
                if (distance > limit * limit || distance >= nearestDistance) continue;
                nearestDistance = distance;
                danger = laser;
                nearest = point;
                tangent = along;
            }
            else
            {
                // 100像素仅用于上游候选筛选；擦边、远离及安全静止弹不会触发移动。
                double clearance = PredictBullet(in state, bullet, Vector2.Zero, out double time, out double endClearance);
                if (clearance <= 1) stay.Hits++;
                stay.Clearance = Math.Min(stay.Clearance, clearance);
                stay.EndClearance = Math.Min(stay.EndClearance, endClearance);
                if (clearance > 1 || time >= earliest) continue;
                earliest = time;
                threat = bullet;
            }
        }
        _escaping = danger;
        if (danger is null && threat is null)
        {
            _evadeDirection = Vector2.Zero;
            return RestOrReturn(in state, nearby);
        }
        _quietSteps = 0;
        // 激光偏离中心线时只允许向外；中心线上比较两个法线，普通弹比较来向两侧。
        Vector2 first, second;
        if (danger is not null)
        {
            first = state.Position - nearest;
            if (first.IsZeroApprox())
            {
                first = new Vector2(-tangent.Y, tangent.X);
                second = -first;
            }
            else { first = first.Normalized(); second = Vector2.Zero; }
        }
        else
        {
            // 零速重叠弹沿径向退出；完全同心时用固定轴提供两侧候选。
            var velocity = threat!.Velocity;
            if (!velocity.IsZeroApprox()) first = new Vector2(-velocity.Y, velocity.X).Normalized();
            else
            {
                first = state.Position - threat.WorldPosition;
                first = first.IsZeroApprox() ? Vector2.Right : first.Normalized();
            }
            second = -first;
        }
        if (hasLasers)
        {
            // 主激光确定后补上激光风险，不再次计算普通弹轨迹。
            var laserRisk = Evaluate(in state, nearby, Vector2.Zero, danger, lasersOnly: true);
            stay.Hits += laserRisk.Hits;
            stay.Clearance = Math.Min(stay.Clearance, laserRisk.Clearance);
            stay.EndClearance = Math.Min(stay.EndClearance, laserRisk.EndClearance);
        }
        _evadeDirection = ChooseEscape(in state, nearby, random, first, second, danger, in stay);
        return new AIIntent { Movement = _evadeDirection };
    }

    /// <summary>连续安全满一秒后走向驻留矩形最近点，途中有危险则停下，不搜索绕路。</summary>
    /// <param name="state">当前角色状态，位置单位为逻辑像素。</param>
    /// <param name="nearby">粗筛后包含安全普通弹的局部弹幕。</param>
    /// <returns>至多半速的移动输入，区域内或路径不安全时为零。</returns>
    private AIIntent RestOrReturn(in AICharState state, IReadOnlyList<VBullet> nearby)
    {
        _quietSteps = Math.Min(_quietSteps + 1, QuietStepsBeforeReturn);
        if (_quietSteps < QuietStepsBeforeReturn || RestRegion.Contains(state.Position)) return default;
        // 只需到达区域边缘；限制末步距离防止在边界两侧反复移动。
        var toward = RestRegion.Clamp(state.Position) - state.Position;
        double fullStep = state.MoveSpeed * state.StepSeconds;
        if (fullStep <= 0 || toward.LengthSquared() < 0.0001f) return default;
        var movement = toward.Normalized() * (float)Math.Min(ReturnInputScale, toward.Length() / fullStep);
        if (!CanMove(in state, movement)) return default;
        // 与避让使用同一短时风险检查，静止障碍和预警激光都能阻止主动回位。
        if (Evaluate(in state, nearby, movement, null).Hits != 0) return default;
        return new AIIntent { Movement = movement };
    }

    /// <summary>只比较两个侧移候选及停留，不因为弹幕数量变化继续游走。</summary>
    /// <param name="state">当前角色状态。</param>
    /// <param name="nearby">参与候选路径安全判断的全部局部弹幕。</param>
    /// <param name="random">最终平局使用的角色独立随机流。</param>
    /// <param name="first">第一个单位避让方向。</param>
    /// <param name="second">第二个单位方向；零表示禁止反向穿过激光。</param>
    /// <param name="laser">正在逃离的主激光，可为空。</param>
    /// <param name="stay">已在初次威胁扫描中计算的停留风险。</param>
    /// <returns>风险较低的合法移动输入；不能改善时停留。</returns>
    private Vector2 ChooseEscape(in AICharState state, IReadOnlyList<VBullet> nearby, VRandomStream random,
        Vector2 first, Vector2 second, VLaser? laser, in Risk stay)
    {
        // 停留作为保底；正在逃离的光束只比较终点离线程度，其余激光检查整个移动走廊。
        var best = stay;
        var chosen = Vector2.Zero;
        for (int index = 0; index < 2; index++)
        {
            // 两个固定候选无需建立方向数组或排序。
            var direction = index == 0 ? first : second;
            if (direction.IsZeroApprox() || !CanMove(in state, direction)) continue;
            var risk = Evaluate(in state, nearby, direction, laser);
            int comparison = Compare(in risk, in best);
            if (comparison < 0) { chosen = direction; best = risk; continue; }
            if (comparison != 0 || chosen.IsZeroApprox()) continue;
            // 优先保持连续避让方向；停留或回位不会留下此方向记忆。
            float previous = _evadeDirection.Dot(chosen), current = _evadeDirection.Dot(direction);
            if (current > previous || (current == previous && random.GetRandomInt(0, 1) == 0))
                chosen = direction;
        }
        return chosen;
    }

    /// <summary>计算相对匀速轨迹的最近距离，返回相对安全判定圆的距离平方比。</summary>
    /// <param name="state">角色当前状态。</param>
    /// <param name="bullet">普通弹，仅使用当前自身实际速度，像素每秒。</param>
    /// <param name="movement">待评估角色输入，右下为正。</param>
    /// <param name="time">返回120毫秒内最近接近的秒数。</param>
    /// <param name="endClearance">返回预测终点的距离平方比，用于已重叠时选择退出方向。</param>
    /// <returns>小于等于1表示重叠或即将进入碰撞半径加3像素余量。</returns>
    private static double PredictBullet(in AICharState state, VBullet bullet, Vector2 movement,
        out double time, out double endClearance)
    {
        // 使用相对速度，防止只判断目的地而主动穿过横向移动弹幕。
        var relative = bullet.WorldPosition - state.Position;
        var velocity = bullet.Velocity - movement * state.MoveSpeed;
        double speedSquared = velocity.LengthSquared();
        time = speedSquared == 0 ? 0 : Math.Clamp(-relative.Dot(velocity) / speedSquared, 0, LookAheadSeconds);
        // 最近点就在当前时刻表示没有继续接近：安全静止或远离弹只按实际碰撞圆判断。
        double radius = bullet.Radius + state.Radius + (time > 0 ? BulletMargin : 0);
        var closest = relative + velocity * (float)time;
        var end = relative + velocity * (float)LookAheadSeconds;
        endClearance = end.LengthSquared() / (radius * radius);
        return closest.LengthSquared() / (radius * radius);
    }

    /// <summary>评估一个短时移动候选；普通弹用相对轨迹，激光按当前正式几何检查。</summary>
    /// <param name="state">当前角色状态。</param>
    /// <param name="nearby">粗筛后的全部敌方弹幕。</param>
    /// <param name="movement">长度不超过1的输入，右下为正。</param>
    /// <param name="escaping">主逃离光束，允许离开其初始重叠区。</param>
    /// <param name="lasersOnly">默认false；补算原地风险时跳过已计算的普通弹。</param>
    /// <returns>预计危险对象数及最近距离平方比。</returns>
    private static Risk Evaluate(in AICharState state, IReadOnlyList<VBullet> nearby, Vector2 movement, VLaser? escaping,
        bool lasersOnly = false)
    {
        // 预测终点仅用于候选评分，每次实际仍只移动一个60Hz步。
        var end = state.Position + movement * (float)(state.MoveSpeed * LookAheadSeconds);
        var padding = Vector2.One * (float)(state.Radius + EnterMargin);
        var lower = new Vector2(Math.Min(state.Position.X, end.X), Math.Min(state.Position.Y, end.Y)) - padding;
        var upper = new Vector2(Math.Max(state.Position.X, end.X), Math.Max(state.Position.Y, end.Y)) + padding;
        var corridor = new Rect2(lower, upper - lower);
        var risk = new Risk { Clearance = double.PositiveInfinity, EndClearance = double.PositiveInfinity };
        for (int index = 0; index < nearby.Count; index++)
        {
            // 多光束都参与候选安全检查，但不会组合搜索逃生路径。
            var bullet = nearby[index];
            if (!IsThreatObject(bullet) || (lasersOnly && bullet is not VLaser)) continue;
            double clearance, endClearance;
            bool hit;
            if (bullet is VLaser laser)
            {
                if (!laser.TryGetNearestHazard(end, out var nearest, out _)) continue;
                double radius = laser.Settings.HitWidth * 0.5 + state.Radius + EnterMargin;
                endClearance = end.DistanceSquaredTo(nearest) / (radius * radius);
                clearance = endClearance;
                hit = ReferenceEquals(laser, escaping) ? endClearance <= 1 : laser.IntersectsRect(corridor);
                // 保守包围盒命中也计为风险，不能因终点安全而穿过其他光束。
                if (hit) clearance = Math.Min(clearance, 1);
            }
            else
            {
                clearance = PredictBullet(in state, bullet, movement, out _, out endClearance);
                hit = clearance <= 1;
            }
            if (hit) risk.Hits++;
            risk.Clearance = Math.Min(risk.Clearance, clearance);
            risk.EndClearance = Math.Min(risk.EndClearance, endClearance);
        }
        return risk;
    }

    /// <summary>先比较危险数量，再比较最近距离；初始重叠时用终点距离打破平局。</summary>
    /// <param name="first">待比较风险。</param>
    /// <param name="second">当前最佳风险。</param>
    /// <returns>负数表示第一项更安全，零为完全并列。</returns>
    private static int Compare(in Risk first, in Risk second)
    {
        // 使用固定比较次序，不依赖容器排序或随机阈值。
        int result = first.Hits.CompareTo(second.Hits);
        if (result == 0) result = second.Clearance.CompareTo(first.Clearance);
        if (result == 0) result = second.EndClearance.CompareTo(first.EndClearance);
        return result;
    }

    /// <summary>检查当前步和短时预测终点均位于活动区域，避免选择即将撞边的侧移。</summary>
    /// <param name="state">角色位置、半径、步长及像素每秒速率。</param>
    /// <param name="movement">长度不超过1的输入，右下为正。</param>
    /// <returns>两个位置的判定圆均由形状接口完整包含时为真。</returns>
    private static bool CanMove(in AICharState state, Vector2 movement)
    {
        // 使用共用区域接口，不依赖具体矩形或圆形公式。
        var step = state.Position + movement * (float)(state.MoveSpeed * state.StepSeconds);
        var end = state.Position + movement * (float)(state.MoveSpeed * LookAheadSeconds);
        return BattleConfig.GameRegion.Contains(step, state.Radius) && BattleConfig.GameRegion.Contains(end, state.Radius);
    }

    /// <summary>排除已经失效、尚未出生和玩家阵营的对象。</summary>
    /// <param name="bullet">待检查的运行弹幕。</param>
    /// <returns>有效敌方对象时为真。</returns>
    private static bool IsThreatObject(VBullet bullet) => bullet.IsAlive && !bullet.PendingBirth && bullet.Team == VBulletTeam.Enemy;

    /// <summary>值类型候选评分，避免每步分配临时对象。</summary>
    private struct Risk
    {
        // 危险对象数量、轨迹最小安全距离比与终点最小安全距离比。
        public int Hits;
        public double Clearance;
        public double EndClearance;
    }
}