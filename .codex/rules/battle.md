Timestamp: 2026-09-28T21:49:36+09:00

# 战斗领域规则

## 确定性、随机与数学

- 所有业务随机数必须通过 `Math/VMath.cs` 的 `VMath` 获取；禁止自行创建随机生成器或调用 Godot、.NET 等其他随机入口。
- 战斗随机种子统一在战斗初始化时设置，保持固定物理步和稳定的随机调用顺序；不得由渲染帧、动画显示或界面刷新消耗战斗随机序列。
- 修改随机算法、种子映射、抽样范围映射或随机调用顺序时，必须评估旧录像兼容性并同步固定序列验证；重现所需的完整初始状态见下述当前Creator树结构基线。
- 新结构、求值顺序与行为以当前Creator树结构为重现基线；不承诺旧Replay逐位兼容。验证须使用相同的完整初始战斗状态、种子、数据和固定输入，不能仅重置种子而保留上一轮受击无敌等状态。
- 后续涉及较复杂数学计算时，优先考虑在 `VMath` 添加通用函数，并在实施前提出相应方案。
- 项目内部方向、排列、运动和贴图旋转统一使用弧度：0向右、π/2向下，顺时针为正；外部度数通过 VMath 的 DegreesToRadians 转换，输出度数通过 RadiansToDegrees 转换。最终方向标准化到 [0,2π)，偏移和累计旋转量保留圈数。

## 固定步与时间线

- 战斗固定60Hz，由 BattleManager.StepFixed 按一步输入推进；内部每秒60000个整数时间单位，不读取系统时间，不由渲染或线程计时器推进战斗。
- Boss、Phase、Emitter、VNode（含VBullet）和PlayerController各自仅持有自身 VTimeline，不保存或注入 `VTimerProcessor`；玩家攻击、闪避及受伤保护使用玩家时间线，玩家组件按玩家年龄计算剩余时间。
- 接口时间为整数毫秒；秒配置统一用 VTimerProcessor.SecondsToMilliseconds 转换，中点远离零。
- 只有 `BattleManager` 推进正式战斗时钟；隔离调度器测试仍可自行创建和推进处理器。
- VTimeline.At和Repeat相对所有者激活时刻，After相对当前年龄，不能登记已过去的时间点。有限重复包含落在结束时刻上的动作；永久重复不设结束。
- 独立任务用 VTimelineAdapter(processor, targets?) 创建，由处理器自动计龄。传入目标时创建时固定、去重并过滤目标，适配器的 At/After/Repeat 回调收到当次存活快照；最后目标失效或全部动作自然完成时解除。逻辑死亡或注销须通知处理器。
- 每个60Hz步先完成玩家与Boss移动，再按Emitter登记顺序、Creator前序、批次生成序、成员出生序推进VNode/VBullet运动、年龄、寿命和碰撞，直接子弹单独推进。随后先派发控制时间线，再按树派发Emitter与成员本地时间线及直接子弹动作；本地所有者不进入全场候选排序。对象内部仍按到期时间和登记顺序补齐周期动作，跨对象按树顺序。新生对象只通过FIFO执行零龄动作，下步才运动；不递归调用，共用单次派发10000动作上限，取消及清场立即生效。
- 禁止回调重入推进逻辑时钟。
- 动画、闪烁、碰撞和阶段条件检查不包装成时间线动作；VNode与VBullet年龄和时间线使用同一实体年龄，寿命在运动后直接判断。
- 玩家允许零血及负血继续战斗，不得仅因HP小于等于0而取消玩家时间线。玩家自动攻击只登记一次200毫秒永久周期；Stop关闭射击，空回调随玩家时间线在战斗清理时结束；重新启动从至少等待200毫秒后的原周期格点恢复。

## 战斗服务与绑定

- `GlobalEvent` 是当前战斗的统一访问入口；业务代码通过无参数的 `GetBoss()`、`GetPlayer()`、`GetBulletManager()` 获取当前实体和容器，不提供旧计时器注册入口。无有效战斗时明确报错，不返回旧实体或隐式创建管理器。
- `BattleManager` 持有本场唯一的 `VBulletManager` 和 `VTimerProcessor`；处理器不再由弹幕容器创建。重开创建新的弹幕容器和处理器，胜利清理时间线与弹幕但保留实体供显示，离场清理并解绑。
- 不为各管理器分别维护可独立变化的静态 `Instance`。全局绑定仅由管理层维护；初始化和旧战斗清理使用所属战斗的临时绑定，场景替换失败恢复旧绑定，旧实例退出不得解除新战斗绑定。
- 先准备本场Boss、玩家和服务，再启动阶段与攻击逻辑，保证阶段初始化可查询双方实体。

## VNode 与 VBullet 运动模型

- VNode为无显示、无碰撞、不占子弹容量的移动点，VBullet继承VNode并增加显示、阵营、伤害和碰撞参数。公共运动、年龄、寿命及自身时间线直接放在VNode，不另建运动状态容器。
- VNode对外保存Angle/Speed/AAngle/ASpeed，内部实际速度独立累积；负Speed沿外部Angle反向起动，贴图跟随实际运动方向，零速保留最后朝向，出生零速采用外部Angle。同向加速度沿外部Angle施加；固定步先加速再移动。
- Velocity只读，SetDirection和SetSpeed按更新后的外部参数重建实际速度，加速度保留。VBullet集中校验贴图和完整出生参数，VBulletManager统一创建及登记；Emitter保留存活子弹只读视图。
- VNode默认Follow且速度0，VBullet默认Snapshot且速度180。Follow只继承参考对象平移，Snapshot在批次触发时固定一次父参考位置，全批共享；逻辑引用与Godot场景父子关系分离。参数动作改变父位置时，后代读取世界位置立即反映变化。

## 发射器、队列与生命周期

- VNodeCreator统一配置、子树、批次生成、随机和批量修改，VBulletCreator继承后增加子弹校验与管理器创建。每个Emitter仅持有一个Root，允许子弹根和子弹Creator拥有Children，现有内容原则上将子弹Creator放在末层。
- Creator持续持有Children和Batches；一次生成规则触发对应一个内层成员列表，包含全部轮次。公开批次仅保存存活成员、按建立序排列；内部待生任务可保留暂时空批次，恢复后仍归原批次。Members及VBulletCreator.Bullets是唯一批次来源的只读投影。批量参数操作作用于当前Creator全部批次，不隐式修改Children。实际对象保存Creator、具体ParentVNode和固定BirthIndex。Manager只通过树推进树内成员，不重复更新。
- 具体Emitter的`Build`可登记代码时间规则或调用UseDefinition绑定VBulletEmitter.Load加载的数据；已迁移发射器的生成频率只在JSON的Timeline定义；`Start`仅调用一次`Build`，未登记时间线动作则不发射，不提供绕过时间线的手动立即发射入口。
- Phase只绑定和停止Emitter，不保存发射频率。
- 后续修改具体Emitter时，若非必要，禁止新增内部类、辅助函数和类字段；发射相关处理只在`Build`及其局部变量和回调中实现。如果确实必须新增内部类、函数或类字段，先说明原因并取得用户确认后再添加。
- VNode寿命可省略，表示持续至父节点或Emitter结束；会多次生成的定义必须有有限寿命。节点到期取消后代VNode与未来生成，已发子弹继续存活；Follow子弹参考消失时保留当前世界位置并转为独立运动，不继承父速度。
- Emitter停止清理纯节点并真正取消未来生成动作。KeepBullets保留已发子弹及成员动作，Manager保留运行树直到遗留子弹耗尽；ClearBullets通过Manager清除所属子弹。任意父VNode/VBullet到期均结束纯节点后代、取消后代生成，但不清除已发子弹。父Creator没有成员时仍遍历Children；全场清理、重开、胜利和离场清理树、批次、时间线及索引。

## 数据协议

### 格式与引用

- JSON不保留Version，只解析当前结构；旧字段及未知、重复字段拒绝加载。最新规范为../BulletHell_Design/BulletData/数据化_最新版.md；旧版文档按数据化_vN归档，文档编号不参与运行。
- VBulletEmitter直接持有Core和单个Root，JSON字段为Core和单个VNodes对象，Children数组内嵌子Creator，不增加Definition中间层。Emitter.Core含可选Name、RefObject（Boss或null）、Team、Damage和StopMode；子弹出生时复制Team、Damage，VNode不持有。
- 删除旧队列构造及独立生成分支；保留通过Manager的统一单颗子弹创建入口。复用数据时Creator树、批次及查找索引独立。
- VNodeCreator与VBulletCreator直接持有Core、BaseAttributes列表、AddAttributes、RandDiffAttributes.Batch/Member、Timeline、MemberTimeline、Children和Batches；不再复制Creator表示一次生成。
- Creator.Core.Type必填VNode或VBullet，Name可选；小数字段允许加载时求值的PI/TAU表达式，整数和毫秒不接受表达式。子弹专用Core.Radius、Core.VisualScale及Display不能出现在VNode。内部Core.Id由树生成且不进入JSON：根VN001，每层追加从1开始的三位序号，每个Creator最多999个孩子。
- Emitter没有独立运动，RefObject=null以世界原点为空间参考，阶段仍管理生命周期。根Creator参考Emitter，子Creator绑定具体父VNode及其固定出生索引；不使用Id/Name建立关系，不查找最新批次。Follow只继承平移，Snapshot固定批次触发时的父参考位置。
- 同一Emitter内Id各自唯一、非空Name各自唯一，允许跨类别重名；未命名不参与名称索引。GetCreator(string idOrName)覆盖整棵树且优先Id、其次Name，精确区分大小写，空输入或无匹配返回null。运行逻辑使用直接对象引用。

### 时间规则与参数动作

- TimelineAttribute使用整数毫秒StartMs、IntervalMs、EndMs或AtMs数组，支持StartMsAdd、IntervalMsAdd、EndMsAdd按父出生索引派生；结束包含端点，重合时刻不去重。直接引用Emitter时索引0。检查派生时刻非负、周期正数和整数溢出。
- 生成规则登记在父Emitter或VNode时间线，不为Creator另建时钟。BaseAttributes为非空列表，Amount为轮数，TotalAmount为两者乘积；BirthIndex=g×列表长度+j。位置RefMoveQueue归基础、增量和随机组，Core.CreatePositionMode控制Follow/Snapshot。
- Base[j].SpawnDelayMs+g×Add.SpawnDelayMs为相对批次实际触发时刻的出生延迟，非负整数，不支持表达式或随机。实际出生才创建对象、占容量、计龄和登记成员/后代动作；延迟任务登记到实际父对象或Emitter时间线，完成及取消及时释放引用。
- LifeTimeMs在JSON、参数动作、运行对象及直接生成预设中统一为正整数毫秒，VNode可null；内部以整数时间单位判断寿命，Age仍为秒。Snapshot在批次建立时采样一次父位置，Follow每颗出生时独立读取并持续跟随。
- MemberTimeline按实际成员年龄及自身出生索引登记ParameterActionAttribute，仅修改运动、同向开关、坐标或总寿命。全部字段从同一旧状态求值后原子应用；Angle/Speed修改重建实际速度，仅修改加速度、位置或寿命不重建。
- AimPlayer在执行时从对象旧世界位置求值；Follow坐标为局部，Snapshot或脱离参考后为世界坐标；寿命缩短到已过去时在下一固定步检查释放。
- 稳定登记按Children声明顺序；每颗先登记成员动作，再登记子生成。每次实际生成新增独立批次，多个父实例同刻触发也不合并。批次与成员删除保持剩余顺序，不改写BirthIndex。

### 随机求值顺序

- 第g轮第j项=Base[j]+g×Add+BatchRandom+MemberRandom；只累积固定增量，不继承前一轮随机或运动状态。Batch在首次实际成功生成时抽一次，全批全部基础项与轮次共享；Member每颗独立抽样，包括首颗。
- 两组按Angle、Speed、有效AAngle、ASpeed、位置动作顺序求值；同向加速度忽略AAngle随机，零宽度不消耗序列，JSON键顺序无影响。随机宽度为非负Center总宽度，不支持时间字段。
- 满额跳过本颗、不补发、不抽样、不重排索引；批次开始时满额也保留后续延迟出生机会。VNode不占2048子弹容量。位置增量/随机动作数组省略视为零，提供时须匹配所有基础项的长度和Type。

## 当前内容基线：Boss_01

四个非空 B01 发射器均从 `Data/Emitters/` 加载：

| 阶段 | 生成及成员行为 |
|---|---|
| 阶段01 | 两个发射器每1000ms发射，第二个不延迟降速 |
| 阶段02 | 每1000ms生成6个Snapshot节点，寿命100ms，距离250加Center总宽100；各立即发12颗环 |
| 阶段03 | 首次1000ms，此后每2000ms生成6个斜线Snapshot节点，寿命1600ms；按父索引每200ms启动十二颗六边形（六顶点加六边中点），每颗间隔50ms，实际子弹出生2000ms后瞄准玩家并设置速度150 |
