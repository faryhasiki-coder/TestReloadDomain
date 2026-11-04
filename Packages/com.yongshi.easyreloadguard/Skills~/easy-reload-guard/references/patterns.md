# 写法参考

所有示例都假定文件中有 `using EasyReloadGuard;`（Editor 适配器另需 `using EasyReloadGuard.Editor;`）。

## 1. 先判断字段属于哪一类

看字段在**所在类型内部**的读写情况（检查报告每条问题下的“读写位置”就是这个结论）：

| 情况 | 处理 |
| --- | --- |
| 私有，只在字段初值或静态构造中赋值，或从未赋值，内容也不改 | 值不会变：`const` 或 `static readonly` |
| 数值或 string，在方法 X 中修改 | 有非默认初值 → `[ResetStatic(ResetMode.InitialValue)]`；否则 `[ResetStatic]` |
| 引用，没有初值，在 X 中赋值 | 下一次 Play 一定会再经过 X（Awake、Init、判空创建）→ `[ResetStatic]` |
| 引用，有初值，又在 X 中重新赋值 | 下一次 Play 一定先经过 X 才能 `[ResetStatic]`；否则在重置方法中恢复初值 |
| 引用，只有初值，没有其他赋值 | 不能置 null。对象下一次 Play 仍然有效 → `[ReloadSafe]`；对象内部有 Play 中的状态 → 在重置方法中重新创建 |
| 集合，只在静态初始化时填充 | 不要 Clear。内容不变 → `[ReloadSafe]` |
| 集合，没有初始内容，在 X 中增删 | `[ResetStatic(ResetMode.Clear)]` |
| 集合，既有初始内容又在 X 中修改 | 重置方法：清掉运行中加入的数据并恢复初始内容 |
| 非私有字段 | 先搜索类型外的写入方，再按上面几行判断 |

## 2. 常见写法

### 单例与普通字段

```csharp
public class GameManager : MonoBehaviour
{
    [ResetStatic] public static GameManager Instance;                  // Awake 中重新赋值
    [ResetStatic(ResetMode.InitialValue)] static float s_Speed = 1f;   // 恢复为 1，不是 0
    [ResetStatic(ResetMode.Clear)] static readonly List<Enemy> s_All = new List<Enemy>();
    [field: ResetStatic] public static int Score { get; private set; }

    void Awake() => Instance = this;
}
```

工程已有单例基类时，在基类的泛型字段上加一次 `[ResetStatic]`，会展开到所有派生类；新单例继承基类即可，不要另写一套。

### 静态事件

```csharp
public static class EventHub
{
    [field: ResetStatic] public static event Action<int> ScoreChanged;
}

public class ScoreView : MonoBehaviour
{
    void OnEnable()  => EventHub.ScoreChanged += OnScore;
    void OnDisable() => EventHub.ScoreChanged -= OnScore;   // 订阅方必须成对取消，这是业务本身的要求
    void OnScore(int score) { }
}
```

订阅只在静态构造或 `[InitializeOnLoad]` 中登记一次时，不要清空事件：清掉后不会再登记。

### 需要一起恢复的注册表（只在 Editor 编译的重置方法）

```csharp
public static class ConditionRegistry
{
    static readonly Dictionary<int, List<Action>> s_Listeners = new Dictionary<int, List<Action>>();
    [ReloadSafe("只读名称表，只在静态初始化时填充")]
    static readonly Dictionary<int, string> s_Names = BuildNames();
    static int s_Version = 1;

#if UNITY_EDITOR
    [ResetMethod]
    static void ResetForPlay()
    {
        s_Listeners.Clear();
        s_Version = 1;
    }
#endif
}
```

规则：`static`、无参、返回 `void`、同步、可以重复调用；只恢复本类型的静态字段，不调用业务流程（不 Init、不创建对象、不订阅、不派发事件）；业务代码不调用它。类型中其余可变静态字段要么由它处理，要么加 `[ReloadSafe]`，检查器会逐个核对。

### 线程、计时器、订阅等需要释放的资源

```csharp
public sealed class NetClient
{
    static NetClient s_Current;
    Thread m_Worker;
    CancellationTokenSource m_Cts;

    public void Start() { /* 创建线程、订阅回调 */ }

    // 业务本身就需要的停止方法：登出、切场景时也会调用
    [OnExitPlayMode]
    public static void StopCurrent()
    {
        s_Current?.m_Cts?.Cancel();
        s_Current?.m_Worker?.Join(1000);
        s_Current = null;
    }
}
```

- `[OnExitPlayMode]` 只能加在静态、无参、返回 `void` 的方法上；无论 Domain Reload 是否关闭，停止 Play 时都会调用。取消进入 Play 时 Unity 也会触发一次停止，所以方法必须可以重复调用。
- 只把字段置 null 不会停止正在运行的工作。没有现成的停止方法时，先按业务需要补齐退出流程，再加特性。

### 异步任务

```csharp
public static class ProfileLoader
{
    [ResetStatic] public static string CurrentProfile { get; private set; }

    public static async Task LoadAsync(Func<CancellationToken, Task<string>> fetch)
    {
        var token = Application.exitCancellationToken;   // 发起时读取，沿调用链传递，不存进静态字段
        CurrentProfile = null;
        var profile = await fetch(token);
        token.ThrowIfCancellationRequested();             // 已停止：以取消结束，不写回
        CurrentProfile = profile;
    }
}
```

- MonoBehaviour 发起的任务用 `destroyCancellationToken`；跨场景的服务用自己的 `CancellationTokenSource`，在服务的停止方法中取消。
- `Application.exitCancellationToken` 在关闭 Domain Reload 时的行为以连续 Play → Stop 的实测为准。
- 被取消的任务以取消结束，不要当作成功，也不要吞掉异常。
- 避免 `async void`；Unity 消息方法不得不用时，在内部传递令牌并处理异常。
- 自己写的每帧循环（`while (running) { await Yield(); }`）在停止 Play 时可能永远不再继续，标志位会一直停在“运行中”。停止方法中要显式复位状态并结束请求。

### ScriptableObject 等只执行一次 OnEnable 的实例状态

特性只处理静态成员。在 Editor 适配器中用 `OnReset` 找到已加载的实例，调用它已有的入口（例如重新执行初始化），效果等同于开启 Domain Reload。不要在业务的 `OnEnable` 中加登记代码。

## 3. Editor 适配器

用于无法修改源码的第三方代码，或者业务中只需在 Editor 恢复、又不适合加特性的状态。写在工程已有的 Editor 适配器程序集中。

```csharp
using EasyReloadGuard;
using EasyReloadGuard.Editor;

sealed class SomeSdkAdapter : ReloadAdapter
{
    public override string Target => "com.vendor.sdk";                  // 包名；程序集用 TargetKind => AdapterTargetKind.Assembly
    public override string[] VerifiedVersions => new[] { "3.2.1" };    // 复核时的版本，只作记录，可以不写
    public override void Configure(ReloadAdapterBuilder b)
    {
        b.ResetField("Vendor.Sdk.Core, Vendor.Sdk", "s_Instance");                // 私有字段也可以
        b.ResetField("Vendor.Sdk.Cache, Vendor.Sdk", "s_Map", ResetMode.Clear);
        b.InvokeOnReset("Vendor.Sdk.Core, Vendor.Sdk", "Shutdown");               // 进入 Play 时调用它自带的方法
        b.InvokeOnExitPlayMode("Vendor.Sdk.Net, Vendor.Sdk", "CloseAll");         // 停止 Play 时调用
        b.OnReset("重建默认配置", () => Vendor.Sdk.Config.Apply(Vendor.Sdk.Config.Default));
        b.HandlesType("Vendor.Sdk.Lookup, Vendor.Sdk", "只读查找表，复核于 3.2.1");
        b.KnownIssue("Vendor.Sdk.Pool, Vendor.Sdk", "s_Buffers", "原生缓冲区没有完整释放入口，保留报告");
    }
}
```

- `SelfHandled("依据")`：整个包逐条复核过、确认不影响运行时才用。
- `KnownIssue` 只是如实报告，不算处理。
- `RequireDomainReload("原因")`：兜底，关闭 Domain Reload 时每次进入 Play 前都重新加载脚本。
- 包更新后不按版本号判断：引用的成员没了报“适配器找不到目标”错误；`HandlesType`、`SelfHandled` 跳过的类型，静态字段有增删或类型变化时报“跳过检查的代码中静态字段有变化”，只复核报告列出的字段，确认后记为已知遗留。不要为了消除提醒而改适配器。
- 业务代码订阅了包的公开静态事件时，检查器看不到这些订阅，仍须由业务代码成对取消。
