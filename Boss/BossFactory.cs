using Godot;
using System;
using System.Collections.Generic;

/// <summary>从独立配置生成控制器和全新阶段，供挑战与重开共用。</summary>
public static class BossFactory
{
    // 阶段组合工厂，必须每次返回新的有状态阶段实例。
    private static readonly Dictionary<string, Func<IEnumerable<BossPhase>>> Profiles = new(StringComparer.Ordinal)
    {
        ["Boss_01"] = () => new BossPhase[] { new B01_Phase01(), new B01_Phase02(), new B01_Phase03() }
    };
    /// <summary>注册可被 BossData 引用的阶段组合。</summary>
    /// <param name="key">非空且唯一的阶段组合标识。</param>
    /// <param name="create">每次调用返回新阶段实例的工厂。</param>
    public static void RegisterProfile(string key, Func<IEnumerable<BossPhase>> create)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("阶段组合标识不可为空。", nameof(key));
        Profiles.Add(key, create ?? throw new ArgumentNullException(nameof(create)));
    }
    /// <summary>检查阶段组合是否已注册。</summary>
    /// <param name="key">配置引用的阶段组合标识。</param>
    public static void ValidateProfile(string key)
    {
        if (!Profiles.ContainsKey(key)) throw new ArgumentException($"未注册的阶段组合：{key}");
    }
    /// <summary>创建尚未入树的 Boss。</summary>
    /// <param name="data">独立 Boss 配置。</param>
    /// <param name="bullets">战斗独立弹幕容器。</param>
    /// <returns>可直接加入战场的新控制器。</returns>
    public static BossController Create(BossData data, BulletManager bullets)
    {
        data.Validate();
        ValidateProfile(data.PhaseProfile);
        // 构造前获取阶段列表，让阶段工厂失败时不产生孤立节点。
        var phases = new List<BossPhase>(Profiles[data.PhaseProfile]());
        if (phases.Count == 0 || phases.Exists(phase => phase is null)) throw new ArgumentException("阶段组合不可为空。");
        var boss = new Boss { Name = "Boss", Position = data.SpawnPosition };
        boss.Configure(data);
        boss.Initialize(bullets, data.VisualScale);
        boss.SetPhases(phases);
        return boss;
    }
}
