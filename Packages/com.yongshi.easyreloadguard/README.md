# Easy Reload Guard

用于关闭 Domain Reload（Enter Play Mode Options → DisableDomainReload）的工具包：静态状态重置、退出 Play 时释放、Play 会话边界、第三方 UPM/DLL 适配、离线检查与基线、强制重载。

- **业务代码只依赖特性**：`EasyReloadGuard` 运行时程序集只有特性和 `PlaySession`，自动引用（Assembly-CSharp 不需要改 asmdef）。
- **Player 中没有开销**：Player 每次启动都是新的域，重置引擎只在 Editor 关闭 Domain Reload 时执行。
- **方案统一**：新代码和旧代码都使用同一套特性；第三方代码使用适配器；实在无法处理时强制重载兜底。

> 验证状态（Unity 6000.3.1f1，2026-10-22）：
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
| 复杂 owner（池、注册表、需要按顺序恢复的不变量） | `[ResetMethod(order)] static void Reset()`，该类型整体视为 owner 自行管理 |
| 订阅、线程、计时器、Socket、Native 内存 | `[OnExitPlayMode(order)] static void Release()` |
| 有意跨会话保留的状态 | `[ReloadSafe("原因")]`（字段/类型/程序集，原因必填） |
| 泛型单例 `Singleton<T>.s_Instance` | 在泛型定义的字段上加 `[ResetStatic]`，自动展开所有“经派生类出现”的闭合类型；直接使用的闭合类型在首次赋值处调用 `PlaySession.RegisterReset(() => s_Instance = null)` |
| 异步结果回写 | 传 `PlaySession.Token`，或 `var s = PlaySession.Current; await ...; if (!s.IsAlive) return;` |

`ResetMode.Default` 有一个陷阱：`static float s_Speed = 1f` 会被重置为 0。首次进入 Play 时，如果观察到初值不是 default，会输出警告。

## 2. 执行顺序（关闭 Domain Reload 时）

```
进入 Play（RuntimeInitializeLoadType.SubsystemRegistration）
  PlaySession.Begin（新 Id / 新 Token）
  → PlaySession.RegisterReset 登记项（先进先出，执行后移除）
  → [ResetStatic] 字段（按 类型.字段名 排序）
  → [ResetMethod] 与适配器重置（按 Order，相同 Order 按名称）

退出 Play（ExitingPlayMode，Domain Reload 开启时也执行）
  PlaySession.Token 取消
  → [OnExitPlayMode] 与适配器释放（按 Order）
```

- 单个步骤失败只记录异常，不中断后续步骤。失败汇总后输出错误；默认在下次进入 Play 前强制重载域（设置项“重置失败后强制重载”）。
- 你自己的初始化代码请放在 `AfterAssembliesLoaded` 或更晚的阶段。同一 `SubsystemRegistration` 阶段内，各方法的执行顺序 Unity 不保证。
- `[OnExitPlayMode]` 方法所在类型如果有带副作用的静态构造，退出时会被触发。对于运行时才创建的资源，优先使用 `PlaySession.Token.Register(() => res.Dispose())`。

## 3. 旧代码：统一迁移流程

1. **Tools/Easy Reload Guard/运行检查**：扫描 Assets 下全部 Player 程序集和预编译 DLL 的静态字段，报告写入 `Library/EasyReloadGuard/AuditReport.md`。
2. 已经使用 Unity 推荐写法（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 或 `[InitializeOnEnterPlayMode]`）的类型，视为自行重置，不再报告。
3. **Tools/Easy Reload Guard/将当前遗留问题写入基线**：生成 `ProjectSettings/EasyReloadGuardBaseline.txt`，提交到版本管理。
4. 在 Project Settings/Easy Reload Guard 中把“进入 Play 前检查”设为 **Block**。之后新增的问题会阻止进入 Play，基线内的遗留问题只做列出。
5. 按报告逐个修复，修复后删除对应基线行。已经不存在的基线条目会以 RG030 提示。

| 规则 | 级别 | 含义 |
| --- | --- | --- |
| RG001 | 错误 | 未处理的静态事件/委托 |
| RG002 | 错误 | 未处理的静态 `UnityEngine.Object`（单例常见） |
| RG003 | 警告 | 未处理的可变静态字段 |
| RG004 | 警告/提示 | `readonly` 集合（警告）、数组或对象（提示）的内容可能残留 |
| RG010 | 错误 | 特性用错（readonly 用 Default、引用类型用 InitialValue、async 重置方法等），不可入基线 |
| RG011 | 错误 | `[ReloadSafe]` 没写原因 |
| RG012 | 提示/警告 | 泛型静态字段展开结果 |
| RG020 | 错误 | 适配器找不到类型/成员（目标已升级或改名），不可入基线 |
| RG021 | 警告 | 适配器目标的版本未经复核 |
| RG022 | 提示 | 适配器要求强制重载 |
| RG030 | 提示 | 基线条目已失效 |
| RG040 | 警告 | 某个类型无法反射分析，已跳过 |

## 4. 第三方 UPM 包

- 默认**不扫描**包。需要检查的包，在设置的“纳入检查的包”中添加。
- 包本身已经处理了关闭 Domain Reload 的情况：写一个适配器并调用 `SelfHandled("依据")`，此后忽略该包。内置适配器：UniTask 2.5.11、Input System 1.17.0（依据见 `Editor/Adapters/BuiltInAdapters.cs`）。
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
- 升级目标包后，如果版本不在 `VerifiedVersions` 中，检查器报 RG021：复核后追加版本即可。成员被改名或删除时报 RG020 错误，不会静默失效。

## 5. DLL

DLL 可以按以下顺序依次尝试，前一种不可行时再用下一种：

1. **先查**：Assets 下的预编译 DLL 默认在扫描范围内，可以看出有哪些静态状态。
2. **反射适配**：`TargetKind => AdapterTargetKind.Assembly`，`Target` 填程序集名，`VerifiedVersions` 填 AssemblyVersion，再用 `ResetField` / `InvokeOnReset` / `InvokeOnExitPlayMode` 处理私有静态状态或调用其自带的清理 API。DLL 更新后版本会变化，触发复核。
3. **确认安全**：`HandlesType(type, 依据)` 或 `SelfHandled(依据)`。
4. **熔断兜底**：`b.RequireDomainReload("原因")`。关闭 Domain Reload 时，每次进入 Play 前会自动重载域，等于只为这个 DLL 恢复了 Domain Reload，正确性优先。
5. **运行时发现异常**：业务代码调用 `PlaySession.RequestDomainReload("原因")`，下次进入 Play 前重载。

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

插拔边界：业务代码只依赖 `EasyReloadGuard` 程序集中的特性。卸载本包时，删除这些特性和 `PlaySession` 调用即可，不影响任何运行时行为。

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
- 检查只基于反射元数据，无法识别“静态构造中订阅了外部事件”这类行为。这类问题需要 owner 自行在 `[OnExitPlayMode]` 中解除订阅。
- 关闭 Scene Reload 带来的问题（对象不重建）不在本包范围内。
- 测试程序集（引用 nunit）中的特性和适配器不会进入项目的重置计划和检查。
