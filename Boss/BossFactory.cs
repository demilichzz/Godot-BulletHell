using System;
using System.Collections.Generic;

/// <summary>从已校验的数据创建独立Boss和有序阶段，供正式战斗及预览共用。</summary>
public static class BossFactory
{
    /// <summary>创建尚未入树的Boss，每个阶段持有本场独立运行状态。</summary>
    /// <param name="data">包含非空阶段队列的Boss配置。</param>
    /// <returns>可加入战场的新控制器。</returns>
    public static BossController Create(BossData data)
    {
        data.Validate();
        if (data.Phases.Count == 0) throw new ArgumentException("Boss必须包含数据阶段。", nameof(data));
        // 先建立阶段，失败时不产生孤立Godot节点。
        var phases = new List<BossPhase>();
        for (int index = 0; index < data.Phases.Count; index++)
            phases.Add(new BossPhase(data.Phases[index], index, data.EmitterLoader));
        var boss = new BossController { Name = "Boss", Position = data.SpawnPosition };
        boss.Configure(data);
        boss.Initialize(data.VisualScale);
        boss.SetPhases(phases);
        return boss;
    }
}
