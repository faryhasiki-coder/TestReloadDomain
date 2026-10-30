# Easy Reload Guard

用于关闭 Domain Reload（Enter Play Mode Options → DisableDomainReload）的工具包：静态状态重置、退出 Play 时释放、第三方 UPM/DLL 与业务程序集的 Editor 适配、离线检查与基线、强制重载。

- **不影响出包逻辑（最强约束）**：运行时程序集 `EasyReloadGuard` 只有特性，以及 `PlaySession` 上带 `[Conditional("UNITY_EDITOR")]` 的 void 命令。Player 中没有会话状态、启动入口、退出订阅或取消令牌；业务的取消、回写、释放与初始化顺序完全由业务自己决定。
- **接入边界**：业务代码在**已有成员**上加特性，并为此引用 `EasyReloadGuard`（自动引用，Assembly-CSharp 不需要改 asmdef；asmdef 组织的业务添加该引用）。唯一允许新增的业务成员是**只在 Editor 编译的重置方法**：方法连同 `[ResetMethod]` 整体放在 `#if UNITY_EDITOR` 内，只把本类型的静态状态恢复为新域首次进入 Play 时的样子（见[接入手册](Documentation~/接入手册.md)第 4 节），Player 中不存在。除此之外，不要**为本包**新增业务方法、Unity 消息、参数、登记调用或控制分支（即业务不依赖本包的任何运行时状态或流程）。第三方代码放到独立的 Editor 适配器（见第 4 节）；没有可用入口时用 `KnownIssue` 记录，或用强制重载兜底。
- **接入手册**：拿到检查报告后如何判断归属（业务缺陷 / 改模式源头 / 本包声明 / 适配器）、按上下文选择处理方式、禁忌清单与大工程的推进节奏，见 [Documentation~/接入手册.md](Documentation~/接入手册.md)。
- **异步生命周期由业务负责**：异步任务、后台循环与晚到结果不得跨越其 owner 的生命周期，这是业务自身的正确性要求（Player 中切场景、返回主菜单、登出时同样成立），不属于“为本包改业务”。业务用自有取消源或引擎自带的 `Application.exitCancellationToken` / `MonoBehaviour.destroyCancellationToken` 结束异步工作；本包不替业务取消、搁置或改写异步延续，只由检查器提示风险（RG005/RG006，见第 1 节“异步与后台工作”）。
- **Player 中没有开销**：Player 每次启动都是新的域，重置引擎只在 Editor 关闭 Domain Reload 时执行。
- **方案统一**：新代码和旧代码都使用同一套特性；第三方代码使用适配器；实在无法处理时强制重载兜底。

> 0.3.0 接入能力（2026-10-29）：检查器读取方法体 IL（只读，限声明类型内），每条报告附“上下文”（初值、赋值与内容修改位置、分析范围），建议按上下文给出；报告开头新增按来源 → 程序集 → 类型分组的工作清单；新增 RG013（已声明的重置会破坏下一会话）与 RG014（`[ResetMethod]` 没有覆盖类型内的可变字段）；私有且只在类型初始化时写入的字段/目录降为提示。允许业务新增 Editor-only 重置方法。新增退出 Play 后的残留探测（`Library/EasyReloadGuard/ResidueReport.md`）。新增[接入手册](Documentation~/接入手册.md)。6000.3.1f1 batchmode 验证：编译通过，EditMode 63 项中 62 通过，`EasyReloadGuard.Editor.Tests` 全部通过（新增 9 项）；唯一失败为既有的 MATH-02 全量顺序问题，单独运行通过。**残留探测尚未在用户编辑器中做 PSP 实测。**
>
> 0.2.0 异步规范调整（2026-10-28）：异步生命周期改由业务负责，移除 2026-10-27 短暂加入的 `IAsyncReloadHandler` / `AsyncHandler` 及工程侧样例原型，新增检查规则 RG005/RG006；同日清除接入样例的接入代码，样例恢复接入前形态。2026-10-28 经 Unity Pipeline 在 6000.3.1f1 下验证：编译通过，`EasyReloadGuard.Editor.Tests` **40/40 通过**（含新增异步规则测试）；检查为错误 0、警告 10、提示 1、已基线 8，阻断 0。较 10-27 新增的 3 条警告为 RG005 `AutoSaveService.Loop` 与恢复报告的两条样例 RG023（`AutoSaveService.s_Running`、`ProfileLoader.CurrentProfile`），RG006 未命中（该检查在清除样例接入代码之前运行）。**未运行 PSP 与接入验收。**
>
> 2026-10-27 的验证（Unity 6000.3.1f1，GPT/Codex 执行）对应调整前的代码：第 03 轮运行时隔离调整与 `IAsyncReloadHandler` 编译通过，`EasyReloadGuard.Editor.Tests` 40/40 通过；工程侧异步原型的后台工作验收 6/6、完整接入验收 11/12（INT-05 编辑态预览前置条件失败），四场景各 3 轮 Pipeline PSP 无新增错误。异步原型已移除，其 6/6 与 ExecuteAlways 场景的 PSP 结果不再适用；原型说明已归档到 `F:/本地文档/项目技术方案文档/域重载/归档/`。下面的 0.2.0 记录是隔离调整前的历史结果。
>
> 0.2.0 验证状态（Unity 6000.3.1f1，2026-10-27）：
> - **批处理流水线**：编译无本包警告；EditMode 测试 53 项，52 通过。未通过的 MATH-02 单独运行通过，全量失败源于测试顺序与 InitialValue 关口强制重载的相互作用。
> - **用户打开的编辑器**（通过 `com.unity.pipeline` 驱动，关闭 Domain Reload、开启 Scene Reload）：同一域内连续 4 轮 Play→Stop，UniTask 回调保持 16 个，持久登记每轮执行，owner 销毁后登记移除，Console 无错误。
> - **检查**：内置适配器生效、测试工程排除 `com.unity.pipeline` 后，错误 0、警告 7（RG023 已知问题 6 条），退出码 0；接入后再做 3 轮 PSP，Console 无错误、无警告。
>
> 0.1.0 验证状态（Unity 6000.3.1f1，2026-10-22）：
> - **批处理流水线**：编译无错误、无警告；EditMode 测试 27/27 通过，其中包括关闭/开启 Domain Reload 时各连续 3 轮 Play→Stop 的正式案例（见 `Assets/DomainReloadCases`）。
> - **用户打开的编辑器**（通过 `com.unity.pipeline` 驱动）：EditMode 测试全部通过；人工 `editor_play` / `editor_stop` 连续 3 轮重入验证通过；关口强制重载并自动进入 Play 验证通过。
> - **尚未人工检查**：设置页 UI、Block 模式。

---

## 1. 新代码：一个特性解决

```csharp
using EasyReloadGuard;

public class GameManager : MonoBehaviour
{
    [ResetStatic] public static GameManager Instance;                 // 置 null
    [ResetStatic(ResetMode.InitialValue)] static float s_Speed = 1f;  // 恢复首次进入 Play 时的值
    [ResetStatic(ResetMode.Clear)] static readonly List<Enemy> s_All = new List<Enemy>(); // Clear()，保留引用
    [field: ResetStatic] public static event Action<int> ScoreChanged; // 事件 / 自动属性使用 field: 目标
    [field: ResetStatic] public static int Score { get; private set; }
}
```

| 状态类型 | 写法 |
| --- | --- |
| 单例、普通静态字段 | `[ResetStatic]` |
| 有非默认初值的值类型/string | `[ResetStatic(ResetMode.InitialValue)]` |
| `static readonly` 集合 | `[ResetStatic(ResetMode.Clear)]` |
| 静态事件、自动属性 | `[field: ResetStatic]` |
| 复杂 owner（池、注册表、需要按顺序恢复的不变量） | 标注 owner 的静态无参重置方法 `[ResetMethod(order)]`，该类型整体视为 owner 自行管理，检查器用 RG014 核对每个可变字段都被处理。没有现成入口时可新增 `#if UNITY_EDITOR` 的重置方法（见接入手册第 4 节） |
| 订阅、线程、计时器、Socket、Native 内存 | 标注业务已有、语义适用的静态无参停止/释放方法 `[OnExitPlayMode(order)]`（如 `Stop()`）；没有现成入口时在 Editor 适配器中处理，不要为此新建方法 |
| 有意跨会话保留的状态 | `[ReloadSafe("原因")]`（字段/类型/程序集，原因必填） |
| 泛型单例 `Singleton<T>.s_Instance` | 在泛型定义的字段上加 `[ResetStatic]`，自动展开所有“经派生类出现”的闭合类型；业务直接使用的闭合类型在 Editor 适配器中补充：`b.ResetField(typeof(EventBus<EnemyKilled>).AssemblyQualifiedName, "s_Handlers")` |
| ScriptableObject 等 `OnEnable` 只执行一次的实例状态 | 特性只处理静态成员。在 Editor 适配器中用 `OnReset` 定位已加载实例，调用其已有入口（例如重新执行原有的 `OnEnable`，等同开启 Domain Reload 时的行为）；不要在业务 `OnEnable` 中插入登记 |
| 异步任务、后台循环、晚到的异步结果 | 业务 owner 负责，见下文“异步与后台工作”。业务已有的停止/取消方法可标注 `[OnExitPlayMode]`；第三方代码用适配器调用其自带的取消入口，没有入口时 `KnownIssue` 记录或 `RequireDomainReload` 兜底 |

`ResetMode.Default` 有一个陷阱：`static float s_Speed = 1f` 会被重置为 0。首次进入 Play 时，如果观察到初值不是 default，会输出警告。

### 异步与后台工作（业务 owner 负责）

开启 Domain Reload 时，进入 Play 会卸载旧域，上一会话挂起的 `await` 延续随之失效；关闭后，这些延续会在下一会话（或编辑模式）继续执行，旧结果可能写入新会话。这类问题在 Player 中切场景、返回主菜单、登出时同样存在，因此由业务在自己的生命周期内解决，本包不提供会话令牌，也不替业务取消或搁置延续。

- 每个异步工作都要有明确的 owner 和结束点：MonoBehaviour 发起的用 `destroyCancellationToken`；跨场景的服务用自有 `CancellationTokenSource`，在 owner 的停止/释放方法中取消（该方法可标注 `[OnExitPlayMode]`）；只需随 Play 会话或应用结束的用 `Application.exitCancellationToken`（退出 Play 时取消、下次进入时重建；关闭 Domain Reload 时的行为尚未在本工程实测，接入时以 PSP 为准）。
- 令牌在发起工作时读取并沿调用链传递（`Task.Delay(ms, token)`、支持取消的 I/O）；不要把令牌缓存进跨会话存活的静态字段。
- `await` 之后写共享状态前，确认本次工作仍属于当前 owner（令牌未取消）。被取消的工作以取消结束，不要吞成成功，也不要用异常伪装。
- 避免 `async void`；Unity 消息方法（如 `async void Start()`）不得不用时，在内部传递取消令牌并处理异常。线程池计时器、线程在停止方法中 `Dispose`/结束，只把字段置 null 不会停止已在运行的工作。

```csharp
public static class ProfileLoader
{
    [ResetStatic] public static string CurrentProfile { get; private set; }

    public static async Task LoadAsync(Func<CancellationToken, Task<string>> fetch)
    {
        var token = Application.exitCancellationToken;   // 本次会话的令牌，发起时读取
        CurrentProfile = null;
        var profile = await fetch(token);
        token.ThrowIfCancellationRequested();              // 会话已结束：以取消结束，不回写
        CurrentProfile = profile;
    }
}
```

检查器对 Player 程序集报告 `async void` 方法（RG005）和类型为 `Task`、`ValueTask`、`CancellationTokenSource`、`Thread`、计时器的未处理静态字段（RG006），两者均为可入基线的警告。检查只基于元数据，无法判断 `await` 之后是否写入了已失效的状态，最终以连续 Play→Stop→Play 验证为准。

## 2. 执行顺序（关闭 Domain Reload 时）

```
进入 Play（RuntimeInitializeLoadType.SubsystemRegistration）
  PlaySession.RegisterReset 登记项（先进先出，执行后移除）
  → PlaySession.RegisterPersistentReset 登记项（按登记顺序，每次都执行，直到解除或 owner 销毁）
  → [ResetStatic] 字段（按 类型.字段名 排序）
  → [ResetMethod] 与适配器重置（按 Order，相同 Order 按名称）

退出 Play（ExitingPlayMode，Domain Reload 开启时也执行）
  [OnExitPlayMode] 与适配器释放（按 Order）
```

- 单个步骤失败只记录异常，不中断后续步骤。失败汇总后输出错误；默认在下次进入 Play 前强制重载域（设置项“重置失败后强制重载”）。持久登记某次执行失败后仍保留，下次进入 Play 照常执行。
- 重置在 `SubsystemRegistration` 执行。同一阶段内各方法的执行顺序 Unity 不保证：业务自己在该阶段初始化的字段不要再标注 `[ResetStatic]`（检查器把这类类型视为自行重置）。不要为本包调整业务的初始化阶段。
- `[OnExitPlayMode]` 方法所在类型如果有带副作用的静态构造，退出时会被触发。

## 3. 旧代码：统一迁移流程

1. **Tools/Easy Reload Guard/运行检查**：扫描 Assets 与全部包（嵌入式包和注册表包）中的 Player 程序集及预编译 DLL 的静态字段，报告写入 `Library/EasyReloadGuard/AuditReport.md`。不需要检查的包在设置的“排除的包”中添加。
2. 已经使用 Unity 推荐写法（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 或 `[InitializeOnEnterPlayMode]`）的类型，视为自行重置，不再报告。
3. **Tools/Easy Reload Guard/将当前遗留问题写入基线**：生成 `ProjectSettings/EasyReloadGuardBaseline.txt`，提交到版本管理。
4. 在 Project Settings/Easy Reload Guard 中把“进入 Play 前检查”设为 **Block**。之后新增的问题会阻止进入 Play，基线内的遗留问题只做列出。
5. 按报告逐个修复，修复后删除对应基线行。已经不存在的基线条目会以 RG030 提示。判断归属与处理方式见[接入手册](Documentation~/接入手册.md)。

报告的读法：

- 开头的**工作清单**按 来源（Assets / 包 / DLL）→ 程序集 → 类型 汇总未基线的错误与警告，Assets 源码在前、问题多的类型在前。一个类型通常对应一个 owner，按行分工。
- 每条字段问题下面有一行**上下文**，来自只读 IL 分析：初值（类型初始化中是否赋值/填充）、类型初始化外的赋值位置、内容修改位置（集合/数组/对象，启发式）、分析范围。私有字段结论完整；非私有字段、或经非私有属性 setter/事件访问器写入的字段，类型外的写入没有分析。lambda、局部函数与 async 状态机中的访问还原为源方法名。
- 建议按上下文给出：例如只在类型初始化时填充的私有目录降为提示并提醒不要 Clear；有初值又被重新赋值的引用提醒 `[ResetStatic]` 会丢失初值。

| 规则 | 级别 | 含义 |
| --- | --- | --- |
| RG001 | 错误 | 未处理的静态事件/委托 |
| RG002 | 错误 | 未处理的静态 `UnityEngine.Object`（单例常见） |
| RG003 | 警告/提示 | 未处理的可变静态字段；私有且只在类型初始化时赋值（实际不变）为提示，建议改 const/readonly |
| RG004 | 警告/提示 | `readonly` 集合（警告；私有且只在类型初始化时填充为提示）、数组（元素被改写为警告，否则提示）或对象（提示）的内容可能残留 |
| RG005 | 警告 | `async void` 方法：延续会跨 Play 会话执行，需由业务 owner 取消 |
| RG006 | 警告 | 未处理的 `Task`/`ValueTask`/`CancellationTokenSource`/`Thread`/计时器静态字段：异步工作或取消源跨会话存活 |
| RG010 | 错误 | 特性用错（实例字段/属性/事件、readonly 用 Default、引用类型用 InitialValue、async 重置方法等），不可入基线 |
| RG011 | 错误 | `[ReloadSafe]` 没写原因 |
| RG012 | 提示/警告 | 泛型静态字段展开结果 |
| RG013 | 警告 | 已声明的重置会破坏下一会话：`[ResetStatic]` 把只在类型初始化中创建、类型内没有其他赋值位置的引用置 null；`Clear` 清掉类型初始化中填充的内容。可入基线（类型外会重新赋值时） |
| RG014 | 警告 | 类型有 `[ResetMethod]`，但重置方法（含其调用的类型内方法）没有赋值或清理某个可变静态字段；有意保留时用 `[ReloadSafe]`。可入基线 |
| RG020 | 错误 | 适配器找不到类型/成员（目标已升级或改名），不可入基线 |
| RG021 | 警告 | 适配器目标的版本未经复核 |
| RG022 | 提示 | 适配器要求强制重载 |
| RG023 | 警告 | 适配器登记的已知问题：包内经复核会影响运行逻辑的静态状态，可入基线 |
| RG030 | 提示 | 基线条目已失效 |
| RG040 | 警告 | 某个类型无法反射分析，已跳过 |

### 退出 Play 后的残留探测

关闭 Domain Reload 时，每次退出 Play、回到编辑模式后，探测结果写入 `Library/EasyReloadGuard/ResidueReport.md`，发现残留时在 Console 输出 `[Easy Reload Guard]` 警告。它补足元数据与 IL 检查看不到的运行时行为，做 PSP 时可直接定位 owner：

- **已销毁对象的引用**：静态字段、集合元素或回调目标仍指向上一会话已销毁的 `UnityEngine.Object`。
- **跨会话增长**：同一回调方法在引擎/编辑器静态事件（`EditorApplication`、`SceneManager`、`Application`、`Camera`、`Canvas`、`RenderPipelineManager` 等）或项目委托中的数量逐次增加，或集合元素逐次增加。从本域第 2 次回到编辑模式开始比较。
- 项目字段取自最近一次检查报告中未处理（含已基线）的字段；本域没有运行过检查时只探测引擎回调。
- 读取静态字段会触发尚未执行的静态构造，因此默认跳过有静态构造的项目类型，跳过数量写在报告中；设置“残留探测读取有静态构造的类型”可放开（可能在编辑模式执行本次 Play 没用到的类型的静态构造）。
- 设置“退出 Play 后探测残留”可关闭本功能。

## 4. 第三方 UPM 包

- 默认**扫描全部包**，与 Assets 下的代码使用同一套规则。确认不需要检查的包，在设置的“排除的包”中添加包名。
- 处理方式按包的来源选择：公司内部托管的包，直接在包源码中修复；外部官方来源的包保持源码与官方一致，用适配器处理。
- 包内静态状态经逐条复核、关闭 Domain Reload 时不影响 Play 运行逻辑：写一个适配器并调用 `SelfHandled("依据")`，此后忽略该包。复核出的例外用 `KnownIssue(type, member, 说明)` 继续报告为 RG023 警告，不随整包一起忽略。只复核了部分类型时，用 `HandlesType` 逐个类型声明。
- 判断标准是“旧值是否改变下一会话的运行结果”，而不是“有没有静态字段”：确定性缓存/常量、仅编辑器路径、包自身在 Play 边界或 owner 生命周期中重置、使用前判空重建、有意保留的设置，都不影响运行逻辑；只增不删但不被读取的缓存仅造成内存增长，在依据中注明。
- 内置适配器（锁定版本，依据见 `Editor/Adapters/BuiltInAdapters.cs`；版本变化报 RG021 后需重新复核）：

  | 包 | 版本 | 仍报告的已知问题（RG023） |
  | --- | --- | --- |
  | Input System | 1.17.0 | — |
  | Core RP | 17.3.0 | `DebugUpdater`：每次 Play 调用 `EnhancedTouchSupport.Enable()` 不配对 Disable |
  | URP | 17.3.0 | — |
  | uGUI / TextMeshPro | 2.0.0 | `MaterialReferenceManager` 富文本资产缓存；`EventSystem` 废弃覆盖 API；`PanelEventHandler` 修饰键；两个 UI Toolkit picker 未能确认 |
  | Visual Scripting | 1.9.9 | — |
  | Timeline | 1.8.9 | — |
  | Burst | 1.8.26 | — |
  | Collections | 2.6.2 | — |
  | AI Navigation | 2.0.9 | — |
  | Newtonsoft Json | 3.2.2 | — |
  | Unity MCP（com.coplaydev.unity-mcp） | 10.0.0 | — |

- 忽略只覆盖包自身。业务代码订阅包的公开静态事件或全局钩子（如 `TMP_Text.OnFontAssetRequest`、`UniTaskScheduler.UnobservedTaskException`、`EventBus.Register`、`JsonConvert.DefaultSettings`）时，检查器看不到这类订阅，仍须由业务 owner 在退出 Play 时成对解除。
- 预编译 DLL 只扫描对当前构建目标可用的部分；仅编辑器可用的工具库（版本管理、IDE、IL 处理等）不参与 Play 运行逻辑，不扫描。NUnit 与测试程序集同样不扫描。
- 无法改源码时，在任意 Editor 程序集中添加适配器：

```csharp
using EasyReloadGuard;
using EasyReloadGuard.Editor;

sealed class SomeSdkAdapter : ReloadAdapter
{
    public override string Target => "com.vendor.sdk";                    // 包名
    public override string[] VerifiedVersions => new[] { "3.2.1" };      // 人工复核过的版本
    public override void Configure(ReloadAdapterBuilder b)
    {
        b.ResetField("Vendor.Sdk.Core, Vendor.Sdk", "s_Instance");            // 私有字段也可以
        b.ResetField("Vendor.Sdk.Cache, Vendor.Sdk", "s_Map", ResetMode.Clear);
        b.InvokeOnReset("Vendor.Sdk.Core, Vendor.Sdk", "Shutdown");            // 调用其自带的重置/关闭
        b.InvokeOnExitPlayMode("Vendor.Sdk.Net, Vendor.Sdk", "CloseAll");
        b.OnReset("重建默认配置", () => Vendor.Sdk.Config.Apply(Vendor.Sdk.Config.Default));
        b.HandlesType("Vendor.Sdk.Lookup, Vendor.Sdk", "只读查找表，复核于 3.2.1");
    }
}
```

- 目标未安装时，适配器休眠。
- 业务程序集同样可以写适配器：`TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名（asmdef 程序集的 AssemblyVersion 为 `0.0.0.0`）。适配器所在的 Editor 程序集可以直接引用业务程序集，用 `typeof(...)` 写类型名。
- 升级目标包后，如果版本不在 `VerifiedVersions` 中，检查器报 RG021：复核后追加版本即可。成员被改名或删除时报 RG020 错误，不会静默失效。

## 5. DLL

DLL 可以按以下顺序依次尝试，前一种不可行时再用下一种：

1. **先查**：Assets 与包中的预编译 DLL 默认在扫描范围内，可以看出有哪些静态状态。
2. **反射适配**：`TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名，`VerifiedVersions` 填 AssemblyVersion，再用 `ResetField` / `InvokeOnReset` / `InvokeOnExitPlayMode` 处理私有静态状态或调用其自带的清理 API。DLL 更新后版本会变化，触发复核。
3. **确认安全**：`HandlesType(type, 依据)` 或 `SelfHandled(依据)`。
4. **熔断兜底**：`b.RequireDomainReload("原因")`。关闭 Domain Reload 时，每次进入 Play 前会自动重载域，等于只为这个 DLL 恢复了 Domain Reload，正确性优先。
5. **运行时发现异常**：工程自有的 Editor 工具或调试代码调用 `PlaySession.RequestDomainReload("原因")`，下次进入 Play 前重载。不要为此在业务流程中新增检测分支。

尚未提供的能力：IL 改写（如 Harmony 或 Cecil 修改 DLL 的静态构造）。它侵入性强、随版本变化很脆弱，等确有需要时再单独评估。

## 6. 强制重载

| 入口 | 行为 |
| --- | --- |
| Tools/Easy Reload Guard/立即重载域 | Play 中会先退出，然后 `EditorUtility.RequestScriptReload()` |
| Tools/Easy Reload Guard/重载域并进入 Play | 重载完成后自动进入 Play |
| `ForceReload.RequestBeforeNextPlay(reason)`（Editor） | 下次进入 Play 时先取消，重载后再进入 |
| `PlaySession.RequestDomainReload(reason)`（运行时，Player 中调用会被编译器移除） | 同上 |
| 重置/释放失败、适配器 `RequireDomainReload` | 自动登记 |

如果本域还没有进入过 Play（刚编译或刚重载过），说明域已经是新的，待处理的重载请求会被直接满足，不会重复重载。

## 7. 离线检查（CI）

```bash
Unity -batchmode -nographics -projectPath <项目> -executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit -easyReloadGuardReport Build/ReloadGuardReport.md -easyReloadGuardFailOn error
```

退出码：0 表示通过，1 表示存在未入基线的问题，2 表示检查本身失败。不要同时传 `-quit`。

工程仓库自带流水线脚本 `Tools/UnityCI/Run-UnityTests.ps1`，它依次运行 batchmode 测试和本检查。工程正被编辑器打开时，脚本会先把 Assets、Packages、ProjectSettings 和 Library/PackageCache 镜像到 `%TEMP%\EasyReloadGuardCI` 再运行，不影响正在使用的编辑器：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityCI/Run-UnityTests.ps1
```

## 8. 升级 Unity 的检查清单

插拔边界：业务代码只在已有成员上使用 `EasyReloadGuard` 的特性，另有只在 Editor 编译的重置方法。卸载本包时，删除这些特性、`#if UNITY_EDITOR` 的重置方法、对应的 `using EasyReloadGuard;` 与 asmdef 引用，以及引用本包的 Editor 适配器即可，业务方法与流程不需要改回，Player 行为不变。

1. 复核 `Editor/Core/EditorCompat.cs`：Enter Play Mode 设置读取、在 ExitingEditMode 中取消进入 Play、`RequestScriptReload`、包版本查询。所有版本敏感的调用都集中在这个文件。
2. 运行 `Tools/UnityCI/Run-UnityTests.ps1`（或在 Test Runner → EditMode 中运行 `EasyReloadGuard.Editor.Tests` 与 `DomainReloadCases.Tests`）。嵌入式包的测试默认可见，不需要配置 `testables`。
3. 运行检查，处理 RG020/RG021（内置适配器对应的包可能也随 Unity 升级了）。
4. 关闭 Domain Reload，做至少 3 次连续 Play→Stop→Play，确认 Console 中没有 `[Easy Reload Guard]` 错误。
5. 如果新版本 Unity 提供了官方的同类机制，用适配器或替换 `ResetPipeline` 的执行时机即可，业务特性保持不变。

## 9. 已知限制

- `InitialValue` 记录的是“本域首次在关闭 Domain Reload 时进入 Play”那一刻的值。若本域此前已运行过 Play（例如先在开启 Domain Reload 时 Play 过，再切换为关闭），关口会先强制重载一次，以便拿到干净的初值；但编辑模式下的代码（ExecuteAlways、编辑器工具）在此之前对字段的修改仍无法识别。
- 在 ExitingEditMode 中通过 `EditorApplication.isPlaying = false` 取消进入 Play：已在 6000.3.1f1 上实测有效。取消时 Unity 会同步触发一次 `ExitingPlayMode`，所以退出释放步骤会多执行一次；释放方法应保证幂等。升级 Unity 后需要重新验证。
- 延迟执行统一使用 `EditorCompat.RunOnNextUpdate`，不用 `EditorApplication.delayCall`。6000.3.1f1 实测：编辑器在后台、由外部工具（Unity Pipeline 的 autotick）驱动时，`update` 照常执行，但 `delayCall` 不会被调用，会导致"取消后重载并进入 Play"卡住。
- Unity 6.3 中 `EditorSettings.enterPlayModeOptionsEnabled` 实际由 options 推导，不能独立设置：
  - 先设 enabled、再设 `options = None`，enabled 会被改回 false；
  - 对 `None` 设 `enabled = true`，options 会变成"两项都关闭"；
  - 磁盘上旧版的 `1/0` 组合无法通过 API 原样写回。

  需要修改设置时，先写 options，再写 `enabled = options != None`。本包只读这两项，不受影响。
- 检查读取反射元数据与声明类型内的方法体 IL（只读，不执行、不改写）。类型外对非私有字段的写入、经局部变量转手或参数中夹杂同类型调用的集合修改、反射写入都不在分析范围内；上下文中“范围”一项标明结论是否完整。
- 检查无法判断 `await` 之后写入的状态是否已失效、lambda 形式的 `async void`、`ConfigureAwait(false)` 后在线程池上的写入；这些依靠业务按“异步与后台工作”的规范处理，并以 PSP 与残留探测验证。
- 检查无法识别“静态构造中订阅了外部事件”这类行为。这类订阅需要由 owner 已有的释放入口（标注 `[OnExitPlayMode]`）或 Editor 适配器解除；连续 Play 时残留探测会把订阅的逐次增长报告出来。
- 残留探测默认跳过有静态构造的项目类型（大多数带初值的集合都在其中），只读字段值，不分析引用链；“未发现残留”不等于没有残留。
- 关闭 Scene Reload 带来的问题（对象不重建）不在本包范围内。
- UniTask 2.5.11：`PlayerLoopHelper.InsertRunner` 每次初始化都向 `EditorApplication.playModeStateChanged` 追加一组匿名处理器（每个 timing 一个，共 16 个）且从不移除，关闭 Domain Reload 时每次进入 Play 累积一组，旧处理器持有旧 runner 和队列。本包不内置 UniTask 适配器：检查器看不到挂在 UnityEditor 事件上的订阅，也不能把它声明为整包已处理。应在 UniTask 源码中修复（内部托管版本）；测试工程中的临时做法见 `Assets/ReloadGuardProjectAdapters`。
- 测试程序集（引用 nunit）中的特性和适配器不会进入项目的重置计划和检查。
