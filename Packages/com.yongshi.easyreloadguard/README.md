# Easy Reload Guard

在 Unity 中关闭 Domain Reload（Project Settings → Editor → Enter Play Mode Settings）可以让进入 Play 快很多，代价是：**静态变量不会在每次 Play 时恢复初值**。本包帮你做三件事：

1. **找出来**：扫描项目和包里的静态字段，按规范判定每个静态字段是否有本包执行的重置，或能证明初始化之后不变。
2. **恢复它**：在静态字段上加特性，进入 Play 时自动恢复；停止 Play 时调用你指定的释放方法。
3. **验证它**：每次停止 Play 后自动检查，有没有字段还引用已销毁的对象、有没有订阅或集合每次 Play 都在增加。

**0.5.0 第一阶段**：检查结论分为“不符合规范”和不计入结论的“建议”；暂缓仍然不符合。进入 Play 后的一致性比对、全局写入索引、ScriptableObject 内容检查尚未实现。

写新代码时，先看第 2.2 节的编码规范：按规范写的代码不需要事后处理。处理某份检查报告时，请看[接入手册](Documentation~/接入手册.md)：其中说明了每类问题该谁改、怎么改，以及改完后如何证明。版本变化和验证记录见 [CHANGELOG](CHANGELOG.md)。

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

## 2. 约束与编码规范

适用的规则取决于这次是哪种工作：

| 工作 | 适用 |
| --- | --- |
| 写新代码，或重写自己负责的代码 | 2.1 + 2.2：按编码规范一次写对，目标是不产生新的检查问题 |
| 给已有代码接入本包（处理检查报告、排查关闭 Domain Reload 后才出现的问题） | 2.1 + 2.3：尽量少改业务，判断方法见[接入手册](Documentation~/接入手册.md) |

### 2.1 始终适用

1. **不影响打包后的逻辑**：运行时程序集 `EasyReloadGuard` 只包含特性，以及 `PlaySession` 上几个带 `[Conditional("UNITY_EDITOR")]` 的方法（Player 中调用会被编译器删除）。Player 中没有本包的任何状态、启动入口或回调。业务代码中，本包只以特性、`#if UNITY_EDITOR` 中的重置方法和 `EasyReloadGuard` 程序集引用（Assembly-CSharp 自动引用，使用 asmdef 的程序集需手动添加）的形式出现；运行时流程中不调用本包接口，不为关闭 Domain Reload 加判断分支，不调整业务的初始化阶段。
2. **释放和取消由业务自己负责**：订阅、线程、计时器、Socket、下载、原生资源，以及异步任务、后台循环和延迟返回的结果，不能比发起它的对象活得更久。这在 Player 中切场景、返回主菜单、登出时同样是必要的，属于业务本身的正确性。本包不替业务释放或取消，只在检查报告中提示风险（见第 3 节“异步任务”）。
3. **`[OnExitPlayMode]` 只加在业务流程本身也会调用的停止方法上**：不写只在停止 Play 时才被调用的方法。

### 2.2 写新代码：编码规范

参照物是开启 Domain Reload 时的状态：进入 Play 前，静态字段等于脚本刚加载时的值。优先把运行时状态放在实例中；保留的静态状态必须由本包重置，或由检查器证明不变。目标是没有不符合项，不依靠暂缓清单通过。

| # | 规范 | 当前判定方式 |
| --- | --- | --- |
| 1 | 每个可变静态字段有明确处理方式 | 静态：特性或适配器声明；业务在 Awake、Init、SubsystemRegistration 中自行赋值不算处理 |
| 2 | 重置方式与初值匹配 | 静态：非默认初值不用 Default，填充过或无法证明为空的集合不用 Clear |
| 3 | ResetMethod 负责类型内全部不能证明不变的字段 | 静态覆盖检查；实际重置结果需运行验收，自动一致性比对是后续阶段 |
| 4 | ReloadSafe 只说明检查器能证明不变的字段 | 静态：逐字段证明；类型或程序集标记也不整体豁免 |
| 5 | 不用 async void | 静态：带异步状态机且返回 void 即不符合 |
| 6 | Task、取消源、线程、计时器放在实例上 | 静态入口服务例外：同一类型的退出方法必须对对应字段调用 Cancel 或 Dispose；字段仍需重置 |
| 7 | ScriptableObject 不存运行时状态 | 当前靠验收；序列化内容检查留到后续阶段 |
| 8 | 令牌沿异步调用链传递，await 后写状态前检查取消 | 运行验收 |
| 9 | 订阅、线程、计时器和外部资源由业务停止方法释放 | 停止后检查和验收；不限制 += / -= 的代码形状 |
| 10 | Editor 事件只在脚本加载时订阅一次 | 停止后检查增长情况 |

初始化和停止方法应可重复执行。纯计算缓存也需要重置：运行中填入的数据不等于脚本刚加载时的数据。

**能证明不变**：const、readonly 不再赋值；非 readonly 的字段在第一阶段必须私有，且类型内没有初始化以外的赋值。内容还必须不可变：基础值类型、string、Type、委托、递归满足条件的结构体，或所有实例字段 readonly 且内容不可变的密封类。集合和数组必须私有、元素不可变、初始化后每次使用均在只读白名单中。白名单包括 BCL 集合的 TryGetValue、Contains/ContainsKey/ContainsValue、索引读取、Count/Length、GetEnumerator、IndexOf、Keys/Values 的读取；数组支持长度和元素读取。自定义同名方法、传参、返回集合、局部变量转手、LINQ 延迟枚举或无法确认的初值来源均不能作为证明。证明失败需要声明重置，不能用文字依据放行。

**重置由本包执行**：SubsystemRegistration 的业务初始化不豁免字段；InitializeOnEnterPlayMode 方法必须同时标 ResetMethod（或由适配器 InvokeOnReset 指定），由包在字段重置后再执行一次，方法必须可重复调用。不能改变业务初始化阶段来规避检查。

### 2.3 接入已有代码：改动边界

1. **业务代码只做两类修改**：
   - 在**已有的**字段、属性、事件、方法上加特性。
   - 新增**只在 Editor 编译**的重置方法：方法连同 `[ResetMethod]` 整体放在 `#if UNITY_EDITOR` 中，只把本类型的静态字段恢复成刚加载时的样子（写法见[接入手册](Documentation~/接入手册.md)第 4 节）。

   除此之外，不要为本包在业务中新增方法、Unity 消息、参数、登记调用或判断分支。无法修改的第三方代码放到 Editor 适配器中处理（第 5 节）。
2. **业务缺陷修业务**：订阅不成对、缺少释放、`Init` 不去重，在开启 Domain Reload、打包后登出重进时也会出错，不是接入问题。确认后修业务本身；释放方法存在之后，才在它上面加 `[OnExitPlayMode]`。
3. **不借接入重写旧代码**：接入只解决关闭 Domain Reload 带来的问题；值得按 2.2 重构的地方单独提出。

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
| 初值为 default 的单例、普通静态字段 | `[ResetStatic]`：引用置 null，数值归零 |
| 有非默认初值的数值或 string | `[ResetStatic(ResetMode.InitialValue)]`：恢复为第一次进入 Play 时的值 |
| 初始内容为空、仅运行中增删的集合 | `[ResetStatic(ResetMode.Clear)]`；有初始内容时用重置方法恢复 |
| 静态事件、自动属性 | `[field: ResetStatic]` |
| 几个字段需要一起恢复（对象池、注册表等） | 在类型中写一个静态无参方法并加 `[ResetMethod(order)]`。加了之后，整个类型都由这个方法负责，检查器会逐个核对类型中的字段是否都已处理。没有现成方法时，可以新增一个只在 Editor 编译的方法（见接入手册第 4 节） |
| 订阅、线程、计时器、Socket、Native 内存 | 在业务的停止或释放方法（如 `Stop()`，业务流程本身也会调用）上加 `[OnExitPlayMode(order)]`，停止 Play 时调用。写新代码时按 2.2 设计这个方法；接入已有代码时只加在已有方法上，缺少释放是业务缺陷，按 2.3 处理。第三方代码在 Editor 适配器中调用它自带的清理方法 |
| 检查器能证明不变的字段 | 不需要特性；可用 `[ReloadSafe("原因")]` 说明。证明不了即不符合且不能暂缓 |
| 泛型静态字段 | 在泛型定义字段上加 `[ResetStatic]`：合并派生类与单参数接口/基类约束枚举；直接使用且无法枚举的类型用适配器 `ResetGenericField` 提供类型实参，或对闭合类型用 `ResetField` |
| ScriptableObject 等 `OnEnable` 只执行一次的实例状态 | 特性只处理静态成员。写新代码时不把运行时数据写回资产（2.2 第 9 条）；已有代码在 Editor 适配器中用 `OnReset` 找到已加载的实例，调用它已有的入口（例如重新执行 `OnEnable`，效果等同开启 Domain Reload）。不要在业务的 `OnEnable` 中加登记代码 |
| 异步任务、后台循环 | 由发起它的业务代码负责取消，见下文。业务的停止方法可以加 `[OnExitPlayMode]` |

注意 `ResetMode.Default` 的陷阱：`static float s_Speed = 1f` 加 `[ResetStatic]` 后会被重置为 0。静态检查将非默认初值配 Default 判为不符合，不能暂缓；运行时首次读取还会给出辅助警告。

### 异步任务

开启 Domain Reload 时，进入 Play 会卸载全部脚本，上一次 Play 中挂起的 `await` 随之失效。关闭后，它们会在下一次 Play（或编辑模式）中继续执行，上一次 Play 的结果可能写进下一次 Play。

- 每个异步任务都要有明确的发起者和结束时机：MonoBehaviour 发起的任务用 `destroyCancellationToken`；跨场景的服务用自己的 `CancellationTokenSource`，在服务的停止方法中取消（该方法可加 `[OnExitPlayMode]`）；只需在 Play 结束时取消的任务用 `Application.exitCancellationToken`（停止 Play 时取消，下次进入时重建。该令牌在关闭 Domain Reload 时的行为尚未在本工程实测，以连续 Play → Stop 的结果为准）。
- 令牌在发起任务时读取，并沿调用链传递（`Task.Delay(ms, token)`、支持取消的 I/O）。不要把令牌存进静态字段。
- `await` 之后写共享状态前，先确认令牌未取消。被取消的任务以取消结束，不要当作成功处理，也不要吞掉异常。
- 不使用 `async void`，Unity 消息方法也不例外；由同步入口启动可等待、可取消的任务并明确处理终态。线程和线程池计时器要在停止方法中 `Dispose` 或结束；只把字段置 null 不会停止已在运行的工作。

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

1. 升级 0.5.0 时先清空 `ProjectSettings/EasyReloadGuardBaseline.txt` 的旧条目，按新规则重新检查。旧 ReloadSafe 复核行失效；不自动当作暂缓。
2. 从菜单 `Tools/Easy Reload Guard/检查静态变量并查看结果` 运行检查，结果显示在设置页，并写入 `Library/EasyReloadGuard/AuditReport.md`。
3. 按类型修复不符合项。SelfHandled 只接受 Unity 官方来源；其他代码声明 ResetField、ResetType、ResetGenericField 或 InvokeOnReset。
4. 暂时不能处理的“字段没有处理方式”及 KnownIssue，可用“把当前问题列为暂缓”记录。特性/适配器用错、初值不匹配、覆盖不完整、async void、未取消的异步资源、ReloadSafe 证明不了、无法分析或泛型无法展开，均不能暂缓。
5. 检查档位仍是关闭=0、只警告=1、阻止=2，默认只警告。阻止档位拦截未暂缓的不符合项；暂缓不等于符合规范。

### 检查报告怎么看

开头直接显示“符合规范”，或“不符合规范：N 项（待处理 X、暂缓 Y）”；下面分别列出待处理、暂缓、建议，以及适配器和未检查范围。建议不计入结论。字段条目列出初值、写入位置、内容使用和分析范围。非私有可变字段在第一阶段不能通过全局写入证明。

| 问题 | 结论 | 可否暂缓 |
| --- | --- | --- |
| 静态字段、事件、Unity 对象或只读集合内容没有处理方式 | 不符合 | 可以 |
| 第三方代码的 KnownIssue | 不符合 | 可以 |
| 特性用错、原因缺失、重置方式与初值不匹配、覆盖遗漏、ReloadSafe 证明失败 | 不符合 | 不可以 |
| async void、没有取消/释放的静态异步资源、进入 Play 钩子未交给包执行 | 不符合 | 不可以 |
| 适配器目标不存在/用法错误/内置版本未复核、泛型不能展开、IL 分析失败 | 不符合 | 不可以 |
| 私有字段可改为 readonly、泛型展开结果、初值可能不干净、过期清单行 | 建议 | 不影响结论 |
| 项目 SelfHandled 官方包的静态字段签名变化 | 建议 | 可记录复核签名，不计为暂缓的不符合项 |

### 暂缓清单

文件名仍是 `ProjectSettings/EasyReloadGuardBaseline.txt`。每行 `规则编号|程序集|类型.成员`，官方包签名复核行另带证据。生成会覆盖当前清单；修复后删除对应行。清单仅影响阻止 Play，报告始终保留这些不符合项。不要为了获得“符合规范”而扩大排除范围。

### 停止 Play 后检查

关闭 Domain Reload 时，每次停止 Play 回到编辑模式后，会自动检查一次，结果写入 `Library/EasyReloadGuard/ResidueReport.md`；发现问题时 Console 输出 `[Easy Reload Guard]` 警告。它能发现静态检查看不到的运行时问题，连续 Play → Stop 时可以直接定位到负责的代码：

- **仍引用已销毁的对象**：静态字段、集合元素或回调的目标，仍指向上一次 Play 已销毁的 `UnityEngine.Object`。
- **比上次停止时增加**：同一个回调方法在引擎或编辑器的静态事件（`EditorApplication`、`SceneManager`、`Application`、`Camera`、`Canvas`、`RenderPipelineManager` 等）或项目的委托中出现的次数增加了，或者集合元素增加了。从本次脚本加载后的第 2 次停止开始比较。
- 检查的项目字段来自最近一次检查报告中未处理的字段（含暂缓）。本次脚本加载后还没运行过检查时，只检查引擎回调。
- 读取静态字段会触发尚未执行过的静态构造。只创建集合、字符串、数值的静态构造照常读取；其他有静态构造的项目类型默认跳过，报告分别列出“跳过”和“无法读取”的数量。设置“停止后检查也读取有静态构造的类型”可以放开。
- 设置“停止 Play 后检查残留”可以关闭本功能。

## 5. 第三方包和 DLL

默认扫描 Assets、全部包中的 Player 程序集以及当前构建目标可用的 DLL。Editor-only 与测试程序集不扫描。设置排除、目录排除标记和 SelfHandled 官方包列在报告“未检查范围”。`.easyreloadguard-exclude` 的范围从 asmdef/DLL 所在目录向上到包根或项目根，项目根本身不生效；没有 asmdef 的脚本不能按单个目录排除。只给个人本机插件使用，不用于隐藏需要处理的项目状态。

适配器声明怎么处理，不接受“保留也不会出错”的豁免。`SelfHandled` 仅限 Package Manager 报告为 BuiltIn，或 Registry 且 registry.isDefault 的包；嵌入、Git、本地来源及其他注册表，即使包名以 com.unity 开头，也不满足。该来源判定在本次实现后尚未实测。DLL 不能 SelfHandled。内部包源码变更与业务工程接入的授权分别处理。

`HandlesType` 已删除，没有兼容入口。检查器能证明不变的类型可删除旧声明，其余改为逐字段重置或明确的重置动作。

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
        b.ResetType("Vendor.Sdk.Lookup, Vendor.Sdk", () => Vendor.Sdk.Lookup.RestoreInitialContents());
    }
}
```

- 目标包没安装时适配器休眠。程序集目标用 `TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名。
- `ResetField` 核对模式与初值；`InvokeOnReset` 要通过整个类型的字段覆盖检查；`ResetType(typeName, action, order)` 声明动作恢复类型的全部字段。0.5.0 尚无自动结果比对，动作正确性必须运行验收。
- `OnReset` 只执行动作，不宣称任何字段已处理；`InvokeOnExitPlayMode` / `OnExitPlayMode` 调用释放。
- `ResetGenericField(openTypeName, fieldName, mode, Func<IEnumerable<Type>> typeArguments = null)` 支持单泛型参数。省略列表时枚举接口/基类约束；例如 `b.ResetGenericField("Vendor.Entry`1, Vendor", "Id", typeArguments: () => new[] { typeof(MyEntry) });`。没有约束、没有闭合类型或实参非法都会报告不符合。
- 项目适配器不比对 package.json 版本。引用的类型/成员消失会报告不符合；只有 SelfHandled 官方包继续比对静态字段签名，复核后可把签名写进清单。签名比较不覆盖方法体变化。
- 内置官方包适配器按版本复核；版本不在列表中即不符合、不能暂缓，而且继续扫描目标。
- `KnownIssue` 记录已知不符合、可暂缓的问题，不等于已处理。
- 无法恢复时用 `RequireDomainReload("原因")`：进入前重新加载全部脚本，让目标静态字段恢复；这不是只重载某个 DLL，异步资源释放仍由业务负责并继续检查。
- Editor 工具可通过 `PlaySession.RequestDomainReload` 请求下次进入前重新加载；不要向业务流程插入登记或判断。

尚未提供的能力：修改 DLL 的代码（如用 Harmony 或 Cecil 改写静态构造）。侵入性强，又容易随版本失效，确有需要时再单独评估。

## 6. 进入和停止 Play 时的执行顺序（关闭 Domain Reload 时）

```text
ExitingEditMode：检查关口 → 纯初始化类型提前记录 InitialValue → 登记已加载场景数
Unity 调用业务 InitializeOnEnterPlayMode → 编辑模式对象退出
最后一个进入方向的 sceneClosed：
  一次性登记 → 持久登记 → ResetStatic 字段 → ResetMethod / 适配器重置
SubsystemRegistration：若上述重置未执行则补做，并警告时机退回；处理本次重置失败
ExitingPlayMode：OnExitPlayMode / 适配器释放（开启 Domain Reload 时也执行）
```

- 场景关闭回调只在进入登记期间响应，重置每轮只执行一次。场景数来自已加载的场景，不包含 Hierarchy 中未加载的场景；未保存场景、关闭 Scene Reload 和场景计数不匹配的实际效果仍待新版实测。
- `InitialValue` 只采集一次；纯初始化类型在首次 ExitingEditMode 采集，其余在首次重置时采集，报告提示“初值可能不干净”。编辑模式代码更早写入的污染不能自动识别。
- 每一步失败后继续其他步骤。进入重置失败时本轮 Play 作废，请求退出、重新加载并自动再进入；自动重试仍失败时只退出，不再循环。关闭“重置失败后重新加载脚本”时只退出。失败写入 Console 和 `Library/EasyReloadGuard/ResetFailureReport.md`（最近一次失败记录）。
- 请求退出不是立即生效：旧时机实测失败轮仍可执行初始化、Awake/Start 和首帧；外部文件、网络、原生资源副作用不在保证范围。新版 sceneClosed 执行与 SubsystemRegistration 失败收尾组合尚未实测。
- 停止时释放失败仍登记下次进入前重新加载。持久登记失败不会自动取消登记。
- 同阶段业务回调没有包保证的顺序。业务 SubsystemRegistration 写入的字段仍需包重置；InitializeOnEnterPlayMode 方法须同时标 ResetMethod，字段重置后再执行，必须可重复调用。
- OnExitPlayMode 所在类型有带副作用的静态构造时，退出时读取/调用仍可能触发它。

## 7. 重新加载脚本（兜底）

| 入口 | 行为 |
| --- | --- |
| `Tools/Easy Reload Guard/立即重新加载脚本` | Play 中会先停止，再调用 `EditorUtility.RequestScriptReload()` |
| `Tools/Easy Reload Guard/重新加载脚本并进入 Play` | 重新加载完成后自动进入 Play |
| 主工具栏 Play 按钮旁的“重载并 Play” | 同上；右键可只重新加载脚本或打开设置页。第一次加载本包时自动显示（每人每工程一次），之后可在工具栏右键菜单 `Easy Reload Guard` 中隐藏或重新显示 |
| `ForceReload.RequestBeforeNextPlay(reason)`（Editor） | 下次进入 Play 时先取消，重新加载后再进入 |
| `PlaySession.RequestDomainReload(reason)`（运行时代码中调用，Player 中会被编译器删除） | 同上 |
| 重置或释放失败、适配器 `RequireDomainReload` | 自动登记 |

主工具栏上还有一个 Domain Reload 状态图标：**绿色闪电**表示 Domain Reload 已关闭（进入 Play 快），**黄色闪电**表示已开启（常规，每次进入 Play 都重新加载脚本）。按 Unity 实际使用的 `EditorSettings.asset` 序列化开关判断；接口与序列化开关不一致时显示黄色，悬停提示中说明原因。设置改变后约 1 秒内刷新。点击打开 Enter Play Mode 设置，右键打开本包设置；显示和隐藏方式与“重载并 Play”相同。

如果本次脚本加载后还没进入过 Play（刚编译或刚重新加载），脚本已经是干净的，待处理的请求直接视为完成，不会重复加载。

## 8. 命令行检查（CI）

```bash
Unity -batchmode -nographics -projectPath <项目> -executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit -easyReloadGuardReport Build/ReloadGuardReport.md -easyReloadGuardFailOn pending
```

`pending`（默认）在未暂缓的不符合项存在时失败；`violation` 在任何不符合项（含暂缓）存在时失败。0.4 的 error/warning 参数已删除，传入会作为参数错误退出。退出码：0 满足所选策略，1 不满足，2 检查或参数失败。不要同时传 `-quit`。

本工程自带流水线脚本 `Tools/UnityCI/Run-UnityTests.ps1`，依次运行 batchmode 测试和检查。工程正被编辑器打开时，脚本会先把 Assets、Packages、ProjectSettings 和 Library/PackageCache 复制到 `%TEMP%\EasyReloadGuardCI` 再运行，不影响正在使用的编辑器：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityCI/Run-UnityTests.ps1
```

## 9. 升级 Unity 与卸载本包

卸载：删除业务代码中的特性、`#if UNITY_EDITOR` 中的重置方法、对应的 `using EasyReloadGuard;` 和 asmdef 引用，以及引用本包的 Editor 适配器即可。业务方法和流程不需要改回，Player 行为不变。

升级 Unity 后：

1. 复核 `Editor/Core/EditorCompat.cs`：所有和 Unity 版本相关的调用（读取 Enter Play Mode 设置及其序列化字段、在 ExitingEditMode 中取消进入 Play、`RequestScriptReload`、查询包版本）都集中在这个文件。
2. 运行 `Tools/UnityCI/Run-UnityTests.ps1`（或在 Test Runner → EditMode 中运行 `EasyReloadGuard.Editor.Tests` 和 `DomainReloadCases.Tests`）。嵌入式包的测试默认可见，不需要配置 `testables`。
3. 运行检查，处理“适配器找不到目标”“信任的官方包中静态字段有变化”和“适配器目标的版本未复核”（内置适配器对应的包可能也随 Unity 升级了）。
4. 关闭 Domain Reload，连续 Play → Stop 至少 3 次，确认 Console 中没有 `[Easy Reload Guard]` 错误。
5. 新版 Unity 如果提供了官方的同类机制，用适配器接入或替换 `ResetPipeline` 的执行时机即可，业务代码上的特性保持不变。

## 10. 已知限制

- `InitialValue` 的采集时机与局限见第 6 节。如果此前已经运行过 Play（例如先在开启 Domain Reload 时 Play 过，再切换为关闭），进入 Play 前会先重新加载一次脚本，以便拿到干净的初值；但编辑模式下的代码（ExecuteAlways、编辑器工具）在此之前对字段的修改无法识别。
- 在 ExitingEditMode 中通过 `EditorApplication.isPlaying = false` 取消进入 Play：已在 6000.3.1f1 上实测有效。取消时 Unity 会同时触发一次 `ExitingPlayMode`，所以释放方法会多执行一次，释放方法应可重复调用。升级 Unity 后需要重新验证。
- 延迟执行统一使用 `EditorCompat.RunOnNextUpdate`，不用 `EditorApplication.delayCall`。6000.3.1f1 实测：编辑器在后台、由外部工具驱动时，`update` 照常执行，但 `delayCall` 不会被调用，会导致“取消后重新加载并进入 Play”卡住。
- Unity 6.3 中 `EditorSettings.enterPlayModeOptionsEnabled` 由 options 推导，不能单独设置：
  - 先设 enabled、再设 `options = None`，enabled 会被改回 false；
  - 对 `None` 设 `enabled = true`，options 会变成“两项都关闭”；
  - 磁盘上旧版的 `1/0` 组合无法通过 API 原样写回。

  需要修改设置时，先写 options，再写 `enabled = options != None`。
- 6000.3.1f1 实测：接口读到的 `enterPlayModeOptionsEnabled` 只由 options 推导，写入也不会改动 `EditorSettings.asset` 中的 `m_EnterPlayModeOptionsEnabled`；而进入 Play 时 Unity 按这个序列化字段决定。字段为 false 时，接口显示已关闭 Domain Reload，脚本却照样重新加载。本包以序列化字段为准判断是否关闭了 Domain Reload；两者不一致时，设置页和检查报告会提示，进入 Play 时 Console 也会输出警告。用脚本切换时，要用 `SerializedObject` 同时写入该字段，结束后清除脏标记。
- 检查只读取元数据和类型内 IL，不执行它们。全局写入索引尚未实现：非私有可变字段不能仅凭类型内没有写入获得证明。局部变量转手、传参、返回引用等不能证明只读；反射写入和原生状态不在分析范围内。
- 检查无法判断 `await` 之后写入的状态是否已失效，也看不到 lambda 形式的 `async void`、`ConfigureAwait(false)` 之后在线程池上的写入。这些依靠业务按第 3 节处理，并用连续 Play → Stop 和停止后检查验证。
- 检查看不到“静态构造中订阅了外部事件”。这类订阅需要由负责的代码在已有的释放方法中取消（加 `[OnExitPlayMode]`），或在 Editor 适配器中处理；连续 Play 时，停止后检查会报告订阅次数的增加。
- 停止后检查默认跳过静态构造有副作用的项目类型（调用 UnityEngine、业务代码或读取其他类型静态字段的初始化都算有副作用），只读字段的值，不追踪引用链。“没有发现残留”不等于没有残留。
- 关闭 Scene Reload 带来的问题（场景对象不重建）不在本包范围内。
- UniTask 2.5.11：`PlayerLoopHelper.InsertRunner` 每次初始化都向 `EditorApplication.playModeStateChanged` 追加一组处理器（每个时机一个，共 16 个），且从不移除。关闭 Domain Reload 时，每次进入 Play 都会多一组，旧处理器还持有旧的 runner 和队列。本包不内置 UniTask 适配器：检查器看不到挂在 UnityEditor 事件上的订阅，也不能把整个包声明为已处理。应在 UniTask 源码中修复（内部维护的版本）；本测试工程中的临时做法见 `Assets/ReloadGuardProjectAdapters`。
- 测试程序集（引用了 nunit 的程序集）中的特性和适配器不参与重置，也不参与检查。

## 附录：暂缓清单中的规则编号

报告和 Console 使用问题类型名称。编号只用于清单比对，不代表所有类型都可以暂缓。

| 编号 | 问题类型 |
| --- | --- |
| RG001 | 静态事件或委托未处理 |
| RG002 | 静态 Unity 对象未处理 |
| RG003 | 静态字段没有处理方式 |
| RG004 | 只读集合、数组或对象的内容没有处理方式 |
| RG005 | async void 方法 |
| RG006 | 静态字段持有异步任务、线程或计时器 |
| RG010 | 特性用法错误 |
| RG011 | [ReloadSafe] 没写原因 |
| RG012 | 泛型静态字段 |
| RG013 | 重置方式与字段初值不匹配 |
| RG014 | 重置方法漏掉了字段 |
| RG015 | [ReloadSafe] 的字段不能证明不变 |
| RG016 | [InitializeOnEnterPlayMode] 方法没有标 [ResetMethod] |
| RG017 | 初值可能不干净 |
| RG020 | 适配器找不到目标 |
| RG021 | 适配器目标的版本未复核 |
| RG022 | 适配器要求进入 Play 前重新加载脚本 |
| RG023 | 第三方代码的已知问题 |
| RG024 | 信任的官方包中静态字段有变化 |
| RG025 | 适配器用法错误 |
| RG030 | 暂缓清单中的过期行 |
| RG040 | 无法分析的类型 |
