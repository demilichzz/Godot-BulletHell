using Godot;
using System;
using System.Diagnostics;
using System.Linq;

/// <summary>运行集合与出生路径的定向成本测量；不把微基准结果当作游戏帧率。</summary>
public partial class BattleVerification
{
    /// <summary>测量成员视图、停止树派发和实际子弹创建，不推进战斗时钟或改变正式配置。</summary>
    private void MeasureRuntimeCosts()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        try
        {
            // 本地派发必须经过处理器的派发阶段；零单位不改变所有者年龄。
            Action dispatch = battle.Bullets.DispatchTimelines;
            Action flush = () => battle.Timers.AdvanceByUnits(0, dispatchLocal: dispatch);
            // 单批2000颗静止子弹覆盖容量内的大集合，生成仍经正式零龄时间线。
            var emitter = CostEmitter(2000);
            emitter.Start(battle.Boss, battle.Bullets);
            battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
            var members = emitter.Root.Members;
            long checksum = 0;
            MeasureCost("members-foreach-2000", 100, () =>
            {
                checksum = 0;
                foreach (var member in members) checksum += member.BirthIndex;
            });
            Check(checksum == 1999000, "成员枚举保留出生序列");
            MeasureCost("members-indexed-2000", 4, () =>
            {
                checksum = 0;
                for (int index = 0; index < members.Count; index++) checksum += members[index].BirthIndex;
            });
            Check(checksum == 1999000, "索引遍历访问相同成员");
            emitter.Stop();
            MeasureCost("dispatch-stopped-1x2000", 100, flush);
            battle.Bullets.Clear();

            // 多棵停止树均保留子弹，迫使清理检查处理前、中、后部所属对象。
            var emitters = Enumerable.Range(0, 16).Select(_ => CostEmitter(125)).ToArray();
            foreach (var item in emitters) item.Start(battle.Boss, battle.Bullets);
            battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
            foreach (var item in emitters) item.Stop();
            Check(battle.Bullets.ActiveCount == 2000, "停止树测量保留2000颗实际子弹");
            MeasureCost("dispatch-stopped-16x125", 100, flush);
            battle.Bullets.Clear();

            // 贴图校验、重复缓存加载和完整创建分别测量，避免把节点创建成本误归因给资源查询。
            var sprite = VBulletDefaultSet.Get(VBulletType.ScaleSet);
            MeasureCost("validate-sprite", 2000, () => VBullet.Validate(sprite));
            MeasureCost("cached-texture-load", 2000, () => GD.Load<Texture2D>(sprite.TexturePath!));
            MeasureCost("spawn-sprite", 1000, () => SpawnAndFree(sprite));
            MeasureCost("spawn-circle", 1000, () => SpawnAndFree(VBulletDefaultSet.Get(VBulletType.PlayerSet)));

            /// <summary>使用正式登记和注销入口，立即释放，避免延迟释放队列污染下一轮测量。</summary>
            /// <param name="settings">已定义的完整出生参数。</param>
            void SpawnAndFree(VBulletDefaultSet settings)
            {
                var bullet = battle.Bullets.Spawn(settings)!;
                // 正常出生队列也要消耗，不让待派发引用累积影响测量。
                flush();
                battle.Bullets.Release(bullet, false);
                bullet.Free();
            }
        }
        finally { world.Free(); }
    }

    /// <summary>验证停止树清理仍包含直接关联子弹，保持其他发射器顺序、活动空树及重开清理。</summary>
    private void VerifyRuntimeHotPaths()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        try
        {
            // 空树不生成成员；两个子弹树用于验证清理不会跨Emitter或跳过后续对象。
            var empty = VBulletEmitter.FromJson(EmitterJson("""{"Core":{"Type":"VNode"},"BaseAttributes":[{}]}"""));
            var first = CostEmitter(2); var second = CostEmitter(2);
            empty.Start(battle.Boss, battle.Bullets); first.Start(battle.Boss, battle.Bullets); second.Start(battle.Boss, battle.Bullets);
            var settings = VBulletDefaultSet.Get(VBulletType.PlayerSet);
            var unowned = battle.Bullets.Spawn(settings)!;
            Flush();
            // 存活视图必须一直引用同一权威来源，不因测量优化变成一次性快照。
            var firstView = first.Bullets; var memberView = first.Root.Members; var secondView = second.Bullets;
            var firstTree = firstView.ToArray(); var secondTree = secondView.ToArray();
            var direct = battle.Bullets.Spawn(settings, first)!;
            Check(direct.Creator is null && firstView.SequenceEqual(firstTree.Append(direct)), "Emitter视图包含无Creator的直接关联子弹");
            empty.Stop(); first.Stop(); second.Stop(); Flush();
            Check(empty.GetCreator("VN001") is null, "空的停止树及时退役");
            Check(first.GetCreator("Cost") is not null && second.GetCreator("Cost") is not null, "保留子弹的停止树仍可查找");
            battle.Bullets.Release(firstTree[0]); Flush();
            Check(firstView.SequenceEqual(new[] { firstTree[1], direct }) && memberView.Count == 1, "释放更新既有视图并保留成员顺序");
            battle.Bullets.Release(firstTree[1]); Flush();
            Check(memberView.Count == 0 && first.GetCreator("Cost") is not null && firstView.Single() == direct,
                "Creator批次已空但存在直接关联子弹时不能提前退役");
            battle.Bullets.Release(direct); Flush();
            Check(first.GetCreator("Cost") is null && firstView.Count == 0, "最后一颗直接关联子弹注销后退役");
            Check(secondView.SequenceEqual(secondTree) && battle.Bullets.ActiveBullets.SequenceEqual(new[] { unowned }.Concat(secondTree)),
                "清理保持其他Emitter与直接子弹的登记顺序");
            battle.Bullets.ClearEmitter(second); Flush();
            Check(second.GetCreator("Cost") is null && secondView.Count == 0 && battle.Bullets.ActiveBullets.Single() == unowned,
                "清除一个Emitter不清除其他直接子弹");

            // 活动空树仍可持有未来生成规则，必须等Stop后再退役。
            var active = CostEmitter(1); active.Start(battle.Boss, battle.Bullets); Flush();
            battle.Bullets.ClearEmitter(active); Flush();
            Check(active.GetCreator("Cost") is not null && active.Timeline is not null, "活动空树不因暂时无子弹退役");
            active.Stop(); Flush();
            Check(active.GetCreator("Cost") is null, "停止后的活动空树及时退役");
            var retained = CostEmitter(1); retained.Start(battle.Boss, battle.Bullets); Flush(); retained.Stop();
            var retainedView = retained.Bullets;
            battle.Restart();
            Check(retainedView.Count == 0 && retained.GetCreator("Cost") is null, "重开清除旧管理器所属视图及停止树索引");

            // 满额也要先检查非法资源；减少重复加载不能变成跳过每次出生参数校验。
            battle.Boss.Stop();
            var full = CostEmitter(BattleConfig.MaxBullets); full.Start(battle.Boss, battle.Bullets); Flush();
            Check(battle.Bullets.ActiveCount == BattleConfig.MaxBullets, "满额校验使用实际容量");
            bool rejected = false;
            try { battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.ScaleSet) with { TexturePath = "res://Assets/missing-runtime-cost.png" }); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && battle.Bullets.ActiveCount == BattleConfig.MaxBullets, "满额前仍拒绝无效资源且不留下节点");

            /// <summary>进入合法本地派发阶段而不推进战斗时间，重开后使用新的处理器。</summary>
            void Flush() => battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        }
        finally { world.Free(); }
    }

    /// <summary>建立只在零龄发射一次的静止子弹树，用于独立测量和生命周期验证。</summary>
    /// <param name="amount">单批正整数成员数，总数不超过战斗容量。</param>
    /// <returns>尚未启动且停止时保留子弹的发射器。</returns>
    private static VBulletEmitter CostEmitter(int amount) => VBulletEmitter.FromJson(EmitterJson($$"""
        {"Core":{"Type":"VBullet","Amount":{{amount}},"LifeTimeMs":100000,"Name":"Cost"},
         "Display":{},"BaseAttributes":[{"Speed":0}],"Timeline":[{"StartMs":0}]}
        """));

    /// <summary>预热后独立采样五轮，输出中位操作耗时与线程托管分配，不设依赖硬件的通过阈值。</summary>
    /// <param name="name">稳定测量名称。</param>
    /// <param name="iterations">每轮调用次数，为正整数。</param>
    /// <param name="operation">不含结果输出的被测操作。</param>
    private static void MeasureCost(string name, int iterations, Action operation)
    {
        // 少量预热用于初始化与JIT；采样统计本身不进入被测循环。
        for (int index = 0; index < Math.Min(iterations, 16); index++) operation();
        var times = new double[5];
        var bytes = new double[5];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long begin = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++) operation();
            times[sample] = Stopwatch.GetElapsedTime(begin).TotalMilliseconds * 1000 / iterations;
            bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
        }
        Array.Sort(times); Array.Sort(bytes);
        GD.Print(FormattableString.Invariant($"COST {name} median_us={times[2]:F3} min_us={times[0]:F3} bytes={bytes[2]:F1} iterations={iterations} samples=5"));
    }
}
