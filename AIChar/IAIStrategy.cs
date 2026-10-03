using System.Collections.Generic;

/// <summary>可替换的AI决策接口，每个角色持有独立实例，仅由固定逻辑步调用。</summary>
public interface IAIStrategy
{
    /// <summary>根据当前局部信息产生操作，不修改弹幕、不推进时钟。</summary>
    /// <param name="state">当前角色状态，位置为世界逻辑像素。</param>
    /// <param name="nearby">本步附近敌方弹幕只读视图，仅在本次调用期间读取。</param>
    /// <param name="random">所属角色的独立随机流，不得使用默认战斗随机入口。</param>
    /// <returns>当前逻辑步的移动、闪避与攻击意图。</returns>
    AIIntent Decide(in AICharState state, IReadOnlyList<VBullet> nearby, VRandomStream random);
}