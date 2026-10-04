using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>集中声明战斗专项组，拒绝拼错参数或同时选择多个组。</summary>
public partial class BattleVerification
{
    /// <summary>建立明确的专项入口；默认组保持原覆盖范围，不代表完整回归。</summary>
    /// <param name="arguments">Godot分隔符后的用户参数。</param>
    /// <returns>参数名到验证动作的映射。</returns>
    private Dictionary<string, Action> VerificationGroups(string[] arguments) => new(StringComparer.Ordinal)
    {
        ["--targeted-copy"] = VerifyCopies,
        ["--targeted-regions"] = VerifyRegions,
        ["--targeted-boss03"] = VerifyBoss03,
        ["--targeted-player-combat"] = () => { VerifyPlayer(); VerifyCombat(); VerifyNegativePlayerHealth(); },
        ["--targeted-laser"] = VerifyLasers,
        ["--targeted-path"] = VerifyPaths,
        ["--targeted-spawn"] = VerifySpawnLists,
        ["--targeted-doc-json"] = () => VerifyJsonExamples(arguments),
        ["--targeted-vnode"] = () => { VerifyVNodeModel(); VerifyVNodeSchedules(); VerifyVNodeLifecycle(); VerifyVNodeValidation(); VerifyVNodeReplay(); },
        ["--targeted-data"] = () => { VerifyBulletData(); VerifyOriginal(); VerifyBatches(); VerifyDefaultSets(); },
        ["--targeted-sprites"] = VerifySpriteSets,
        ["--targeted-bullet"] = () => { VerifyOriginal(); VerifyBatches(); VerifyDefaultSets(); },
        ["--targeted-phase-switch"] = VerifyPhaseSwitch,
        ["--targeted-global-services"] = VerifyGlobalServices,
        ["--targeted-b01"] = () => { VerifyBoss01Sequence(); VerifyBoss01Stages(); VerifyPhaseSwitch(); },
        ["--targeted-test-selection"] = VerifyGroupSelection,
        ["--targeted-migration"] = VerifyMigrationBaseline,
        ["--targeted-catalog"] = VerifyBossCatalog,
        ["--default-battle"] = () =>
        {
            VerifyOriginal(); VerifyPlayer(); VerifyCombat(); VerifyPhases(); VerifyBoss01Stages();
            VerifyNegativePlayerHealth(); VerifyBatches(); VerifyDefaultSets(); VerifyInput();
        }
    };

    /// <summary>解析唯一验证组；未知参数、重复选择及无所属示例路径立即失败。</summary>
    /// <param name="arguments">用户参数；无参数选择原默认战斗组。</param>
    /// <param name="groups">已注册的组名。</param>
    /// <returns>组名，或仅列出入口的--list-groups。</returns>
    private static string SelectVerificationGroup(string[] arguments, IEnumerable<string> groups)
    {
        // 参数集只用于身份检查，不决定测试执行顺序。
        var known = new HashSet<string>(groups, StringComparer.Ordinal);
        string? selected = null;
        int examples = 0;
        foreach (string argument in arguments)
        {
            if (argument.StartsWith("--json-example=", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(argument[15..])) throw new ArgumentException("JSON示例路径不可为空。");
                examples++;
                continue;
            }
            if (argument != "--list-groups" && !known.Contains(argument)) throw new ArgumentException("未知验证参数：" + argument);
            if (selected is not null) throw new ArgumentException("每次仅选择一个验证组；多个组须分别执行。");
            selected = argument;
        }
        selected ??= "--default-battle";
        if ((selected == "--targeted-doc-json") != (examples > 0))
            throw new ArgumentException("--json-example仅供--targeted-doc-json使用，且至少提供一个路径。");
        return selected;
    }

    /// <summary>逐份校验明确指定的Emitter示例，不枚举其他目录。</summary>
    /// <param name="arguments">已验证归属且包含至少一个示例路径的参数。</param>
    private void VerifyJsonExamples(string[] arguments)
    {
        // 各示例保留用户声明顺序。
        foreach (string argument in arguments.Where(value => value.StartsWith("--json-example=", StringComparison.Ordinal)))
        {
            VBulletEmitter.FromJson(System.IO.File.ReadAllText(argument[15..]), argument[15..]);
            Check(true, "JSON示例通过：" + argument[15..]);
        }
    }

    /// <summary>验证测试参数不会静默扩大范围或忽略冲突。</summary>
    private void VerifyGroupSelection()
    {
        // 只解析参数，不执行被选中的其他组。
        var names = VerificationGroups(Array.Empty<string>()).Keys;
        Check(SelectVerificationGroup(Array.Empty<string>(), names) == "--default-battle", "无参数保留原默认组");
        Check(SelectVerificationGroup(new[] { "--targeted-b01" }, names) == "--targeted-b01", "准确选择专项");
        Check(SelectVerificationGroup(new[] { "--list-groups" }, names) == "--list-groups", "列表入口不执行测试");
        Check(SelectVerificationGroup(new[] { "--json-example=a.json", "--targeted-doc-json" }, names) == "--targeted-doc-json", "示例参数顺序无关");
        // 覆盖拼错、重复、冲突、空路径与多余的示例参数。
        string[][] invalid = {
            new[] { "--targeted-typo" }, new[] { "--targeted-b01", "--targeted-copy" },
            new[] { "--targeted-b01", "--targeted-b01" }, new[] { "--list-groups", "--targeted-b01" },
            new[] { "--targeted-doc-json" }, new[] { "--json-example=a.json" },
            new[] { "--targeted-doc-json", "--json-example=" }, new[] { "a.json" }
        };
        foreach (var arguments in invalid)
        {
            try { SelectVerificationGroup(arguments, names); }
            catch (ArgumentException) { Check(true, "拒绝非法验证参数"); continue; }
            throw new Exception("未拒绝参数：" + string.Join(" ", arguments));
        }
    }
}
