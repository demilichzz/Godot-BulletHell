Timestamp: 2026-09-26T23:53:56+09:00

# 战斗领域规则

## 确定性、随机与数学

- 所有业务随机数必须通过 `Math/VMath.cs` 的 `VMath` 获取；禁止自行创建随机生成器或调用 Godot、.NET 等其他随机入口。
- 战斗随机种子统一在战斗初始化时设置，保持固定物理步和稳定的随机调用顺序；不得由渲染帧、动画显示或界面刷新消耗战斗随机序列。
- 修改随机算法、种子映射、抽样范围映射或随机调用顺序时，必须评估旧录像兼容性并同步固定序列验证；重现所需的完整初始状态见下述格式2基线。
- 新结构、求值顺序与行为以格式2为重现基线；不承诺旧Replay逐位兼容。验证须使用相同的完整初始战斗状态、种子、数据和固定输入，不能仅重置种子而保留上一轮受击无敌等状态。
- 后续涉及较复杂数学计算时，优先考虑在 `VMath` 添加通用函数，并在实施前提出相应方案。
- 项目内部方向、排列、运动和贴图旋转统一使用弧度：0向右、π/2向下，顺时针为正；外部度数通过 VMath 的 DegreesToRadians 转换，输出度数通过 RadiansToDegrees 转换。最终方向标准化到 [0,2π)，偏移和累计旋转量保留圈数。

## 固定步与时间线

- 战斗固定60Hz，由 BattleManager.StepFixed 按一步输入推进；内部每秒60000个整数时间单位，不读取系统时间，不由渲染或线程计时器推进战斗。
- Boss、Phase、Emitter、VNode（含Bullet）和PlayerController各自仅持有自身 VTimeline，不保存或注入 `VTimerProcessor`；玩家攻击、闪避及受伤保护使用玩家时间线，玩家组件按玩家年龄计算剩余时间。
- 接口时间为整数毫秒；秒配置统一用 VTimerProcessor.SecondsToMilliseconds 转换，中点远离零。
- 只有 `BattleManager` 推进正式战斗时钟；隔离调度器测试仍可自行创建和推进处理器。
- VTimeline.At和Repeat相对所有者激活时刻，After相对当前年龄，不能登记已过去的时间点。有限重复包含落在结束时刻上的动作；永久重复不设结束。
- 独立任务用 VTimelineAdapter(processor, targets?) 创建，由处理器自动计龄。传入目标时创建时固定、去重并过滤目标，适配器的 At/After/Repeat 回调收到当次存活快照；最后目标失效或全部动作自然完成时解除。逻辑死亡或注销须通知处理器。
- 每个实体在自身逻辑更新时按整数单位累计年龄；每个60Hz步依次完成玩家与Boss移动、父先子后的VNode运动与到期、Bullet运动碰撞及释放，再将所有到期时间线动作在步末执行。同一步先按原定到期时间、再按全场注册顺序；周期动作补齐本步内到期次数，新生节点与子弹下步才运动。回调新增动作不递归调用，取消立即生效。
- 禁止回调重入推进逻辑时钟。
- 动画、闪烁、碰撞和阶段条件检查不包装成时间线动作；VNode与Bullet年龄和时间线使用同一实体年龄，寿命在运动后直接判断。
- 玩家允许零血及负血继续战斗，不得仅因HP小于等于0而取消玩家时间线。玩家自动攻击只登记一次200毫秒永久周期；Stop关闭射击，空回调随玩家时间线在战斗清理时结束；重新启动从至少等待200毫秒后的原周期格点恢复。

## 战斗服务与绑定

- `GlobalEvent` 是当前战斗的统一访问入口；业务代码通过无参数的 `GetBoss()`、`GetPlayer()`、`GetBulletManager()` 获取当前实体和容器，不提供旧计时器注册入口。无有效战斗时明确报错，不返回旧实体或隐式创建管理器。
- `BattleManager` 持有本场唯一的 `BulletManager` 和 `VTimerProcessor`；处理器不再由弹幕容器创建。重开创建新的弹幕容器和处理器，胜利清理时间线与弹幕但保留实体供显示，离场清理并解绑。
- 不为各管理器分别维护可独立变化的静态 `Instance`。全局绑定仅由管理层维护；初始化和旧战斗清理使用所属战斗的临时绑定，场景替换失败恢复旧绑定，旧实例退出不得解除新战斗绑定。
- 先准备本场Boss、玩家和服务，再启动阶段与攻击逻辑，保证阶段初始化可查询双方实体。

## VNode 与 Bullet 运动模型

- VNode为无显示、无碰撞、不占子弹容量的移动点，Bullet继承VNode并增加显示、阵营、伤害和碰撞参数。公共运动、年龄、寿命及自身时间线直接放在VNode，不另建运动状态容器。
- VNode对外保存Angle/Speed/AAngle/ASpeed，内部实际速度独立累积；负Speed沿外部Angle反向起动，贴图跟随实际运动方向，零速保留最后朝向，出生零速采用外部Angle。同向加速度沿外部Angle施加；固定步先加速再移动。
- Velocity只读，SetDirection和SetSpeed按更新后的外部参数重建实际速度，加速度保留。Bullet集中校验贴图和完整出生参数，BulletManager统一创建及登记；Emitter保留存活子弹只读视图。
- VNode默认Follow且速度0，Bullet默认Snapshot且速度180。Follow只继承参考对象平移，Snapshot只固定出生时参考位置；逻辑引用与Godot场景父子关系分离。参数动作改变父位置时，后代读取世界位置立即反映变化。

## 发射器、队列与生命周期

- VNodeQueue统一配置、生成、随机和批量修改，BulletQueue继承后增加子弹校验与管理器创建。
- Members使用唯一的IReadOnlyList<VNode>成员来源，BulletList仅为其兼容视图，不另存成员；队列仅跟踪成功生成且仍存活的成员；子弹由BulletManager逐颗推进和碰撞，队列批量Set操作不额外推进运动。出生后的行为登记在实际生成子弹的时间线上，子弹注销时立即取消。
- 具体Emitter的`Build`可登记代码时间规则或调用UseDefinition绑定BulletEmitter.Load加载的数据；已迁移发射器的生成频率只在JSON的Timeline定义；`Start`仅调用一次`Build`，未登记时间线动作则不发射，不提供绕过时间线的手动立即发射入口。
- Phase只绑定和停止Emitter，不保存发射频率。
- 后续修改具体Emitter时，若非必要，禁止新增内部类、辅助函数和类字段；发射相关处理只在`Build`及其局部变量和回调中实现。如果确实必须新增内部类、函数或类字段，先说明原因并取得用户确认后再添加。
- VNode寿命可省略，表示持续至父节点或Emitter结束；会多次生成的定义必须有有限寿命。节点到期取消后代VNode与未来生成，已发子弹继续存活；Follow子弹参考消失时保留当前世界位置并转为独立运动，不继承父速度。
- Emitter停止总会清理节点树与未来生成，KeepBullets保留已发子弹及其成员动作，ClearBullets仅在Emitter停止时通过BulletManager按归属清除子弹。单个VNode到期不清弹；重开、胜利和离场继续遵循全场清理。

## 数据协议

### 格式与引用

- 当前JSON格式为Core.Version=2，文档为../BulletHell_Design/BulletData/数据化_v3.md，v2文档保留为历史记录。旧格式1明确报告不支持；未知字段、重复字段拒绝加载，不能静默忽略旧队列Team、Damage。
- BulletEmitter直接持有Core、VNodes、BulletQueues；数组内嵌队列定义，不增加Definition中间层。Core含Id、Version、RefObject（Boss或null）、Team、Damage和StopMode；数据子弹出生时复制Team、Damage，VNode不持有。
- 旧C#完整出生参数入口保持兼容。
- VNodeQueue与BulletQueue直接持有Core、BaseAttributes、AddAttributes、AddAttributesRandDiff、PositionAttributes、Timeline和MemberTimeline。
- Id、Version在Core；子弹额外保留Core.Radius、Core.VisualScale和Display，VNode不接受这些专用字段。小数允许加载时求值的PI/TAU四则表达式，整数及毫秒不接受表达式。
- Emitter没有独立运动。其RefObject=null表示以世界原点作空间参考，阶段仍控制其生命周期。队列RefObject可指Emitter或本Emitter的VNodeQueue Id；引用队列展开为每个实际父实例独立启动的子任务，绑定实际父实例及固定批内出生索引，不查找最新批次。
- 加载时拒绝重复Id、缺失引用、循环或以BulletQueue为父。

### 时间规则与参数动作

- TimelineAttribute使用整数毫秒StartMs、IntervalMs、EndMs或AtMs数组，支持StartMsAdd、IntervalMsAdd、EndMsAdd按父出生索引派生；结束包含端点，重合时刻不去重。直接引用Emitter时索引0。检查派生时刻非负、周期正数和整数溢出。
- 生成规则登记在父Emitter或VNode时间线，不为队列另建时钟。
- MemberTimeline按实际成员年龄及自身出生索引登记ParameterActionAttribute，仅修改运动、同向开关、坐标或总寿命。全部字段从同一旧状态求值后原子应用；Angle/Speed修改重建实际速度，仅修改加速度、位置或寿命不重建。
- AimPlayer在执行时从对象旧世界位置求值；Follow坐标为局部，Snapshot或脱离参考后为世界坐标；寿命缩短到已过去时在下一固定步检查释放。
- 稳定登记使用VNodes数组在前、BulletQueues数组在后，各保留声明顺序；每颗先登记成员动作，再登记子生成。到期派发遵循统一调度顺序。每次触发创建独立批次，属性冻结共享，存活成员相互独立。

### 随机求值顺序

- 运动随机与位置RandDiffAdd均覆盖首颗；位置RandDiff每批共享一次。子弹满额不抽样，有容量才计算共享随机与逐颗随机；VNode不受2048上限影响，照常求值、生成和运动。
- 先按位移动作及标量顺序求共享随机，再逐颗按Angle、Speed、有效AAngle、ASpeed及位移动作求独立随机；同向AAngle不独立抽样，零宽度不消耗随机。字段求值不依赖JSON键顺序，未迁移C#代码随机责任保持不变。

## 当前内容基线：Boss_01

四个非空 B01 发射器均从 `Data/Emitters/` 加载：

| 阶段 | 生成及成员行为 |
|---|---|
| 阶段01 | 两个发射器每1000ms发射，第二个不延迟降速 |
| 阶段02 | 每1000ms生成6个Snapshot节点，寿命0.1秒，距离250加Center总宽100；各立即发12颗环 |
| 阶段03 | 首次1000ms，此后每2000ms生成6个斜线Snapshot节点，寿命1.2秒；按父索引每200ms依次发射，实际子弹出生2000ms后瞄准玩家并设置速度150 |
