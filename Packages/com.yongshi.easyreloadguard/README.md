# Easy Reload Guard

在 Unity 中关闭 Domain Reload（Project Settings → Editor → Enter Play Mode Settings）可以让进入 Play 快很多，代价是：**静态变量不会在每次 Play 时恢复初值**。本包帮你做三件事：

1. **找出来**：扫描项目和包里的静态字段，列出哪些会把上一次 Play 的数据带到下一次。
2. **恢复它**：在静态字段上加特性，进入 Play 时自动恢复；停止 Play 时调用你指定的释放方法。
3. **验证它**：每次停止 Play 后自动检查，有没有字段还引用已销毁的对象、有没有订阅或集合每次 Play 都在增加。

处理某份检查报告时，请看[接入手册](Documentation~/接入手册.md)：其中说明了每类问题该谁改、怎么改，以及改完后如何证明。版本变化和验证记录见 [CHANGELOG](CHANGELOG.md)。

用 Agent 写代码时，在 `Project Settings/Easy Reload Guard` 的“Agent Skill”分组中安装 Skill（或菜单 `Tools/Easy Reload Guard/安装或更新 Agent Skill`），Agent 会按本包的约束处理静态状态。Agent 定位本包代码见 [AGENTS.md](AGENTS.md)。

---

## 1. 关闭 Domain Reload 后会发生什么

开启 Domain Reload 时，每次进入 Play，Unity 都会重新加载全部脚本，静态变量随之回到初值。关闭后不再重新加载，因此：

| 现象 | 例子 |
| --- | --- |
| 静态字段保留上一次 Play 的值 | 分数、计数器、“已初始化”标记没有归零 |
| 单例指向已销毁的对象 | `GameManager.Instance` 不是真 null，但对象已经销毁 |
| 静态事件的订阅还在 | 上一次 Play 的监听器被重复回调，或回调到已销毁的对象 |
| 静态集合不断累积 | 注册表、缓存每次 Play 都多出一批 |
| 异步任务继续运行 | 停止 Play 后，`await` 之后的代码仍会执行，并把结果写进下一次 Play |

Player（打包后的游戏）每次启动都会重新加载全部脚本，不会出现这些问题。所以本包**只在 Editor 中工作**。

## 2. 三条约束

1. **不影响打包后的逻辑**：运行时程序集 `EasyReloadGuard` 只包含特性，以及 `PlaySession` 上几个带 `[Conditional("UNITY_EDITOR")]` 的方法（Player 中调用会被编译器删除）。Player 中没有本包的任何状态、启动入口或回调。
2. **业务代码只做两类修改**：
   - 在**已有的**字段、属性、事件、方法上加特性（因此需要引用 `EasyReloadGuard` 程序集：Assembly-CSharp 自动引用，使用 asmdef 的程序集需手动添加引用）。
   - 新增**只在 Editor 编译**的重置方法：方法连同 `[ResetMethod]` 整体放在 `#if UNITY_EDITOR` 中，只把本类型的静态字段恢复成刚加载时的样子（写法见[接入手册](Documentation~/接入手册.md)第 4 节）。

   除此之外，不要为本包在业务中新增方法、Unity 消息、参数、登记调用或判断分支。无法修改的第三方代码放到 Editor 适配器中处理（第 5 节）。
3. **异步由业务自己取消**：异步任务、后台循环和延迟返回的结果，不能比发起它的对象活得更久。这在 Player 中切场景、返回主菜单、登出时同样是必要的，属于业务本身的正确性，不算“为本包改业务”。本包不替业务取消任务，只在检查报告中提示风险（见第 3 节“异步任务”）。

## 3. 用特性处理静态状态

```csharp
using EasyReloadGuard;

public class GameManager : MonoBehaviour
{
    [ResetStatic] public static GameManager Instance;                 // 进入 Play 时置 null
    [ResetStatic(ResetMode.InitialValue)] static float s_Speed = 1f;  // 恢复为 1
    [ResetStatic(ResetMode.Clear)] static readonly List<Enemy> s_All = new List<Enemy>(); // 调用 Clear()，保留集合本身
    [field: ResetStatic] public static event Action<int> ScoreChanged; // 事件和自动属性用 field: 目标
    [field: ResetStatic] public static int Score { get; private set; }
}
```

| 静态状态 | 写法 |
| --- | --- |
| 单例、普通静态字段 | `[ResetStatic]`：引用置 null，数值归零 |
| 有非默认初值的数值或 string | `[ResetStatic(ResetMode.InitialValue)]`：恢复为第一次进入 Play 时的值 |
| `static readonly` 集合 | `[ResetStatic(ResetMode.Clear)]` |
| 静态事件、自动属性 | `[field: ResetStatic]` |
| 几个字段需要一起恢复（对象池、注册表等） | 在类型中写一个静态无参方法并加 `[ResetMethod(order)]`。加了之后，整个类型都由这个方法负责，检查器会逐个核对类型中的字段是否都已处理。没有现成方法时，可以新增一个只在 Editor 编译的方法（见接入手册第 4 节） |
| 订阅、线程、计时器、Socket、Native 内存 | 在业务**已有的**停止或释放方法（如 `Stop()`）上加 `[OnExitPlayMode(order)]`，停止 Play 时调用。没有现成方法时，在 Editor 适配器中处理，不要为此新建方法 |
| 有意保留到下一次 Play 的状态 | `[ReloadSafe("原因")]`，可加在字段、类型或程序集上，原因必填。加在字段或类型上之后，如果字段仍在 Play 中被修改，检查器会提醒你重新确认原因 |
| 泛型单例 `Singleton<T>.s_Instance` | 在泛型定义的字段上加 `[ResetStatic]`，会自动展开到所有派生类用到的具体类型。业务直接使用的具体类型（如 `EventBus<EnemyKilled>`），需要在 Editor 适配器中补充：`b.ResetField(typeof(EventBus<EnemyKilled>).AssemblyQualifiedName, "s_Handlers")` |
| ScriptableObject 等 `OnEnable` 只执行一次的实例状态 | 特性只处理静态成员。在 Editor 适配器中用 `OnReset` 找到已加载的实例，调用它已有的入口（例如重新执行 `OnEnable`，效果等同开启 Domain Reload）。不要在业务的 `OnEnable` 中加登记代码 |
| 异步任务、后台循环 | 由发起它的业务代码负责取消，见下文。业务已有的停止方法可以加 `[OnExitPlayMode]` |

注意 `ResetMode.Default` 的陷阱：`static float s_Speed = 1f` 加 `[ResetStatic]` 后会被重置为 0。第一次进入 Play 时，如果发现字段的值不是默认值，Console 会给出警告。

### 异步任务

开启 Domain Reload 时，进入 Play 会卸载全部脚本，上一次 Play 中挂起的 `await` 随之失效。关闭后，它们会在下一次 Play（或编辑模式）中继续执行，上一次 Play 的结果可能写进下一次 Play。

- 每个异步任务都要有明确的发起者和结束时机：MonoBehaviour 发起的任务用 `destroyCancellationToken`；跨场景的服务用自己的 `CancellationTokenSource`，在服务的停止方法中取消（该方法可加 `[OnExitPlayMode]`）；只需在 Play 结束时取消的任务用 `Application.exitCancellationToken`（停止 Play 时取消，下次进入时重建。该令牌在关闭 Domain Reload 时的行为尚未在本工程实测，以连续 Play → Stop 的结果为准）。
- 令牌在发起任务时读取，并沿调用链传递（`Task.Delay(ms, token)`、支持取消的 I/O）。不要把令牌存进静态字段。
- `await` 之后写共享状态前，先确认令牌未取消。被取消的任务以取消结束，不要当作成功处理，也不要吞掉异常。
- 避免 `async void`。Unity 消息方法（如 `async void Start()`）不得不用时，在内部传递取消令牌并处理异常。线程和线程池计时器要在停止方法中 `Dispose` 或结束；只把字段置 null 不会停止已在运行的工作。

```csharp
public static class ProfileLoader
{
    [ResetStatic] public static string CurrentProfile { get; private set; }

    public static async Task LoadAsync(Func<CancellationToken, Task<string>> fetch)
    {
        var token = Application.exitCancellationToken;   // 发起时读取：属于本次 Play
        CurrentProfile = null;
        var profile = await fetch(token);
        token.ThrowIfCancellationRequested();              // Play 已停止：以取消结束，不写回
        CurrentProfile = profile;
    }
}
```

检查器会报告 `async void` 方法，以及类型为 `Task`、`ValueTask`、`CancellationTokenSource`、`Thread`、计时器且未处理的静态字段。它只看代码结构，无法判断 `await` 之后写入的状态是否已经失效，最终要以连续 Play → Stop 的结果为准。

## 4. 处理已有项目

1. **运行检查**：菜单 `Tools/Easy Reload Guard/检查静态变量并查看结果`，结果显示在设置页 `Project Settings/Easy Reload Guard`。扫描 Assets 和全部包（含预编译 DLL）中参与打包的代码，报告写入 `Library/EasyReloadGuard/AuditReport.md`。不需要检查的包，在设置的“排除的包”中添加。只在自己电脑上安装、不随项目提交的插件，用下面的排除标记，不要写进共享设置。
2. 已经使用 Unity 推荐写法（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 或 `[InitializeOnEnterPlayMode]`）自行重置的类型，不再报告。
3. **记录已知遗留**：看过检查结果后，点设置页中的“把当前问题记为已知遗留”，把当前所有问题写入 `ProjectSettings/EasyReloadGuardBaseline.txt`，并提交到版本管理。
4. **阻止新问题**：在 `Project Settings/Easy Reload Guard` 中把“进入 Play 前检查”设为 **阻止进入 Play**。之后新出现的错误会阻止进入 Play，已知遗留的问题只列出、不阻止。
5. **逐个修复**：按报告修复问题，修复后删除清单中对应的行。已修复但没有删除的行，报告会提示“遗留问题清单中的过期行”。判断方法见[接入手册](Documentation~/接入手册.md)。

### 检查报告怎么看

报告从上到下分为：

1. **概况**：Unity 版本、Enter Play Mode 设置、待处理的错误/警告/提示数量，以及已知遗留的数量。
2. **第三方包与 DLL 的适配器**：每个适配器处理哪个包、已安装版本是否经过复核、为什么不需要处理。
3. **按类型汇总**：按来源（Assets / 包 / DLL）→ 程序集 → 类型列出待处理的错误和警告，Assets 源码在前，问题多的类型在前。一个类型的静态状态通常由同一段代码负责，可以按行分工。
4. **错误 / 警告 / 提示 / 已知遗留**：每个级别下按问题类型分小节，每条写明是哪个成员、为什么有问题、建议怎么改。
5. 每条字段问题下方还有一行**读写位置**，来自对编译后代码的只读分析：
   - 初值：是否在字段初值或静态构造中赋值。
   - 赋值 / 内容修改：静态初始化以外，哪些方法给字段赋值、修改集合或数组元素、调用对象方法。
   - 范围：私有字段的结论完整；非私有字段只统计了所在类型内部，类型外的写入需要自己搜索。lambda、局部函数和 async 方法中的访问会还原为所在的源方法名。

问题类型：

| 问题类型 | 级别 | 含义 | 能否记为已知遗留 |
| --- | --- | --- | --- |
| 静态事件或委托未处理 | 错误 | 订阅会带到下一次 Play | 能 |
| 静态 Unity 对象未处理 | 错误 | 下一次 Play 时指向已销毁的对象，单例常见 | 能 |
| 静态字段的值会带到下一次 Play | 警告 / 提示 | 私有且只在静态初始化时赋值（实际不会变）的降为提示，建议改为 const 或 readonly | 能 |
| 只读集合、数组或对象的内容会带到下一次 Play | 警告 / 提示 | 只在静态初始化时填充的私有集合为提示 | 能 |
| async void 方法 | 警告 | 停止 Play 后仍会继续执行 | 能 |
| 静态字段持有异步任务、线程或计时器 | 警告 | 停止 Play 后仍在运行 | 能 |
| 特性用法错误 | 错误 | 如加在实例成员上、readonly 字段用 Default、引用类型用 InitialValue、重置方法是 async | 不能，必须修 |
| [ReloadSafe] 没写原因 | 错误 | 原因必填 | 不能，必须修 |
| 泛型静态字段 | 提示 / 警告 | 泛型字段展开到了哪些具体类型；一个都没找到时为警告 | 能 |
| 已加的重置会破坏下一次 Play | 警告 | 例如把只在静态初始化中创建的对象置 null，或者把静态初始化中填好的集合 Clear 掉 | 能（确认类型外会重新赋值时） |
| 重置方法漏掉了字段 | 警告 | 类型有 `[ResetMethod]`，但某个字段没有被它处理 | 能 |
| 声明保留的字段仍在 Play 中被修改 | 警告 | 加了 `[ReloadSafe]` 的字段在静态初始化以外仍被修改，需要确认原因是否还成立。清单中会记下修改的位置，位置增减后需要重新确认 | 能 |
| 适配器找不到目标 | 错误 | 适配器引用的类型或成员不存在（目标升级或改名） | 不能，必须修 |
| 适配器目标的版本未复核 | 警告 | 只用于本包内置的适配器：已安装的官方包版本不在复核列表中 | 不能，升级本包或联系本包维护者 |
| 跳过检查的代码中静态字段有变化 | 警告 | 项目适配器用 `HandlesType` / `SelfHandled` 跳过的类型，静态字段与上次记为已知遗留时不同（第一次检查时也会出现） | 能（复核后记为已知遗留） |
| 适配器要求进入 Play 前重新加载脚本 | 提示 | 适配器声明了兜底重新加载 | — |
| 第三方代码的已知问题 | 警告 | 适配器复核出的、会影响运行的包内静态状态 | 能 |
| 遗留问题清单中的过期行 | 提示 | 清单中的某行已没有对应问题，可以删除 | — |
| 无法分析的类型 | 警告 | 某个类型无法反射，已跳过 | — |

### 已知遗留问题清单

`ProjectSettings/EasyReloadGuardBaseline.txt` 记录了“已经知道、暂时不修”的问题，作用是让“阻止进入 Play”只拦截**新出现的**问题：

- 文件由菜单生成，按问题类型分组，每组上方有注释说明这是什么问题。每行的格式为 `规则编号|程序集|类型.成员`，由工具比对，不要手工添加新行（编号对照见文末附录）。
- 清单中的问题仍然列在报告末尾的“已知遗留”中，只是不阻止进入 Play。
- 修复一个问题后删除对应行；重新生成会覆盖整个文件。

### 停止 Play 后检查

关闭 Domain Reload 时，每次停止 Play 回到编辑模式后，会自动检查一次，结果写入 `Library/EasyReloadGuard/ResidueReport.md`；发现问题时 Console 输出 `[Easy Reload Guard]` 警告。它能发现静态检查看不到的运行时问题，连续 Play → Stop 时可以直接定位到负责的代码：

- **仍引用已销毁的对象**：静态字段、集合元素或回调的目标，仍指向上一次 Play 已销毁的 `UnityEngine.Object`。
- **比上次停止时增加**：同一个回调方法在引擎或编辑器的静态事件（`EditorApplication`、`SceneManager`、`Application`、`Camera`、`Canvas`、`RenderPipelineManager` 等）或项目的委托中出现的次数增加了，或者集合元素增加了。从本次脚本加载后的第 2 次停止开始比较。
- 检查的项目字段来自最近一次检查报告中未处理的字段（含已知遗留）。本次脚本加载后还没运行过检查时，只检查引擎回调。
- 读取静态字段会触发尚未执行过的静态构造。只创建集合、字符串、数值的静态构造照常读取；其他有静态构造的项目类型默认跳过，报告分别列出“跳过”和“无法读取”的数量。设置“停止后检查也读取有静态构造的类型”可以放开。
- 设置“停止 Play 后检查残留”可以关闭本功能。

## 5. 第三方包和 DLL

- 默认**扫描全部包**，规则与 Assets 相同。确认不需要检查的包，在设置的“排除的包”中添加。
- **个人本机插件用排除标记**：在插件根目录放一个名为 `.easyreloadguard-exclude` 的文件，这个目录及子目录里的程序集都不检查（有 asmdef 的源码程序集和 DLL；没有 asmdef 的代码会编进 `Assembly-CSharp`，不能用标记单独跳过）。文件内容可选，写一句原因，例如“本机个人插件，不随项目”，会显示在报告里。查找从程序集的 asmdef 或 DLL 所在目录逐级向上，到包根目录（含 `package.json`）或工程根目录为止，放在工程根目录的标记不生效。文件名以点开头，Unity 不导入、不生成 `.meta`；放好后运行一次检查即可生效。被跳过的程序集列在报告开头和设置页，不会悄悄消失；它们的静态状态不再检查，需要自己负责。
- 公司内部维护的包，直接修改包源码；外部官方来源的包保持源码不变，用适配器处理。
- 判断标准是“上一次 Play 留下的值会不会改变下一次 Play 的结果”，而不是“有没有静态字段”。以下情况都不影响结果：确定性的缓存和常量、只在编辑器中使用的代码、包自己会在 Play 开始/结束或对象销毁时重置、使用前判空重建、有意保留的设置。只增不删但不会被读取的缓存只会增加内存，在依据中注明即可。
- 包内的静态状态逐条复核、确认不影响运行后，写一个适配器并调用 `SelfHandled("依据")`，此后这个包不再检查。复核中发现的例外用 `KnownIssue(类型, 成员, 说明)` 继续报告为警告。只复核了部分类型时，用 `HandlesType` 逐个声明。

内置适配器（版本固定，复核依据见 `Editor/Adapters/BuiltInAdapters.cs`；已安装版本不在列表中时，报告“适配器目标的版本未复核”）：

| 包 | 已复核版本 | 仍报告的已知问题 |
| --- | --- | --- |
| Input System | 1.17.0、1.20.0 | — |
| Core RP | 17.3.0 | `DebugUpdater`：每次 Play 调用 `EnhancedTouchSupport.Enable()`，但从不调用 Disable |
| URP | 17.3.0 | — |
| uGUI / TextMeshPro | 2.0.0 | `MaterialReferenceManager` 富文本资产缓存；`EventSystem` 已废弃的覆盖 API；`PanelEventHandler` 修饰键；两个 UI Toolkit picker 未能确认 |
| Visual Scripting | 1.9.9 | — |
| Timeline | 1.8.9 | — |
| Burst | 1.8.26 | — |
| Collections | 2.6.2、2.6.3 | — |
| AI Navigation | 2.0.9、2.0.11 | — |
| Newtonsoft Json | 3.2.1、3.2.2 | — |
| Unity MCP（com.coplaydev.unity-mcp） | 10.0.0 | — |

- 适配器只覆盖包自身。业务代码订阅包的公开静态事件或全局钩子（如 `TMP_Text.OnFontAssetRequest`、`UniTaskScheduler.UnobservedTaskException`、`EventBus.Register`、`JsonConvert.DefaultSettings`）时，检查器看不到这些订阅，仍须由业务代码在停止时成对取消。
- 预编译 DLL 只扫描当前构建目标可用的部分。只在编辑器中使用的工具库（版本管理、IDE、IL 处理等）不参与运行，不扫描；NUnit 和测试程序集也不扫描。

无法修改源码时，在任意 Editor 程序集中写一个适配器：

```csharp
using EasyReloadGuard;
using EasyReloadGuard.Editor;

sealed class SomeSdkAdapter : ReloadAdapter
{
    public override string Target => "com.vendor.sdk";                    // 包名
    public override string[] VerifiedVersions => new[] { "3.2.1" };      // 复核时的版本，只作记录，可以不写
    public override void Configure(ReloadAdapterBuilder b)
    {
        b.ResetField("Vendor.Sdk.Core, Vendor.Sdk", "s_Instance");            // 私有字段也可以
        b.ResetField("Vendor.Sdk.Cache, Vendor.Sdk", "s_Map", ResetMode.Clear);
        b.InvokeOnReset("Vendor.Sdk.Core, Vendor.Sdk", "Shutdown");            // 进入 Play 时调用它自带的重置或关闭方法
        b.InvokeOnExitPlayMode("Vendor.Sdk.Net, Vendor.Sdk", "CloseAll");      // 停止 Play 时调用
        b.OnReset("重建默认配置", () => Vendor.Sdk.Config.Apply(Vendor.Sdk.Config.Default));
        b.HandlesType("Vendor.Sdk.Lookup, Vendor.Sdk", "只读查找表，复核于 3.2.1");
    }
}
```

- 目标包没有安装时，适配器不生效。
- 也可以给业务程序集写适配器：`TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名（asmdef 程序集的版本为 `0.0.0.0`）。适配器所在的 Editor 程序集可以直接引用业务程序集，用 `typeof(...)` 写类型名。
- **包更新后怎么发现要重新复核**：项目适配器不比对版本号（基于包开发的项目包更新频繁，又常用 Git tag 发版而不改 `package.json`，版本号既会在无关更新时误报，又会在不改版本时漏报）。按包更新后的情况分别处理：
  - `ResetField`、`InvokeOnReset`、`InvokeOnExitPlayMode`、`KnownIssue` 引用的类型或成员被改名、删除：报“适配器找不到目标”错误，必须修。
  - 用 `HandlesType`、`SelfHandled` 跳过检查的代码：检查器记录其中每个类型的静态字段（名称、类型、是否只读），报“跳过检查的代码中静态字段有变化”警告。第一次出现时，确认这些字段不影响下一次 Play 后记为已知遗留；之后字段有增删或类型变化，原来那一行失效，报告列出与上次记录相比新增、删除的字段，只需复核这几个。包更新没有动到静态字段时，什么都不报。
  - 只比对字段定义，不比对方法体：字段没变、读写方式变了的情况，依靠停止后检查发现。
  - 其余没有声明的代码每次都会重新扫描，新问题照常报告。
- `VerifiedVersions` 只作记录，可以不写。内置适配器针对 Unity 官方发布的包，仍按 `package.json` 版本比对；版本不在列表中时报“适配器目标的版本未复核”，需要升级本包或联系本包维护者。

DLL 按以下顺序尝试，前一种不可行时再用下一种：

1. **先看报告**：DLL 默认在扫描范围内，报告会列出其中的静态状态。
2. **写适配器**：`TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名，再用 `ResetField` / `InvokeOnReset` / `InvokeOnExitPlayMode` 处理私有静态字段或调用它自带的清理方法。DLL 更新后成员没了会报错。
3. **确认不需要处理**：`HandlesType(类型, 依据)` 或 `SelfHandled(依据)`。DLL 更新后静态字段有变化时会提醒重新复核。
4. **兜底**：`b.RequireDomainReload("原因")`。关闭 Domain Reload 时，每次进入 Play 前都会先重新加载脚本，相当于只为这个 DLL 恢复了 Domain Reload。
5. **运行中发现问题**：项目自己的 Editor 工具或调试代码调用 `PlaySession.RequestDomainReload("原因")`，下次进入 Play 前重新加载脚本。不要为此在业务流程中加判断。

尚未提供的能力：修改 DLL 的代码（如用 Harmony 或 Cecil 改写静态构造）。侵入性强，又容易随版本失效，确有需要时再单独评估。

## 6. 进入和停止 Play 时的执行顺序（关闭 Domain Reload 时）

```
进入 Play（RuntimeInitializeLoadType.SubsystemRegistration 阶段）
  PlaySession.RegisterReset 登记的动作（按登记顺序，执行后移除）
  → PlaySession.RegisterPersistentReset 登记的动作（按登记顺序，每次都执行，直到解除或登记方对象销毁）
  → [ResetStatic] 字段（按 类型.字段名 排序）
  → [ResetMethod] 和适配器的重置（按 order，相同时按名称）

停止 Play（ExitingPlayMode，开启 Domain Reload 时也执行）
  [OnExitPlayMode] 和适配器的释放（按 order）
```

- 某一步失败只记录异常，不影响后续步骤。失败会汇总成一条错误，并默认在下次进入 Play 前重新加载脚本（设置“重置失败后重新加载脚本”）。持久登记的动作某次失败后仍然保留。
- 同一阶段内各方法的执行顺序由 Unity 决定，不保证先后。业务自己在 `SubsystemRegistration` 阶段初始化的字段，不要再加 `[ResetStatic]`（检查器把这类类型视为自行重置）。不要为本包调整业务的初始化阶段。
- `[OnExitPlayMode]` 方法所在的类型如果有带副作用的静态构造，停止 Play 时会触发它。

## 7. 重新加载脚本（兜底）

| 入口 | 行为 |
| --- | --- |
| `Tools/Easy Reload Guard/立即重新加载脚本` | Play 中会先停止，再调用 `EditorUtility.RequestScriptReload()` |
| `Tools/Easy Reload Guard/重新加载脚本并进入 Play` | 重新加载完成后自动进入 Play |
| 主工具栏 Play 按钮旁的“重载并 Play” | 同上；右键可只重新加载脚本或打开设置页。第一次加载本包时自动显示（每人每工程一次），之后可在工具栏右键菜单 `Easy Reload Guard` 中隐藏或重新显示 |

主工具栏上还有一个 Domain Reload 状态图标：**绿色闪电**表示 Domain Reload 已关闭（进入 Play 快），**黄色闪电**表示已开启（常规，每次进入 Play 都重新加载脚本）。按 Unity 实际使用的 `EditorSettings.asset` 序列化开关判断；接口与序列化开关不一致时显示黄色，悬停提示中说明原因。设置改变后约 1 秒内刷新。点击打开 Enter Play Mode 设置，右键打开本包设置；显示和隐藏方式与“重载并 Play”相同。
| `ForceReload.RequestBeforeNextPlay(reason)`（Editor） | 下次进入 Play 时先取消，重新加载后再进入 |
| `PlaySession.RequestDomainReload(reason)`（运行时代码中调用，Player 中会被编译器删除） | 同上 |
| 重置或释放失败、适配器 `RequireDomainReload` | 自动登记 |

如果本次脚本加载后还没进入过 Play（刚编译或刚重新加载），脚本已经是干净的，待处理的请求直接视为完成，不会重复加载。

## 8. 命令行检查（CI）

```bash
Unity -batchmode -nographics -projectPath <项目> -executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit -easyReloadGuardReport Build/ReloadGuardReport.md -easyReloadGuardFailOn error
```

退出码：0 表示通过，1 表示存在不在遗留清单中的问题，2 表示检查本身失败。不要同时传 `-quit`。

本工程自带流水线脚本 `Tools/UnityCI/Run-UnityTests.ps1`，依次运行 batchmode 测试和检查。工程正被编辑器打开时，脚本会先把 Assets、Packages、ProjectSettings 和 Library/PackageCache 复制到 `%TEMP%\EasyReloadGuardCI` 再运行，不影响正在使用的编辑器：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityCI/Run-UnityTests.ps1
```

## 9. 升级 Unity 与卸载本包

卸载：删除业务代码中的特性、`#if UNITY_EDITOR` 中的重置方法、对应的 `using EasyReloadGuard;` 和 asmdef 引用，以及引用本包的 Editor 适配器即可。业务方法和流程不需要改回，Player 行为不变。

升级 Unity 后：

1. 复核 `Editor/Core/EditorCompat.cs`：所有和 Unity 版本相关的调用（读取 Enter Play Mode 设置及其序列化字段、在 ExitingEditMode 中取消进入 Play、`RequestScriptReload`、查询包版本）都集中在这个文件。
2. 运行 `Tools/UnityCI/Run-UnityTests.ps1`（或在 Test Runner → EditMode 中运行 `EasyReloadGuard.Editor.Tests` 和 `DomainReloadCases.Tests`）。嵌入式包的测试默认可见，不需要配置 `testables`。
3. 运行检查，处理“适配器找不到目标”“跳过检查的代码中静态字段有变化”和“适配器目标的版本未复核”（内置适配器对应的包可能也随 Unity 升级了）。
4. 关闭 Domain Reload，连续 Play → Stop 至少 3 次，确认 Console 中没有 `[Easy Reload Guard]` 错误。
5. 新版 Unity 如果提供了官方的同类机制，用适配器接入或替换 `ResetPipeline` 的执行时机即可，业务代码上的特性保持不变。

## 10. 已知限制

- `InitialValue` 记录的是“本次脚本加载后，第一次在关闭 Domain Reload 时进入 Play”那一刻的值。如果此前已经运行过 Play（例如先在开启 Domain Reload 时 Play 过，再切换为关闭），进入 Play 前会先重新加载一次脚本，以便拿到干净的初值；但编辑模式下的代码（ExecuteAlways、编辑器工具）在此之前对字段的修改无法识别。
- 在 ExitingEditMode 中通过 `EditorApplication.isPlaying = false` 取消进入 Play：已在 6000.3.1f1 上实测有效。取消时 Unity 会同时触发一次 `ExitingPlayMode`，所以释放方法会多执行一次，释放方法应可重复调用。升级 Unity 后需要重新验证。
- 延迟执行统一使用 `EditorCompat.RunOnNextUpdate`，不用 `EditorApplication.delayCall`。6000.3.1f1 实测：编辑器在后台、由外部工具驱动时，`update` 照常执行，但 `delayCall` 不会被调用，会导致“取消后重新加载并进入 Play”卡住。
- Unity 6.3 中 `EditorSettings.enterPlayModeOptionsEnabled` 由 options 推导，不能单独设置：
  - 先设 enabled、再设 `options = None`，enabled 会被改回 false；
  - 对 `None` 设 `enabled = true`，options 会变成“两项都关闭”；
  - 磁盘上旧版的 `1/0` 组合无法通过 API 原样写回。

  需要修改设置时，先写 options，再写 `enabled = options != None`。
- 6000.3.1f1 实测：接口读到的 `enterPlayModeOptionsEnabled` 只由 options 推导，写入也不会改动 `EditorSettings.asset` 中的 `m_EnterPlayModeOptionsEnabled`；而进入 Play 时 Unity 按这个序列化字段决定。字段为 false 时，接口显示已关闭 Domain Reload，脚本却照样重新加载。本包以序列化字段为准判断是否关闭了 Domain Reload；两者不一致时，设置页和检查报告会提示，进入 Play 时 Console 也会输出警告。用脚本切换时，要用 `SerializedObject` 同时写入该字段，结束后清除脏标记。
- 检查只读取反射信息和所在类型内方法的编译后代码，不执行、不修改。类型外对非私有字段的写入、经局部变量转手的集合修改、反射写入都不在分析范围内；“读写位置”中的“范围”一项会标明结论是否完整。
- 检查无法判断 `await` 之后写入的状态是否已失效，也看不到 lambda 形式的 `async void`、`ConfigureAwait(false)` 之后在线程池上的写入。这些依靠业务按第 3 节处理，并用连续 Play → Stop 和停止后检查验证。
- 检查看不到“静态构造中订阅了外部事件”。这类订阅需要由负责的代码在已有的释放方法中取消（加 `[OnExitPlayMode]`），或在 Editor 适配器中处理；连续 Play 时，停止后检查会报告订阅次数的增加。
- 停止后检查默认跳过静态构造有副作用的项目类型（调用 UnityEngine、业务代码或读取其他类型静态字段的初始化都算有副作用），只读字段的值，不追踪引用链。“没有发现残留”不等于没有残留。
- 关闭 Scene Reload 带来的问题（场景对象不重建）不在本包范围内。
- UniTask 2.5.11：`PlayerLoopHelper.InsertRunner` 每次初始化都向 `EditorApplication.playModeStateChanged` 追加一组处理器（每个时机一个，共 16 个），且从不移除。关闭 Domain Reload 时，每次进入 Play 都会多一组，旧处理器还持有旧的 runner 和队列。本包不内置 UniTask 适配器：检查器看不到挂在 UnityEditor 事件上的订阅，也不能把整个包声明为已处理。应在 UniTask 源码中修复（内部维护的版本）；本测试工程中的临时做法见 `Assets/ReloadGuardProjectAdapters`。
- 测试程序集（引用了 nunit 的程序集）中的特性和适配器不参与重置，也不参与检查。

## 附录：遗留问题清单中的规则编号

报告和 Console 中只显示问题类型的名称。遗留问题清单（`EasyReloadGuardBaseline.txt`）每行开头的编号是工具用来比对的标识，对照如下：

| 编号 | 问题类型 |
| --- | --- |
| RG001 | 静态事件或委托未处理 |
| RG002 | 静态 Unity 对象未处理 |
| RG003 | 静态字段的值会带到下一次 Play |
| RG004 | 只读集合、数组或对象的内容会带到下一次 Play |
| RG005 | async void 方法 |
| RG006 | 静态字段持有异步任务、线程或计时器 |
| RG012 | 泛型静态字段 |
| RG013 | 已加的重置会破坏下一次 Play |
| RG014 | 重置方法漏掉了字段 |
| RG015 | 声明保留的字段仍在 Play 中被修改（行末附带修改位置） |
| RG023 | 第三方代码的已知问题 |
| RG024 | 跳过检查的代码中静态字段有变化（行末附带复核时的字段） |
