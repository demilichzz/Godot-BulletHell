# BulletHell

Godot 4.7.2 .NET / C# 2D Boss 战原型。启动后进入Boss选择场景，可选择并进入通用战斗场景；战斗支持移动、闪避、自动攻击、伤害、Boss胜利结算和重开。

## 运行与操作

使用 `.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe` 导入 `project.godot`，按 F5。需要 .NET 8 或更高版本 SDK。

- Boss选择界面：鼠标点击只改变选中项，方向键按四列网格导航，空格进入战斗。边缘不环绕，末行缺项时向下选择最后一项。
- 战斗中：WASD 或方向键移动，斜向速度归一化；Q 切换到上一阶段，E 切换到下一阶段。
- Esc：从战斗返回Boss选择，保留上次选择。
- 空格：沿输入方向闪避；静止时沿最近一次移动方向，初始向上。按住不会重复闪避。
- 自动瞄准 Boss 射击，无需攻击键。
- 击败Boss后按 R：重建战斗；Q/E 仅在战斗运行中生效，首尾阶段不会切换。

左上角显示双方 HP、闪避冷却、战斗时间和结果。玩家以蓝色几何图形表示，白色中心为判定区域，闪避变为金色，受击后闪烁。

## 参数与保留行为

场地、角色及全场弹幕容量参数位于 `Core/BattleConfig.cs`；弹幕默认参数集位于 `Bullet/BulletDefaultSet.cs`；当前Boss的独立配置位于 `Data/Bosses/Boss_01.tres`。默认逻辑分辨率与启动窗口为1280×800（16:10），窗口可调整大小；通过canvas_items与keep策略整体等比缩放，比例不同时居中并显示纯黑边，窗口尺寸不影响逻辑坐标与移动速度；位置和半径使用像素，速度使用像素/秒，运动与原配置时间使用秒，VTimeline接口使用整数毫秒。内部角度统一使用弧度，0向右、π/2向下，顺时针为正。

| 对象 | 参数 |
|---|---|
| Boss | 位置(640,250)，300 HP，碰撞半径32，图片倍率3 |
| 敌弹 | 每阶段绑定2个发射器，首发在进入阶段后1秒；阶段01每秒24+16发，阶段02每秒在6个随机圆心各发12颗，阶段03每2秒启动6圈、每圈12颗；默认速度180，寿命4秒，半径6，伤害1，颜色索引0，图片倍率3；阶段01发射器02覆盖速度300、颜色索引3 |
| 玩家 | 出生(640,600)，3 HP，速度240，半径5，中心限制在圆心(640,400)、半径395的圆内，保证判定圆完整位于直径800的活动区域 |
| 闪避 | 速度720，持续0.15秒且无敌，触发起冷却1秒 |
| 受击 | 无敌1秒，无敌时敌弹穿过，不销毁 |
| 玩家弹 | 每0.2秒单发，首发在0.2秒，速度600，寿命2秒，半径3，伤害1 |
| 容量 | 活动弹幕最多2048颗，满额跳过新弹并输出调试提示 |

Boss进入战斗后先静止，第5秒首次选点，此后每5秒在圆心(640,250)、半径200的圆周上选取随机目标，以100像素/秒直线移动，到达后停止等待下一目标；移动中继续环形射击。`Assets/Units/Boss_01.png`为128×128透明像素图集，按左上、右上、左下、右下排列四张64×64帧，战斗以4 FPS循环挥臂（每帧0.25秒），结束冻结、重开复位；选择界面仅显示首帧；`Assets/Sprites/Sprite_scale.png`为160×16的十色图集，每格16×16，颜色从左至右为0～9。Boss与敌弹实际默认倍率均为3（旧README中的2已纠正）。贴图居中且最近邻过滤，弹幕按发射角度旋转，外观缩放不改变移动或碰撞。

没有Boss接触伤害。敌弹只伤玩家，玩家弹只伤Boss；有效命中销毁。弹幕不因出屏提前释放。Boss生命归零后清理全部弹幕并停止模拟。玩家生命可降至0及负数，仍可移动、闪避、自动攻击和继续受伤，保留正常受击无敌，不触发死亡注销或失败结算；即使玩家为负血，击败Boss仍正常胜利。

背景使用从旧项目复制的 `Assets/UI/UI_400x600_gamearea_4.png`（400×600）。图片以最近邻过滤、等比覆盖并居中裁切铺满1280×800逻辑画面，上下超出部分裁去；背景位于角色和弹幕下方，不接收鼠标输入，也不覆盖窗口黑边。HUD使用深色描边保持可读性。活动区域绘制浅蓝色圆形边界，Boss绘制橙红色碰撞轮廓；普通移动与闪避共用径向约束。

## 模块与扩展

- `Core`：BattleManager通过StepFixed接收一个60Hz逻辑步的移动、闪避及可选Q/E阶段切换输入，先推进玩家、Boss、弹幕与碰撞，再在步末派发到期事件；GlobalEvent提供当前战斗实体访问入口；BattleConfig保存场地、角色及全场容量参数。
- `Player`：PlayerController协调PlayerMovement、PlayerDodge、PlayerHealth、PlayerAttack。生命变化使用C#事件，零血及负血不触发玩家死亡。
- `Boss`：BossController管理HP与阶段；BossPhase定义Enter、Advance、ShouldEnd、GetInitialHp和Exit。通过入树前SetPhases配置有序阶段；Boss_01使用B01_Phase01、B01_Phase02、B01_Phase03；每损失100点生命立即进入下一阶段（300血时为剩余200、100两条血线），最后阶段持续至Boss被击败。战斗运行中Q/E可切换相邻阶段，阶段初始HP按目标阶段能力设置，位置、动画、玩家和旧子弹保留，首尾阶段无效；单次伤害跨多个阈值时同刻顺序切换。
- `Bullet`：BulletEmitter是阶段绑定的可重复发射实例，自行定义首发与周期；BulletQueue按数量、位置、角度和速度增量逐颗登记子弹。BulletManager独占运动推进、连续碰撞、容量与释放，并同步注销发射器和队列引用；BulletBehavior负责生成后的运动。
- `Bullet/Bullet.cs`统一校验并配置完整参数和贴图；BulletManager.Spawn(BulletDefaultSet)负责创建与登记。子弹禁止自行物理更新。Velocity只读，通过SetDirection和SetSpeed修改运动；负速度沿原角度反向移动，贴图朝向不反转。外部逻辑可通过Emitter.Bullets只读视图选择本批次活动子弹。
- `BossController`是唯一Boss运行类，持有跨阶段时间线。三个阶段各绑定两个独立发射器：阶段01每秒生成24颗普通环形弹和16颗瞄准环形弹，后者初速300、颜色索引3，出生两秒后降速100；阶段02每秒在六个随机圆心生成各12颗弹；阶段03每两秒启动一组六圈弹幕，首圈立即生成，后五圈每200毫秒生成，子弹出生两秒后追踪玩家。阶段退出停止移动和未来发射，已发子弹继续飞行；重开和离场统一清场。
- `Main.cs`装配场景、默认InputMap及简单中文HUD，显示当前阶段并提示Q/E切换。默认绑定同时兼容物理键和辅助输入设备的逻辑键码；已有同名输入动作不会被覆盖。

碰撞使用子弹相对目标运动线段与双方半径之和判定，包含高速穿越与静止重叠。每个固定步先运动、碰撞并注销失效目标，再按原定时间和登记顺序执行本步到期动作；伤害计时从检测步末开始。闪避结束影响后续逻辑步，Boss归零时结算胜利，玩家零血及负血不结束战斗。项目显式固定60Hz。固定种子与同样固定步输入已验证可重现当前战斗过程；尚未实现录像存储、输入回放或跨版本兼容机制。

### 枚举默认参数集

`BulletType.ScaleSet`保留当前敌弹参数；`DotSet`、`DropSet`、`StarSet`分别使用圆形、水滴、星形贴图，除TexturePath外所有参数与ScaleSet一致。四张十色图集位于`Assets/Sprites/`，其中水滴和星形的每个16×16区块已顺时针旋转90°，颜色索引顺序不变。`BulletType.PlayerSet`保留当前玩家弹参数；新增枚举成员统一使用`XXXSet`命名。`BulletDefaultSet.Get(type)`返回不可修改的公共预设，贴图以资源路径保存。参数包括位置、角度、运动行为、贴图路径、图集行列数、按行排列的零基贴图索引、速度、寿命、碰撞半径、阵营、伤害、贴图倍率、贴图/圆点显示方式及圆点颜色；发射间隔由Emitter或PlayerAttack持有。

`BulletDefaultSet`为不可变sealed record，使用`BulletDefaultSet.Get(type) with { ... }`取得参数副本并覆盖本次发射的字段。位置和角度属于settings，由Emitter的生成逻辑逐颗通过with设置；未知枚举立即抛错。生成前统一校验参数范围、贴图类型及图集索引，避免产生无效节点。

弹幕配置的角度、速度、寿命、碰撞半径和贴图倍率，以及`BulletQueueSet`的各项增量、`SetDirection`和`SetSpeed`，统一使用`double`。`VMath.GetAngleBetween2Points`和`getB2PAngle`返回已标准化到`[0,2π)`的双精度方向，发射器可直接赋给`AngleRadians`。`DegreesToRadians`与`RadiansToDegrees`仅做单位换算，保留旋转量的符号和圈数。Godot的`Vector2`、`Sprite2D.Rotation`、缩放和圆点绘制仍使用单精度；代码在这些边界检查范围后转换，不能表示的配置会在生成前报错。改动后的数值精度可能使旧版本录像的逐步状态不同。

```csharp
// 以下代码位于具体发射器的Build(manager, owner)内。
var settings = BulletDefaultSet.Get(BulletType.ScaleSet) with { Speed = 240 };
Timeline!.Repeat(1000, 1000, null, () =>
{
    AddBullet(manager, settings with { Position = owner.GlobalPosition });
});
```

Boss_01的发射器在`Build(manager, owner)`内定义模板和`Timeline.At`、`After`或`Repeat`动作；阶段绑定时`Start`只调用一次`Build`，未登记时间线动作的发射器不会发射。周期、首发时间和弹幕参数都由该发射器定义，Phase只负责绑定。回调可用`AddBullet`生成单颗子弹，或用`AddQueue`按增量生成多颗；玩家单发攻击由`BulletManager.Spawn`处理，攻击间隔由玩家时间线上的200毫秒周期管理。运动行为对象不做深拷贝；有状态行为须为每颗子弹单独创建。圆点按碰撞半径绘制，VisualScale仅用于贴图显示。

### 弹幕队列

`BulletQueue(source, first, settings)`以source为第一颗的全局位置；`settings`使用不可变`BulletQueueSet`，可用`with`派生`Amount`、`XAdd`、`YAdd`、`AngleAdd`、`SpeedAdd`，按零基索引逐颗叠加增量。例如`new BulletQueueSet { Amount = 24, AngleAdd = Math.Tau / 24 }`生成不重复终点的圆环；角度正增量顺时针，负增量逆时针。Queue仅保存成功生成且仍存活的成员，提供与Bullet一致的SetDirection、SetSpeed批量操作；子弹仍由BulletManager逐颗推进。不能用固定位置增量表达的圆周位置可在Emitter的Build回调中逐颗计算。

Emitter只保存仍有效的子弹，命中、过期和清场时同步移除。需要删除子弹的外部操作不可直接Free节点；清场统一调用管理器。若操作会改变列表，应避免在foreach中增删同一列表。

### 随机数与圆周移动

`Math/VMath.cs`是所有业务随机数的唯一入口。`randomSeed`只读；`setRandomSeed(int seed = 0)`重置固定SplitMix64序列，负种子按无符号32位位模式扩展。`getRandomInt(min,max)`支持完整int闭区间，使用拒绝采样避免取模偏差；`getRandomDouble(min,max)`用53位样本生成包含两端的小数。相等端点直接返回、不消耗随机序列；非法范围和非有限小数端点抛错。

BattleManager的BattleRandomSeed常量默认为0，每次进入战斗及R重开均在创建对象前重置。共享处理器由BattleManager持有并在固定步末派发到期动作；渲染帧和UI不抽取战斗随机数。

未来录像仍需保存一致的初始状态、种子与逐物理步输入，保持随机调用顺序及算法版本；较复杂的通用数学计算优先提出扩展VMath的方案。

## 验证

```powershell
dotnet build
& '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --scene res://Tests/BattleVerification.tscn
```

也可通过原`.tools/verify.gd`入口运行同一验证场景。验证包含原弹幕时机、角度、图集、速度和寿命，圆心、圆内点、四轴与斜向边界、闪避越界、脚本迁移路径、背景布局、分辨率配置、闪避冷却、无敌防重复伤害，自动攻击与高速碰撞，阶段生命周期、血线交接、玩家负血、冻结、连续重开和容量上限。新增验证还覆盖一次性批次、双列表同一对象、外部批次转向、容量部分接收、命中与过期注销、阶段退出后继续飞行、重开和直接离场清理，以及圆环/扇形/逆时针/单发排列。失败时返回非零退出码。

本期包含Boss选择界面，但不包含存档、录像、解锁、奖励评价、音频、设置菜单或追踪、分裂等扩展弹幕。



窗口渲染验证（需要图形桌面）：

```powershell
& '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path . --script .tools/verify_display.gd
```

该入口在同一窗口依次调整为1280×800、1600×1000、1600×900、1280×1000，验证固定逻辑尺寸、等比缩放和居中偏移；内容截图保存至`outputs/display-verification/`，截图不含引擎在窗口外围绘制的黑边。宽窗口左右各留80像素，高窗口上下各留100像素，已进行实际窗口检查。


## Stage 场景结构

启动资源为 `Game.tscn`，根节点 `GameManager` 在游戏期间常驻，内部 `StageHost` 挂载当前 Stage。当前不使用 Autoload，也不整棵替换 SceneTree 的根场景，避免重复创建管理器。

- `Stage/Stage.cs`：抽象 Node，提供 StageId、Enter(context)、Exit() 及派生类 OnEnter/OnExit。构造时默认禁止处理，Enter 必须在节点就绪后调用，Exit 幂等并停止整个子树的输入和更新。
- `StageContext`：传入当前 GameManager 和可选 BossData，不在已销毁场景节点中保存共享数据。
- `BossSelectStage`：进入时创建 BossSelectUI；退出时取消事件订阅。选择项以 BossSelectItem 显示。
- `BattleStage`：根据 context.BossData 创建通用 Main 战场，离开时停止 BattleManager、清理弹幕。战斗更新仍只由 BattleManager 的物理回调负责，Stage 不重复推进。
- `GameManager.RequestStage(id, bossId)`：检查注册目标与Boss参数，拦截重复请求，延迟到安全时机切换。目标工厂创建后，先禁止旧场景处理，再进入目标；成功后退出、移除并释放旧节点，准备失败时保留旧场景。不会在两帧之间同时运行两个Stage。
- 返回选择只保存选中Boss标识，Stage节点不缓存；每次战斗都重新创建控制器和阶段实例。进入战斗后须释放确认空格，再次按下才会闪避。

`BattleState.Running/Victory` 是战斗内部状态，独立于游戏当前 Stage。R 重开不会切换 Stage，Esc 返回会销毁战斗 Stage。

## 添加 Boss

1. 复制 `Data/Bosses/Boss_01.tres`，设置唯一 Id、DisplayName、Texture、可选 Portrait、Hframes、Vframes、AnimationFps、MaxHp、CollisionRadius、VisualScale、SpawnPosition 和 PhaseProfile。贴图使用真正带Alpha透明通道的PNG，并检查尺寸与透明度。
2. 将新资源按展示顺序加入 `Data/BossCatalog.tres` 的 Entries。目录当前只包含现有“环形守卫”，测试中的其他Boss不会出现在正式目录。
3. 在 `Boss/BossPhase/新Boss标识/` 下实现专属BossPhase和BulletEmitter，各Boss不复用具体阶段。阶段通过BindEmitter绑定运行实例；发射器定义首次发射时间、周期及每次生成的弹幕，生命周期重写应调用基类。使用BossFactory.RegisterProfile注册每次返回全新阶段列表的工厂，在GameManager校验目录之前完成注册。当前配置ID与PhaseProfile均为 `Boss_01`。
4. BossFactory会应用独立配置并创建阶段，BattleManager重开复用同一配置；碰撞读取实例的CollisionRadius，HUD读取实例的MaxHp，无需再修改全局Boss数值或复制控制器。

## 添加 Stage

1. 在 `Stage/` 下新增继承 Stage 的类，实现唯一 StageId、OnEnter(StageContext) 与 OnExit()。_Ready只处理节点绑定，实际进入逻辑放在OnEnter。
2. 新建以Node为根的 `.tscn` 并绑定脚本，子节点可包含Control或Node2D。
3. 在GameManager初始化时，通过RegisterStage注册标识和实例工厂，工厂加载PackedScene并返回全新、尚未入树的Stage。
4. 通过Context.Game.RequestStage请求切换。新场景不应自行修改StageHost或手动更新BattleManager。新增场景若需要额外参数，可扩展StageContext；所有跨场景保存的数据由GameManager或独立数据对象持有。

## Stage 集成验证

```powershell
dotnet build
& '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --scene res://Tests/StageVerification.tscn
& '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path . --script .tools/verify_stages_display.gd
```

集成测试使用临时七Boss目录覆盖多行导航、视口输入分发、确认隔离、独立Boss参数、返回选择、循环切换、扩展Stage注册和空目录。渲染入口通过正式目录验证选择→战斗→返回，并保存截图到 `outputs/stage-verification/`。

随机与移动重现验证（将可执行文件路径替换为本机Godot .NET路径）：

```powershell
& 'D:/Develop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --scene res://Tests/DeterminismVerification.tscn
```

验证固定算法样本、整数拒绝采样、小数闭区间与溢出边界、相等及非法范围不消耗序列，以及Boss选点、速度、到达停止、步末事件、发射起点和同种子同输入的逐步重现。

### 弧度与坐标工具

`VMath.GetDistanceBetween2Points`和`GetAngleBetween2Points`支持四个double坐标或两个Vector2，调用方保证同一坐标空间；重合点方向为0。`StandardizationAngle`将最终方向标准化到[0,2π)，单精度方向会额外处理整圈上界舍入。`PolarMove`返回极坐标位移后的Vector2，支持负距离，不创建或修改节点。工具内部采用双精度，拒绝非有限输入，结果超出返回类型范围时抛错。

`DegreesToRadians`与`RadiansToDegrees`仅转换单位，保留正负与圈数。初始化数据的`AngleRadians`允许任意有限弧度；子弹初始化及`SetDirection`时标准化，速度向量和贴图`Rotation`同步更新。Boss圆周目标、玩家瞄准及弹幕方向统一接入VMath；随机调用次数和顺序不变，同种子同输入可重复，但不承诺与迁移前浮点结果逐位一致。

确定性验证同时覆盖距离、四轴四象限、重合点、多圈与负角度、整圈边界、极坐标正负距离、单位往返转换及非法输入。

### 实体时间线与固定逻辑时间

`VTimeline`由BossController、Phase、Emitter、Bullet和PlayerController各自持有，实体在自身逻辑更新时累计年龄；Boss时间线跨阶段持续。玩家攻击、闪避和受伤保护也由玩家时间线管理。`VTimerProcessor`管理统一的整数逻辑时钟与稳定注册序号，实体动作保留在各自时间线内。每秒60000单位，每毫秒60单位，每个60Hz固定步1000单位。物理回调只推进固定步，不累计渲染delta或墙上时间。

`VTimeline.At(atMs, action)`和`Repeat(startMs, intervalMs, endMs, action)`相对所有者激活时刻；`After(delayMs, action)`相对当前年龄。有限重复包含恰好落在结束时刻的动作，`endMs: null`表示永久重复。时间参数为非负整数毫秒，周期必须为正；已过去的时刻不能再登记。例如`Repeat(2000, 500, 3000, action)`原定在2000、2500、3000毫秒到期；结束点不在周期上时不额外执行。

`new VTimelineAdapter(processor, targets?)`创建不依附于实体的时间线，由处理器自动推进年龄。省略`targets`可创建无目标任务；传入目标时，适配器在创建时复制、去重，并由其`At`、`After`、`Repeat`回调传入当次仍存活的目标快照。最后一个目标失效时取消；全部动作完成且未登记新动作时自动结束。绑定目标的动作应使用适配器上的方法，以便回调前过滤目标。

```csharp
var adapter = new VTimelineAdapter(battle.Timers, new[] { bullet });
adapter.After(2000, alive => ((Bullet)alive[0]).SetSpeed(0));
```

Boss周期发射、阶段选点、子弹延迟变速以及玩家攻击、闪避和受伤保护均由时间线触发。玩家攻击只登记一次永久周期；`Stop()`停止射击，重新初始化从至少等待200毫秒后的原周期格点恢复。玩家闪避、冷却和受伤保护的剩余秒数由玩家时间线年龄与结束时刻计算。阶段退出取消阶段与发射器时间线；已出生子弹的时间线持续到子弹释放。动画和受击闪烁直接计算；子弹寿命在运动后检查。

每步先推进运动与碰撞，再执行本步到期的动作；原定时刻较早者先执行，同刻按注册序号执行。事件可能比设定时刻晚不足一个60Hz逻辑步，新生子弹从下一步开始运动。回调新增动作在当前回调结束后排入，同刻已有动作优先。一次推进补齐所有周期；单次派发最多执行10000次，超限或回调异常会停止推进并报错，重新开始须清场重置。不允许回调递归推进处理器；取消和战斗清场立即阻止旧时间线后续动作。

计时验证场景`Tests/TimerVerification.tscn`覆盖实体和独立时间线的排序、补发、目标清理、回调增删、玩家计时与清场。战斗、场景和随机重现验证覆盖正式发射与碰撞行为；重现对比增加不同显示刷新次数。时序规则改变可能影响旧版命中结果，不承诺旧录像或跨平台浮点逐位兼容。

```powershell
& 'D:/Develop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --scene res://Tests/TimerVerification.tscn
```

### Boss_01三阶段与负血量

Boss_01初始300血：阶段01覆盖300～201血，阶段02覆盖200～101血，阶段03覆盖100～1血，0血胜利。各阶段分别继承BossPhase，绑定独立的两个Emitter。进入新阶段时重新等待1000毫秒发射和5000毫秒选点，Boss位置和动画保持连续；旧阶段的移动状态与时间线退出，已发子弹继续存活。战斗中Q/E切换相邻阶段时，Boss HP设置为目标阶段初始值；首尾阶段无效，胜利后不可复活。碰撞造成切阶段时先取消旧阶段动作，再处理同刻事件，避免血线边界多发旧弹幕。

玩家HP为有符号整数，归零或降为负数都继续参与战斗；仅在int最小值处防止算术回绕。有效受伤仍有1000毫秒保护，闪避和自动攻击不因血量停用。重开恢复玩家3血、Boss300血及阶段01。测试覆盖六个Emitter的等角参数、200/100阈值、跨阶段大伤害、同刻发射交接、负血操作和正常胜利。
