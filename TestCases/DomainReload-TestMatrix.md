# Domain Reload 测试用例矩阵

设计补全日期：2026-10-23。目标：Unity 6000.3.8f1；依据引用聊天 **调研 Unity 关闭 Reload Domain**（`01a12b28-8de8-7503-8bab-89907c96b0b5`）的官方行为、实战案例、自动化方案与字段范围澄清。

**本次补齐分类、案例文档和代表性测试源码，未编译、未运行测试、未操作 Unity、未安装依赖。** 下列“通过标准”是验收要求，不是实测结果。本次新增 32 个测试方法（其中 R2/R3 两个为 Explicit，需单独选择）；“已有实现”不代表整行扩展场景均已实现或验收。

## 共用前置条件与记录

- 固定 Unity、UTF、被测包、Easy Reload Guard、执行入口和源码提交。先核对实际重载组合，再开始一组实验。
- 每个对象记录来源、创建时机、实例身份、存活/销毁状态、字段值、事件响应和拥有的资源。不要只按“字段是否有 SerializeField”决定需不需要 reset。
- 除明确的重编译对照外，同组连续 Play → Stop 至少 3 次，期间不改脚本、不强制重载域。
- 未来执行新增用例时，从新脚本域开始选择所需组；现有 `GuardedCaseCounter.Label` 使用 InitialValue，首次进入需满足框架的干净域前提。测试不会绕过关口；关口额外换域应记为前置条件不满足，不能计作连续 R1 通过。
- 区分：原版行为刻画、故意保留缺陷的复现组、适配后的兼容性验收、正常重载对照。复现组断言“确实累积”通过，不代表兼容性通过。
- 每次运行记录“实际结果 / 证据路径 / 问题归属”：功能失败、未观察到回调、编译失败、工具失败分别标记。恢复原场景与设置是执行阶段的收尾要求，本次不执行该流程。

### 重载组合

| 模式 | Domain | Scene | 设计用途 |
| --- | --- | --- | --- |
| R0 | 开 | 开 | 完整重载基线 |
| R1 | 关 | 开 | 主验收路径，隔离关闭 Domain 的影响 |
| R2 | 开 | 关 | 区分域重载与场景对象复用；单独记录实例和回调 |
| R3 | 关 | 关 | 复用对象专项，不能沿用 R1 的通过结论 |

R2/R3 是补齐的测试设计，不是当前工程已经支持的承诺。若工具明确不支持某组合，工具用例的通过标准是清楚诊断/阻止不支持配置；基础行为用例仍需在隔离副本记录原生行为，不能把阻止进入 Play 记为游戏兼容通过。[配置与流程](https://docs.unity3d.com/6000.3/Documentation/Manual/configurable-enter-play-mode.html)

## A. 重载配置、连续运行与重编译

落点：已有 [PlayReentryTests.cs](../Assets/DomainReloadCases/Tests/Editor/PlayReentryTests.cs) 覆盖 R0/R1；新增 [ReloadModeMatrixTests.cs](../Assets/DomainReloadCases/Tests/Editor/ReloadModeMatrixTests.cs) 提供四组独立入口，详见 [13-ReloadModes](Cases/13-ReloadModes.md)。静态 reset 对照引用现有 Easy Reload Guard，无需新增第三方包。

| ID | 前置条件 / 模式 | 操作步骤 | 通过标准 | 实现情况 |
| --- | --- | --- | --- | --- |
| DR-01 | 同一场景与探针；R0～R3 分开 | 对四组分别记录 static、实例身份、构造初值、序列化/非序列化值及 Awake/OnEnable/Start/OnDisable/OnDestroy | 四组有独立记录；R1/R3 的静态业务值按声明初始化；不伪造统一回调次数，不把 Scene 重建误当 Domain 重置 | 4 个方法已写；R2/R3 Explicit；对象字段为行为记录，未验收 |
| DR-02 | R1；不重编译 | 运行→改计数/订阅/缓存→退出，连续 3 轮 | 修复组每轮从基线开始；一次输入只响应一次；故意未处理组的累积另列行为刻画 | 已有 PlayReentry；覆盖范围需按探针核对 |
| DR-03 | R1 的同一污染路径 | 先连续 3 轮；随后分别做脚本重编译、显式域重载、重开编辑器对照 | 记录哪些污染被换域清掉；重编译后恢复正常不得替代连续无重载路径通过 | IN-04 已有被动观察入口；污染路径与重开编辑器仍为人工设计 |
| DR-04 | 相同版本/场景/输入；Editor Profiler | 分别记录首次与后续进入 Play 的阶段耗时，另录 Player 启动 | 报告含测量条件、样本和阶段；不编造提速阈值，不用 Scene Reload 关闭的 Editor 耗时代表 Player 启动 | 待设计性能记录器；无本轮数值 |

依据：[Domain Reload](https://docs.unity3d.com/6000.3/Documentation/Manual/domain-reloading.html)、[Scene Reload](https://docs.unity3d.com/6000.3/Documentation/Manual/scene-reloading.html)、[Editor Profiler](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-profiling-applications.html)。

## B. 静态字段、单例、集合与保留状态

源码案例：[StateOwnershipCase.cs](../Assets/DomainReloadCases/Runtime/StateOwnershipCase.cs) 及 [07-StateOwnership](Cases/07-StateOwnership.md)，保留原 Leaky/Guarded 对照。适配目标包为 `com.yongshi.easyreloadguard@0.1.0`，由 [ResetStatic / ResetMethod](../Packages/com.yongshi.easyreloadguard/Runtime/ResetAttributes.cs) 声明责任；不扩展成反射清空全部静态字段。

| ID | 前置条件 / 模式 | 操作步骤 | 通过标准 | 实现情况 |
| --- | --- | --- | --- | --- |
| ST-01 | R0/R1；计数与 IsInitialized | 首轮计数变更并设已初始化；第二/三轮重复启动 | 会话计数回基线；必要初始化仍执行一次，不能被旧标志跳过 | StateOwnershipTests.ST01；3 轮 R1；R0 扩展未实现 |
| ST-02 | R1/R3；static 指向 Unity 对象 | 创建单例→销毁目标→保留托管引用→重进 | 当前 owner 有效且唯一；销毁引用不阻止重建；记录 Unity 对象判空与引用身份，不能只因 static 就判错 | StateOwnershipTests.ST02；3 轮 R1；R3 专项未实现 |
| ST-03 | R1；普通集合、readonly 集合、外部别名 | 分别验证置 null、Clear、重建为新集合三种声明，再通过别名访问 | 每种结果与声明一致；Clear 保留引用且元素归零；重建时别名/订阅按所有权处理；不能把“无元素”等同于“可正常继续用” | StateOwnershipTests.ST03；3 轮 R1 与旧别名断言 |
| ST-04 | R1；静态自动属性与两个闭合泛型类型 | 改属性与 Generic<A>/Generic<B> 状态，重进 | 每个纳入声明的闭合类型都重置；遗漏的直接构造类型有明确诊断或注册入口，不假称全泛型覆盖 | StateOwnershipTests.ST04；两个闭合类型显式登记，非全泛型扫描 |
| ST-05 | R1；纯计算缓存、偏好、每局状态并存 | 填充缓存并改每局状态后重进 | 明确保留的缓存/偏好仍可用；每局数据重置；保留项不能持有已销毁的场景对象 | StateOwnershipTests.ST05；3 轮 R1，保留项与会话项分别断言 |

## C. 对象生命周期与非序列化字段

源码与断言：[ObjectLifetimeProbe.cs](../Assets/DomainReloadCases/Runtime/ObjectLifetimeProbe.cs)、[ObjectLifetimeTests.cs](../Assets/DomainReloadCases/Tests/Editor/ObjectLifetimeTests.cs)、[ScriptableStateCase.cs](../Assets/DomainReloadCases/Runtime/ScriptableStateCase.cs)、[ScriptableStateTests.cs](../Assets/DomainReloadCases/Tests/Editor/ScriptableStateTests.cs)。详见 [08-ObjectLifetime](Cases/08-ObjectLifetime.md)、[09-ScriptableObject](Cases/09-ScriptableObject.md)。采用对象/状态根重建和显式资源归属；不要求给所有类字段逐一加 Reset。

| ID | 前置条件 / 模式 | 操作步骤 | 通过标准 | 实现情况 |
| --- | --- | --- | --- | --- |
| OBJ-01 | R1；普通 C# 状态树，一份每局 new，一份由 static 持有 | 改根与子对象字段→重进 | 新建树自然恢复初始值；保留树按明确 reset 重建；旧树的事件/任务不再持有可写入口 | StateOwnershipTests.OBJ01；3 轮 R1，旧根事件与写入口关闭 |
| OBJ-02 | R0/R1；普通 MonoBehaviour 字段矩阵 | 分别改可序列化 public、SerializeField private、NonSerialized、自动属性、集合/nullable；对比编辑器值、构造初值、运行值 | 每种字段按该类型的真实序列化与对象生命周期记录；不把没有 SerializeField 一律当非序列化，不把热重载规则当场景序列化规则 | ObjectLifetimeTests.OBJ02 两个方法：EditMode 字段结构 + 3 轮 R1 Prefab；R0 扩展待补 |
| OBJ-03 | R0/R1；预存、动态、inactive、disabled 场景对象 | 依次进入、启用、禁用、销毁、退出；记录引用与回调 | 正常场景重建后的对象正确初始化；未启用对象也不被漏出所有权清理；不靠固定 OnEnable 次数判断所有行为 | ObjectLifetimeTests.OBJ03；3 轮 R1；R0 扩展待补 |
| OBJ-04 | R2/R3；预存对象和非序列化实例状态 | 改实例状态后重进，核对实例是否复用与构造/生命周期日志 | 复用对象所需业务初始值由明确入口恢复；不把构造器/Awake 每轮调用当先验条件 | ReloadModeMatrix 的 R2/R3 记录身份/字段；实例业务重置修复组仍待实现 |
| SO-01 | R1；持续加载的 ScriptableObject .asset 与 static 引用 | 修改运行缓存、订阅与非序列化值，退出再进，比较磁盘资产 | 每局状态按声明重置；作者配置与磁盘资产不被无意保存；Scene Reload 不被当作资产重建保证 | ScriptableStateTests.SO01；3 轮 R1 与源资产 SHA256 对照 |
| SO-02 | R1；CreateInstance/Instantiate 运行副本与事件通道 | 创建副本→订阅→退出→新局再建；保留原资产对照 | 旧副本释放/销毁且退订；新副本状态正确；事件通道只响应当前拥有者 | ScriptableStateTests.SO02；3 轮 R1，副本销毁/退订及源资产隔离 |

依据：[字段规则](https://docs.unity3d.com/6000.3/Documentation/Manual/script-serialization-rules.html)、[脚本热重载规则](https://docs.unity3d.com/6000.3/Documentation/Manual/script-serialization-how-unity-uses.html#hot-reload)。调研最后一轮已明确纠正“非序列化意味着所有字段都要处理”的误解，本组以对象身份和实际存活范围判断。

## D. ExecuteAlways、ExecuteInEditMode 与编辑器对象

完整设计、已有实现映射及通过标准见 [06-ExecuteAlways.md](Cases/06-ExecuteAlways.md)：EA-01～EA-08。该类单独列项，不用普通 MonoBehaviour 测试替代。

- 包：Unity 引擎行为；修复对照使用 `com.yongshi.easyreloadguard@0.1.0`。
- 实际源码：[ExecuteAlwaysCaseCounter.cs](../Assets/DomainReloadCases/Runtime/ExecuteAlwaysCaseCounter.cs)。
- 实际场景/Prefab：[ExecuteAlways 目录](../Assets/DomainReloadCases/ExecuteAlways/)。
- 自定义 Editor 状态另见 IN-03，真实程序集重载另见 IN-04。

## E. 事件、异步、线程与外部资源

新增 [ExternalWorkCase.cs](../Assets/DomainReloadCases/Runtime/ExternalWorkCase.cs)、[OwnedResourceCase.cs](../Assets/DomainReloadCases/Runtime/OwnedResourceCase.cs) 与 [NativeJobOwnershipTests.cs](../Assets/DomainReloadCases/Tests/Editor/NativeJobOwnershipTests.cs)。方法映射及操作见 [10-ExternalWork](Cases/10-ExternalWork.md)、[11-OwnedResources](Cases/11-OwnedResources.md)。UniTask [PKG-01](Cases/01-UniTask.md)、ZString [PKG-02](Cases/02-ZString.md) 的实际包适配状态独立记录，通用代表实现不替代包验收。

| ID | 前置条件 / 模式 | 操作步骤 | 通过标准 | 实现情况 |
| --- | --- | --- | --- | --- |
| EV-01 | R1；本类自有 static event | 每轮订阅并触发一次，退出再进 | 当前订阅只调用一次；事件清空仅限自身拥有的事件 | ExternalWorkTests.OwnedEvent；3 轮 R1 |
| EV-02 | R1；Application / EditorApplication / 第三方事件源 | 重复订退、禁用、销毁、退出，再从外部事件源触发 | 旧订阅不调用；重复退订安全；编辑器长期订阅与 Play 临时订阅分别计数，不能清掉别的 owner | 真实 Editor 事件 3 轮 R1 + 普通 C# 外部源 EditMode 所有权断言；未接第三方包 |
| EV-03 | R1；两个 owner 先后替换 formatter/委托槽 | A 注册→B 接管→A 释放→B 释放 | A 不覆盖 B 的后来注册；B 按协议恢复；无失效闭包 | 两个 EditMode 方法验证 A/B 释放顺序；真实 ZString、跨 Play 扩展待接 |
| ASYNC-01 | R1；长 Task/UniTask 与会话 token | 启动→退出→记录取消请求与任务最终状态→再进 | 取消请求与真正停止分别有证据；旧工作不再生产结果；原生 AsTask/适配桥接取消语义分开 | 真实 Task 3 轮 R1、最终 Canceled/finally；UniTask 桥接仍为历史参考 |
| ASYNC-02 | R1；Thread / Timer | 每轮创建工作者，退出时停表、取消并有界等待，再进 | 活跃 worker/定时回调回基线；停止超时可见；字段置 null 不能当作线程已停 | Thread/Timer 各一个 3 轮 R1 方法；超时有异常，但故障注入未实现 |
| ASYNC-03 | R1；不可取消外部完成源与真实异常 | Stop 后才完成旧请求；另一路抛普通业务异常 | 旧结果提交被拒绝；新会话不受污染；真实故障仍可见，不吞成取消或成功 | 晚完成跨 3 轮 R1 + EditMode 原异常身份断言；UniTask 专项待接 |
| RES-01 | R1；NativeArray 与 JobHandle | 调度→中途退出→按所有权等待作业并释放→再进 | Job 完成后安全 Dispose；案例拥有的活跃容器数回基线；无越界/释放后访问 | NativeJobOwnershipTests；3 轮 R1，Complete 后 Dispose；用现有 Collections 2.6.2 |
| RES-02 | R1；ComputeBuffer / RenderTexture / 自建材质 | 每轮申请/使用，中断运行并释放，再次申请 | 自有资源回对应基线，无旧对象被引用；共享资产不被销毁 | RenderTexture/材质副本：3 轮 R1 + EditMode 幂等释放；ComputeBuffer 未实现 |

包适配责任：UniTask 用 token、任务桥接及提交门禁；ZString 用自己的 formatter 注册所有权；原生/线程资源需要具体释放入口。它们都不能通过“把引用设 null”自动完成清理。

## F. 初始化时机与编辑器重载入口

新增 [InitializationOrderCase.cs](../Assets/DomainReloadCases/Runtime/InitializationOrderCase.cs)、[InitializationOrderTests.cs](../Assets/DomainReloadCases/Tests/Editor/InitializationOrderTests.cs) 与 [EditorLifecycleTests.cs](../Assets/DomainReloadCases/Tests/Editor/EditorLifecycleTests.cs)。Editor API 留在 Editor 测试程序集。完整方法和真实重编译的被动观察步骤见 [12-Initialization](Cases/12-Initialization.md)。

| ID | 前置条件 / 模式 | 操作步骤 | 通过标准 | 实现情况 |
| --- | --- | --- | --- | --- |
| INIT-01 | R0/R1；静态构造器、字段初始化器、各 RuntimeInitialize 阶段 | 每轮记录初始化和首次业务读取 | 业务读取前状态就绪；第二轮仍完成必要初始化；不依赖静态构造器每轮执行 | InitializationOrderTests.RuntimePhases；3 轮 R1；R0 扩展待补 |
| INIT-02 | R1；A 依赖 B，二者原本在同一阶段初始化 | 改注册/加载顺序，以统一入口组织依赖后对照 | B 就绪后 A 才读取；无隐含同阶段回调顺序假设；重复初始化不会丢已运行任务 | InitializationOrderTests.OrderedReset；3 轮 R1 显式 B→A 及真实 Awake |
| IN-03 | R0/R1；EditorWindow / Editor 工具静态缓存 | 先在 Edit 中操作工具，再反复 Play→Stop | 每次进入所需 reset 由 InitializeOnEnterPlayMode 等明确入口执行；InitializeOnLoad 不被误当每局入口；应保留的编辑器偏好仍保留 | EditorLifecycleTests.EditorSessionEntry；3 轮 R1；未创建 EditorWindow/R0 对照 |
| IN-04 | R1；AssemblyReloadEvents 与 Play 状态事件 | 分别做普通 Play 切换和真正脚本重编译 | 两类事件各有日志；只在真实程序集重载发生的清理，不被误当每次 Play 都会发生 | 普通 Play 事件自动断言 3 轮 R1；真实重编译为被动菜单 + 人工步骤 |

依据：[RuntimeInitializeOnLoadMethod](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RuntimeInitializeOnLoadMethodAttribute.html)、[InitializeOnEnterPlayMode](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/InitializeOnEnterPlayModeAttribute.html)。同一初始化阶段不假定多个回调有可靠先后顺序。

## G. 包、DLL、自动化工具和构建边界

本组按调研聊天列出的历史材料设计；**本次没有重新核对外部最新版本，也没有安装候选工具**。历史 Issue 的通过要求是“在锁定版本上验证/记录”，不能写成当前 Unity 必现。

| ID | 对象 / 包名 | 测试步骤与通过标准 | 适配方案 / 案例路径 | 实现情况 |
| --- | --- | --- | --- | --- |
| PKG-INIT | UniTask：`com.cysharp.unitask@2.5.11` | 手动初始化→启动任务→自动初始化；核对任务是否丢失、挂起和节点数；修复后唯一初始化 owner，任务最终状态明确 | [UT-06/07](Cases/01-UniTask.md)；现有测试目录 `Assets/UniTaskValidation/` | 新增设计待实现 |
| PKG-TWEEN | DOTween，程序集 `DOTween.dll`；本地未安装，未登记 UPM ID | 锁定版本；R0/R1/R3 分别创建并推进 tween，退出再进；动画到终值、更新对象有效唯一、队列回基线 | 拟 `Assets/DOTweenValidation/`；优先公开初始化/停止/清理 API，先确认作用域；GoKit 是另一案例，不能替代 DOTween 结果 | 仅历史 #385 / 官方发布记录线索 |
| TOOL-01 | UnityDomainReloadHelper；未安装，UPM ID 待取实际包清单 | 对 null / 指定值 / new / Clear、事件、属性、readonly 分别建立对照；生成/执行后结果符合声明，不把 List 清成 null 误当 Clear | 拟 `Assets/ReloadToolValidation/DomainReloadHelper/`；按工具支持字段声明，事件/属性用显式清理入口 | 待选型；[调研来源](https://github.com/joshcamas/unity-domain-reload-helper) |
| TOOL-02 | QuickPlayMode；未安装，UPM ID 待核对 | 标记/未标记类型、泛型、事件、自动属性、静态初始化与 readonly 分组；检查生成 IL/源码差异、连续 Play、移除工具后行为 | 拟 `Assets/ReloadToolValidation/QuickPlayMode/`；只接受明确 opt-in 范围，记录 readonly/编译修改成本 | 待选型；[调研来源](https://github.com/stalomeow/QuickPlayMode) |
| TOOL-03 | AutoStaticsCleanup / 社区生成器；未安装 | 原生 6.5 与当前 6.3 隔离；验证支持版本、排除保留项、分析器漏报/误报、生成代码与实际构建 | 原生功能是调研中的 Unity 6.5 项，不当作 6.3 API；社区移植另锁版本；拟 `Assets/ReloadToolValidation/GeneratedCleanup/` | 待选型；[原生来源](https://docs.unity3d.com/6000.5/Documentation/Manual/WhatsNewUnity65.html)、[社区来源](https://github.com/alexisstrat/unity-auto-statics-cleanup-generator) |
| TOOL-04 | UnityManuallyReload；Editor 源码工具，未安装 | 锁程序集→改脚本→编译完成但未重载→解锁→进入 Play；异常/取消路径也核对解锁 | 拟 `Assets/ReloadToolValidation/ManualReload/`；编辑器入口管理锁所有权；最终新代码生效、组件可正常挂载、不遗留锁；不能在未重载时声称用了新脚本 | 待选型；[调研来源](https://github.com/ZeroUltra/UnityManuallyReload) |
| BUILD-01 | 当前适配代码，R0/R1 与 Player | 分别验证完整重载、无域重载和 Mono/IL2CPP Player 启动退出 | Editor API 不进入 Runtime；构建可初始化/退出；R1 通过不自动推广到 Player/IL2CPP | 待实现，不实际构建 |
| BUILD-02 | 包 / 预编译 DLL 升级与适配器 | 固定包 ID、版本、DLL hash；升级/缺少成员/无公开清理 API 时核对诊断 | 当前版本落在适配已验证范围才接受结论；接口缺失可见；必要时要求完整重载，不静默反射修改未知内部状态 | 包单元测试有部分；实际外部版本矩阵待补 |

## 覆盖关系与交付边界

- 原五个实战项目继续保留其案例文档；本矩阵补充它们没有覆盖的引擎与生命周期用例。
- 新增源码只登记实现和断言，没有改写历史通过数，也不生成新的“运行成功”记录。
- 复现/修复差异应标注：业务源码改了哪里、适配器引用哪个包、是否修改供应商、移除适配后如何恢复。标为“拟路径”的文件本次没有创建。
- 用户此次只要求补齐用例；实际执行、安装、外部项目接入和再次提交 Git 均未开展。DR-03/04、EA-06～08、实际第三方包/工具/构建仍保留专项设计，不冒充自动测试已经完成。
