using System;

/// <summary>编辑器专项入口与截图选项的严格选择，不因参数错误静默扩大测试范围。</summary>
public partial class EditorVerification
{
    // 合法组按显示顺序声明；截图选项在参数通过校验后冻结，本轮各验证共用。
    private static readonly string[] GroupNames = { "--targeted-emitter", "--targeted-boss", "--targeted-test-selection", "--targeted-layout-cache" };
    private bool _capture;

    /// <summary>选择唯一测试组；无组参数保持原Emitter默认范围，截图不扩大组范围。</summary>
    /// <param name="arguments">Godot分隔符后的用户参数，区分大小写。</param>
    /// <returns>明确组名与是否截图；列出组和参数自测不能附带截图选项。</returns>
    private static (string Group, bool Capture) SelectVerificationGroup(string[] arguments)
    {
        // 在执行任何测试、创建编辑器或写入临时文件前完成全部参数检查。
        string? selected = null;
        bool capture = false;
        foreach (string argument in arguments)
        {
            if (argument == "--capture")
            {
                if (capture) throw new ArgumentException("截图参数--capture不可重复。");
                capture = true;
                continue;
            }
            if (argument != "--list-groups" && Array.IndexOf(GroupNames, argument) < 0)
                throw new ArgumentException("未知编辑器验证参数：" + argument);
            if (selected is not null) throw new ArgumentException("每次仅选择一个编辑器验证组；多个组须分别执行。");
            selected = argument;
        }
        selected ??= "--targeted-emitter";
        if (capture && selected is not ("--targeted-emitter" or "--targeted-boss"))
            throw new ArgumentException("--capture仅供Emitter或Boss编辑器验证组使用。");
        return (selected, capture);
    }

    /// <summary>只验证入口选择与非法参数，不启动其他组或创建截图。</summary>
    private void VerifyGroupSelection()
    {
        Check(SelectVerificationGroup(Array.Empty<string>()) == ("--targeted-emitter", false), "无参数保留原Emitter默认组");
        Check(SelectVerificationGroup(new[] { "--capture" }) == ("--targeted-emitter", true), "旧截图入口保留默认组");
        Check(SelectVerificationGroup(new[] { "--list-groups" }) == ("--list-groups", false), "列表入口不执行其他组");
        // 每个声明组均能独立选择，允许截图的两个组不受参数顺序影响。
        foreach (string group in GroupNames)
        {
            Check(SelectVerificationGroup(new[] { group }) == (group, false), "独立选择编辑器组：" + group);
            if (group is not ("--targeted-emitter" or "--targeted-boss")) continue;
            Check(SelectVerificationGroup(new[] { group, "--capture" }) == (group, true), "组后截图选项：" + group);
            Check(SelectVerificationGroup(new[] { "--capture", group }) == (group, true), "组前截图选项：" + group);
        }
        // 拼错、大小写、重复、冲突及无效截图归属都必须抛出参数异常。
        string[][] invalid =
        {
            new[] { "--targeted-bos" }, new[] { "--Targeted-boss" }, new[] { "--full-regression" }, new[] { "extra.json" },
            new[] { "--capture", "--capture" }, new[] { "--targeted-boss", "--targeted-emitter" },
            new[] { "--targeted-boss", "--targeted-boss" }, new[] { "--targeted-emitter", "--list-groups" },
            new[] { "--list-groups", "--list-groups" }, new[] { "--targeted-boss", "--capture", "--unknown" },
            new[] { "--targeted-layout-cache", "--capture" }, new[] { "--list-groups", "--capture" }, new[] { "--capture", "--targeted-test-selection" }
        };
        foreach (var arguments in invalid)
        {
            try { SelectVerificationGroup(arguments); }
            catch (ArgumentException) { Check(true, "非法编辑器参数在执行前被拒绝"); continue; }
            throw new Exception("未拒绝编辑器参数：" + string.Join(" ", arguments));
        }
    }
}
