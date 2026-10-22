# Play Sessions

Unity 编辑器关闭 Domain Reload 时的显式会话工具包。当前版本 **0.1.0 / 实验阶段**，基准 Unity **6000.3.8f1**，要求 Scene Reload 开启。

| 需求 | 接入方式 |
| --- | --- |
| 新增简单静态状态 | 程序集声明一次；每个标量字段加 `[ResetOnPlay]` |
| 集合、事件、资源、非零初始值 | 一个 `[SessionReset]` 方法，统一注册退出清理 |
| 旧代码 | 独立 `IPlaySessionAdapter`，通过公开 API 重置，无需改业务调用 |
| UPM | 项目侧单独适配程序集；精确版本声明；已有处理的库声明 `SelfManaged` |
| 托管 DLL | 同样适配公开 API；版本和 SHA256 双重检查；无法清理时要求完整重载 |
| 原生 DLL | 优先供应商 Shutdown/Initialize；无法确认的进程状态要求重启编辑器 |
| 遗漏检查 | 手动、Play 前可选、离线 Unity batch 检查；可让警告阻止 Fast Play |
| 故障恢复 | `Tools > Play Sessions > Force Script Reload`，不改变 Play Mode 设置 |

## 安装与入口

当前工程采用 embedded UPM：`Packages/com.faryhasiki.play-sessions/`。包只使用 Unity 自带 API，没有新增第三方依赖。项目示例独立放在 `Assets/PlaySessionDemo/`，其中 Input System 示例复用当前项目已有的 1.20.1。

迁入其他工程可复制整个包目录到 `Packages/`，或通过 Package Manager 的 **Add package from disk** 选择 `package.json`。将 `com.faryhasiki.play-sessions` 加入目标工程 manifest 的 `testables` 后可运行包测试。

入口：`Tools > Play Sessions > Monitor`。这里显示会话状态、诊断编号、依赖评审状态、最近一次强制重载时间，并可导出 JSON 报告。

工具包不自动改变 Enter Play Mode 设置。需要时在 Project Settings > Editor 中关闭 Reload Domain，并保持 Reload Scene 开启。

## 新代码：一个标记处理简单字段

程序集的任意源文件声明一次；asmdef 引用 `Faryhasiki.PlaySessions.Core`：

```csharp
using Faryhasiki.PlaySessions;
[assembly: PlaySessionAssembly]

public static class Wallet
{
    [ResetOnPlay] public static int Coins;
    public static void CollectCoin() => Coins++;
}
```

`ResetOnPlay` 每次进入 Play 设置成 **default(T)**。它不会重跑字段初始化器：`Coins = 10` 仍会被重置为 0。仅支持非 readonly、非 ThreadStatic、非开放泛型的静态数值、布尔、字符、枚举、decimal、string 字段。禁止任意对象、委托、集合和指针，避免只丢弃引用而泄漏资源。

对于非零初始值、单例、集合、静态事件，使用一个明确的方法：

```csharp
[SessionOwned("game.state")]
public static class GameState
{
    public static int Lives = 3;
    public static readonly List<string> Items = new List<string>();

    [SessionReset("game.state")]
    static void Reset(SessionScope scope)
    {
        Lives = 3;
        Items.Clear();
        Application.focusChanged += OnFocus;
        scope.OnStop(() => Application.focusChanged -= OnFocus);
    }
    static void OnFocus(bool focused) { /* project focus handling */ }
}
```

`SessionOwned` 只声明责任归属，不能单独重置字段。`SessionPersistent("保留原因")` 声明有意跨 Play 保留的状态。二者与 `ResetOnPlay` 冲突会报错。

## 旧代码：集中适配

```csharp
[assembly: PlaySessionAdapter(typeof(LegacyAdapter))]

public sealed class LegacyAdapter : IPlaySessionAdapter
{
    public void Configure(SessionPlan plan)
    {
        plan.CoverStatics(typeof(OldCache), "legacy.cache", "Reset clears entries and events; repeated Play regression covers both.");
        plan.Add("legacy.cache", scope =>
        {
            OldCache.Reset();
            scope.OnStop(OldCache.Shutdown);
        }, start: scope => OldCache.Initialize());
    }
}
```

`OldCache` 不需要引用本包。`CoverStatics` 是有证据的类型级责任声明，会检查模块是否存在；它不证明 Reset 函数清理完整。新增静态字段后应更新对应行为测试。无法修改、又没有公开清理接口的旧库不能靠此声明变安全。

`plan.Add` 的 `requires` 指定依赖模块；所有 Reset 完成后才按依赖顺序 Start，退出按逆序清理。配置阶段及 `validate` 必须只读、同步，不创建运行资源。重复 ID、缺失依赖、依赖环和验证失败都会在执行重置前拒绝。

## 资源和后台任务

- `scope.Own(disposable)`：接管 IDisposable，退出时释放。
- `scope.OnStop(action)`：注册退订/关闭操作，同一模块内后注册先执行。
- `scope.Token`：退出时取消；不要把主线程阻塞在等待任务上。
- `scope.Track(task)`：追踪任务，退出后最多等待 2 秒；超时或故障阻断后续 Fast Play。
- `scope.TryCommit(action)`：在主线程发布计算结果；旧会话的回调返回 false。

所有会话 API 必须在所属主线程调用。后台线程只能计算隔离的数据，不能直接写静态字段或调用 Unity API；恢复到主线程后用捕获的 scope 提交。工具包无法拦截绕过 scope 的任意线程写入。清理同步执行，因此资源不能在退出后继续由 worker 使用。

启动/清理失败会保留诊断并锁住后续 Fast Play；不会通过清空错误来假装恢复。可使用正常 Domain Reload 或强制重载恢复。原生线程、GPU/设备或进程状态无法由托管域重载保证清理。

## UPM、DLL、升级与卸载

见 [外部依赖接入](Documentation~/external-integrations.md) 和 [架构与升级](Documentation~/architecture-and-migration.md)。不要直接编辑 `Library/PackageCache/` 或反射清空未知库的所有静态字段。

## 检查与验证

Monitor 可单独运行静态检查。默认 Play 前检查、警告提示；可选择“警告阻止 Fast Play”和“要求全部依赖已评审”。已声明的错误契约始终阻断 Fast Play。未评审包会显示 `PS209`，不能把没有 Error 当作完整项目安全证明。

离线检查需要本机已有 Unity、导入完成的工程和缓存；先关闭占用工程的 Editor：

```powershell
& 'D:/UnityEditor/6000.3.8f1/Editor/Unity.exe' -batchmode -projectPath 'F:/Project/TestReloadDomain' -executeMethod Faryhasiki.PlaySessions.Editor.PlaySessionAudit.RunBatch -logFile 'F:/Project/TestReloadDomain/outputs/play-sessions/audit.log'
```

输出 `outputs/play-sessions/audit.json`，失败退出码 1。这里的“离线”指不需要联网的 **Unity 元数据检查**，不是独立 Roslyn 源码分析器。适配器 Configure/Validate 会运行，必须遵守只读契约。未解析的包首次导入仍可能需要网络。

本包的自动桥接仅在 Editor 运行。Player 中保留 API 类型供项目编译，但不会自动执行重置；不能依赖它替代正式游戏的启动/退出流程。若需要 Player 生命周期，请通过项目的启动代码显式管理。

验证记录见 [validation.md](Documentation~/validation.md)。
