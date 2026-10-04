using Godot;
using System;
using System.Collections.Generic;

/// <summary>从独立配置生成控制器和全新阶段，供挑战与重开共用。</summary>
public static class BossFactory
{
    // 阶段组合工厂，必须每次返回新的有状态阶段实例。
    private static readonly Dictionary<string, Func<IEnumerable<BossPhase>>> Profiles = new(StringComparer.Ordinal)
    {
        ["Boss_01"] = () => new BossPhase[] { new B01_Phase01(), new B01_Phase02(), new B01_Phase03() },
        ["Boss_02"] = () => new BossPhase[] { new B02_Phase01() },
        ["Boss_03"] = () => new BossPhase[] { new B03_Phase01(), new B03_Phase02(), new B03_Phase03() },
        ["Boss_AIGen_01"] = () => new BossPhase[] { new B04_Phase01(), new B04_Phase02(), new B04_Phase03() },
        ["Boss_AIGen_02"] = () => new BossPhase[] { new B05_Phase01(), new B05_Phase02(), new B05_Phase03() },
        ["Boss_AIGen_03"] = () => new BossPhase[] { new B06_Phase01(), new B06_Phase02(), new B06_Phase03() },
        ["Boss_AIGen_04"] = () => new BossPhase[] { new B07_Phase01(), new B07_Phase02(), new B07_Phase03() },
        ["Boss_AIGen_05"] = () => new BossPhase[] { new B08_Phase01(), new B08_Phase02(), new B08_Phase03() },
        ["Boss_AIGen_06"] = () => new BossPhase[] { new B09_Phase01(), new B09_Phase02(), new B09_Phase03() },
        ["Boss_AIGen_07"] = () => new BossPhase[] { new B10_Phase01(), new B10_Phase02(), new B10_Phase03() },
        ["Boss_AIGen_08"] = () => new BossPhase[] { new B11_Phase01(), new B11_Phase02(), new B11_Phase03() },
        ["Boss_AIGen_09"] = () => new BossPhase[] { new B12_Phase01(), new B12_Phase02(), new B12_Phase03() },
        ["Boss_AIGen_10"] = () => new BossPhase[] { new B13_Phase01(), new B13_Phase02(), new B13_Phase03() },
        ["Boss_AIGen_11"] = () => new BossPhase[] { new B14_Phase01(), new B14_Phase02(), new B14_Phase03() },
        ["Boss_AIGen_12"] = () => new BossPhase[] { new B15_Phase01(), new B15_Phase02(), new B15_Phase03() },
        ["Boss_AIGen_13"] = () => new BossPhase[] { new B16_Phase01(), new B16_Phase02(), new B16_Phase03() },
        ["Boss_AIGen_14"] = () => new BossPhase[] { new B17_Phase01(), new B17_Phase02(), new B17_Phase03() },
        ["Boss_AIGen_15"] = () => new BossPhase[] { new B18_Phase01(), new B18_Phase02(), new B18_Phase03() },
        ["Boss_AIGen_16"] = () => new BossPhase[] { new B19_Phase01(), new B19_Phase02(), new B19_Phase03() },
        ["Boss_AIGen_17"] = () => new BossPhase[] { new B20_Phase01(), new B20_Phase02(), new B20_Phase03() },
        ["Boss_AIGen_18"] = () => new BossPhase[] { new B21_Phase01(), new B21_Phase02(), new B21_Phase03() },
        ["Boss_AIGen_19"] = () => new BossPhase[] { new B22_Phase01(), new B22_Phase02(), new B22_Phase03() },
        ["Boss_AIGen_20"] = () => new BossPhase[] { new B23_Phase01(), new B23_Phase02(), new B23_Phase03() }
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
    /// <returns>可直接加入战场的新控制器。</returns>
    public static BossController Create(BossData data)
    {
        data.Validate();
        if (data.Phases.Count == 0) ValidateProfile(data.PhaseProfile);
        // 构造前获取阶段列表，让阶段工厂失败时不产生孤立节点。
        var phases = new List<BossPhase>();
        if (data.Phases.Count == 0) phases.AddRange(Profiles[data.PhaseProfile]());
        else for (int index = 0; index < data.Phases.Count; index++) phases.Add(new DataBossPhase(data.Phases[index], index, data.EmitterLoader));
        if (phases.Count == 0 || phases.Exists(phase => phase is null)) throw new ArgumentException("阶段组合不可为空。");
        var boss = new BossController { Name = "Boss", Position = data.SpawnPosition };
        boss.Configure(data);
        boss.Initialize(data.VisualScale);
        boss.SetPhases(phases);
        return boss;
    }
}
