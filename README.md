# BulletHell

Godot 4.7.2 .NET / C# 2D Boss 战原型。启动后进入Boss选择场景，可选择并进入通用战斗场景；战斗支持移动、闪避、自动攻击、伤害、胜负和重开。

## 运行与操作

使用 `.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe` 导入 `project.godot`，按 F5。需要 .NET 8 或更高版本 SDK。

- Boss选择界面：鼠标点击只改变选中项，方向键按四列网格导航，空格进入战斗。边缘不环绕，末行缺项时向下选择最后一项。
- 战斗中：WASD 或方向键移动，斜向速度归一化。
- Esc：从战斗返回Boss选择，保留上次选择。
- 空格：沿输入方向闪避；静止时沿最近一次移动方向，初始向上。按住不会重复闪避。
- 自动瞄准 Boss 射击，无需攻击键。
- 胜利或失败后按 R：重建战斗。

左上角显示双方 HP、闪避冷却、战斗时间和结果。玩家以蓝色几何图形表示，白色中心为判定区域，闪避变为金色，受击后闪烁。

## 参数与保留行为

通用战斗与玩家参数位于 `Core/BattleConfig.cs`；当前Boss的独立配置位于 `Data/Bosses/RingBoss.tres`。默认逻辑分辨率与启动窗口为1280×800（16:10），窗口可调整大小；通过canvas_items与keep策略整体等比缩放，比例不同时居中并显示纯黑边，窗口尺寸不影响逻辑坐标与移动速度；位置和半径使用像素，速度使用像素/秒，时间使用秒。角度0°向右、90°向下。

| 对象 | 参数 |
|---|---|
| Boss | 位置(640,250)，100 HP，碰撞半径32，图片倍率3 |
| 敌弹 | 每1秒24发完整圆环，首发在1秒，速度180，寿命4秒，半径6，伤害1，颜色索引0，图片倍率3 |
| 玩家 | 出生(640,600)，3 HP，速度240，半径5，中心限制在圆心(640,400)、半径395的圆内，保证判定圆完整位于直径800的活动区域 |
| 闪避 | 速度720，持续0.15秒且无敌，触发起冷却1秒 |
| 受击 | 无敌1秒，无敌时敌弹穿过，不销毁 |
| 玩家弹 | 每0.2秒单发，首发在0.2秒，速度600，寿命2秒，半径3，伤害1 |
| 容量 | 活动弹幕最多2048颗，满额跳过新弹并输出调试提示 |

Boss仍固定不动，保留原环形弹幕的角度、时钟余量与外观。`Assets/Boss.png`为64×64透明像素图；`Assets/Sprite_02.png`为160×16的十色图集，每格16×16，颜色从左至右为0～9。Boss与敌弹实际默认倍率均为3（旧README中的2已纠正）。贴图居中且最近邻过滤，弹幕按发射角度旋转，外观缩放不改变移动或碰撞。

没有Boss接触伤害。敌弹只伤玩家，玩家弹只伤Boss；有效命中销毁。弹幕不因出屏提前释放。胜负后清理全部弹幕并停止模拟；同一物理步双方死亡判定失败。

背景使用从旧项目复制的 `Assets/UI/UI_400x600_gamearea_4.png`（400×600）。图片以最近邻过滤、等比覆盖并居中裁切铺满1280×800逻辑画面，上下超出部分裁去；背景位于角色和弹幕下方，不接收鼠标输入，也不覆盖窗口黑边。HUD使用深色描边保持可读性。圆形边界不额外绘制，普通移动与闪避共用径向约束。

## 模块与扩展

- `Core`：BattleManager拥有唯一战斗物理更新入口，按玩家、Boss、弹幕、胜负顺序执行；BattleConfig集中参数。Step支持注入移动与闪避输入，便于验证。
- `Player`：PlayerController协调PlayerMovement、PlayerDodge、PlayerHealth、PlayerAttack。生命变化和死亡使用C#事件。
- `Boss`：BossController管理HP与阶段；BossPhase定义Enter、Advance、ShouldEnd和Exit。通过入树前SetPhases配置有序阶段；首版只使用RingBossPhase，最后阶段保持运行，死亡或结束时退出一次。
- `Bullet`：BulletManager负责生成、连续碰撞、容量与销毁；BulletEmitter管理间隔和发射点；BulletPattern实现SinglePattern与RingPattern；BulletBehavior实现StraightBehavior。新增样式或运动可分别派生对应抽象类，无需改写Boss控制器。
- `Bullet/Bullet.cs`保留原Initialize签名和默认倍率2；ConfigureShot使用首版阵营参数。普通容器中的子弹自行物理更新，管理器内的子弹只由管理器驱动。
- `Boss/Boss.cs`为兼容入口，继承新BossController；普通容器下自行驱动，当前战斗交给BattleManager。
- `Main.cs`装配场景、默认InputMap及简单中文HUD。默认绑定同时兼容物理键和辅助输入设备的逻辑键码；已有同名输入动作不会被覆盖。

碰撞使用子弹相对目标运动线段与双方半径之和判定，包含高速穿越与静止重叠。闪避与受击保护按物理步判定，碰撞后再推进计时；固定步为Godot项目默认60Hz。此阶段不承诺录像确定性。

## 验证

```powershell
dotnet build
& '.tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --scene res://Tests/BattleVerification.tscn
```

也可通过原`.tools/verify.gd`入口运行同一验证场景。验证包含原弹幕时机、角度、图集、速度和寿命，圆心、圆内点、四轴与斜向边界、闪避越界、脚本迁移路径、背景布局、分辨率配置、闪避冷却、无敌防重复伤害，自动攻击与高速碰撞，阶段生命周期、同帧死亡、冻结、连续重开和容量上限。失败时返回非零退出码。

本期包含Boss选择界面，但不包含存档、录像、随机数管理、解锁、奖励评价、音频、设置菜单或追踪、分裂等扩展弹幕。



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
- `BattleStage`：根据 context.Boss 创建通用 Main 战场，离开时停止 BattleManager、清理弹幕。战斗更新仍只由 BattleManager 的物理回调负责，Stage 不重复推进。
- `GameManager.RequestStage(id, bossId)`：检查注册目标与Boss参数，拦截重复请求，延迟到安全时机切换。目标工厂创建后，先禁止旧场景处理，再进入目标；成功后退出、移除并释放旧节点，准备失败时保留旧场景。不会在两帧之间同时运行两个Stage。
- 返回选择只保存选中Boss标识，Stage节点不缓存；每次战斗都重新创建控制器和阶段实例。进入战斗后须释放确认空格，再次按下才会闪避。

`BattleState.Running/Victory/Defeat` 是战斗内部状态，独立于游戏当前 Stage。R 重开不会切换 Stage，Esc 返回会销毁战斗 Stage。

## 添加 Boss

1. 复制 `Data/Bosses/RingBoss.tres`，设置唯一 Id、DisplayName、Texture、可选 Portrait、MaxHp、CollisionRadius、VisualScale、SpawnPosition 和 PhaseProfile。贴图使用真正带Alpha透明通道的PNG，并检查尺寸与透明度。
2. 将新资源按展示顺序加入 `Data/BossCatalog.tres` 的 Entries。目录当前只包含现有“环形守卫”，测试中的其他Boss不会出现在正式目录。
3. 若沿用现有环形阶段，PhaseProfile保留 `ring` 即可。新行为在 `Boss/` 下继承 BossPhase，并通过 `BossFactory.RegisterProfile` 注册一个每次返回全新阶段列表的工厂；注册应在 GameManager 校验目录之前完成。
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
