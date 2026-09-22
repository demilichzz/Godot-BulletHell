# 项目 Agent 协作约定

## 核心原则

- 本项目以战斗确定性和 Replay 可重现性为核心技术约束。
- 战斗逻辑固定 60Hz 推进；业务随机统一由 `VMath` 管理。
- 不得擅自改变用户已经确认的业务规则。
- 只委派当前任务实际需要的 Agent，不因为存在多个 Agent 就强制经过全部流程。
- 具体领域规则按任务需要由子 Agent 从 `.codex/rules/` 读取，Main Agent 不需要预加载全部实现细则。

## Agent 路由

### Analyst

业务逻辑、游戏机制、架构、类职责、数据流、Replay/Timer/Random 设计存在讨论或歧义时，优先委派 Analyst。

Analyst 只分析和形成实现规格，不修改项目文件。
当用户明确处于讨论、分析或设计阶段时，不得提前进入代码实现。

### Developer

需求和业务规则已经明确，需要新增、修改、重构或修复生产代码时，委派 Developer。

Developer 按已确认规格实现，不得自行改变业务行为。实现中发现重要歧义或需要改变核心设计时，应返回 Main Agent，而不是自行决定。

### Tester

生产代码修改后，如需要验证实现，委派 Tester。

Tester 负责测试与验证，原则上不修改生产代码。发现生产代码问题时，应报告给 Main Agent，由 Developer 修复。

## 标准流程

需要先设计再实现：

`User → Main → Analyst → Main → Developer → Tester → Main → User`

已有明确规格：

`User → Main → Developer → Tester → Main → User`

纯讨论：

`User → Main → Analyst → Main → User`

测试失败：

`Tester → Main → Developer → Tester`

以上只是标准路径。简单任务只使用必要的 Agent。

## 测试范围总原则

默认采用最小相关测试（Targeted Testing）。

代码修改后只测试本次修改直接影响的功能、类型和行为。共享基础设施修改可以根据实际依赖扩大为 Expanded Targeted Testing，但仍不自动执行完整回归测试。

“测试一下”“验证修改”“完成后测试”“检查是否正常”“确保没有问题”等普通要求均不代表完整回归测试。

只有用户明确要求“完整测试”“完整回归测试”“全部测试”“Full Regression”或具有同等明确含义的指示时，才执行完整回归测试。

Tester 无权仅根据自己的风险判断将 Targeted Testing 自行升级为 Full Regression。
