# Play Sessions 0.1.0

面向 Unity 6000.3 的显式 Play 会话管理包。当前包在本项目以 embedded UPM package 开发；没有新增第三方依赖。

## 当前能力与边界

- Core：普通 C#，无 Unity 引用；模块依赖排序、启动失败回收、作用域资源、后台任务、过期提交隔离。
- Runtime：Unity 生命周期桥接；从明确声明的程序集发现组合入口，在 `BeforeSceneLoad` 启动受管模块，在退出时关闭会话。
- Editor：进入 Play 前的声明检查、模式检查、诊断窗口；编译开始时结束当前会话，真实重载前检查清理状态。
- 支持目标：关闭 Domain Reload、开启 Scene Reload；保留两者均开启的基线。关闭 Scene Reload 会被检查阻止。
- 本版检查器是 Editor 反射元数据审计，**不是 Roslyn 编译分析器**。不读取或清空未知字段，不证明 Reset/Dispose 的实现正确，不扫描未声明的程序集。
- 源生成、完整静态数据流分析、任意第三方包自动修复、Play 中脚本热恢复、跨 Unity 版本和 IL2CPP 兼容性不属于本版已验证能力。
- 对受管入口保证会话状态检查；Unity 其他初始化回调和未接入插件的副作用不受本包控制。

## 接入

业务程序集引用 `Faryhasiki.PlaySessions.Core`；使用 Unity 运行入口时再引用 `Faryhasiki.PlaySessions.Runtime`。声明一个组合入口：

```csharp
using Faryhasiki.PlaySessions;
using UnityEngine.Scripting;

[assembly: PlaySessionAssembly(typeof(GameComposition))]
[assembly: AlwaysLinkAssembly]

[Preserve]
public sealed class GameComposition : ISessionComposition
{
    [Preserve] public GameComposition() { }

    public void Configure(SessionPlan plan)
    {
        plan.Add("game", scope =>
        {
            // 在这里创建服务、立即交给 scope.Own；订阅用 scope.OnRetire 退订。
        });
    }
}
```

`Configure`/构造函数只声明模块，预检查与运行启动都会调用，禁止在这里注册事件、启动线程或创建运行资源。模块 ID 全项目唯一；依赖通过 `dependencies` 声明。框架先验证图，按依赖顺序调用全部 Reset，再调用 Start，退出时逆序清理。

`AlwaysLinkAssembly` 与 `Preserve` 用于避免纯反射发现的入口被裁剪；它们不能替代目标 Player/IL2CPP 构建测试。场景相关对象仍在自己的 Awake/场景绑定阶段使用已就绪的服务；本包不会提前访问未加载的场景。

## 状态和资源规则

1. 优先使用每次 Start 新建的普通对象。静态可变字段（包括静态属性/事件的 backing field、readonly 集合）必须声明归属。
2. `[SessionOwned("module.id")]` 表示由已注册模块提供明确 Reset 和释放逻辑；检查器验证映射，不自动修改该字段。
3. `[SessionPersistent("具体理由")]` 表示有意跨 Play 保留；仍需考虑真实程序集重载。不可作为整程序集关闭检查的替代。
4. 静态属性和事件使用 `[field: SessionOwned(...)]` / `[field: SessionPersistent(...)]`。泛型静态状态不自动接入，需迁到会话实例或写明确的封闭类型适配器。
5. `Own(IDisposable)` / `OnDispose(Action)`：创建线程与释放线程均为会话主线程；同一资源只能交给一个所有者。按注册逆序释放。
6. `OnRetire(Action)`：立即断开事件/工具入口。所有 scope 先失效，再执行退订和取消，最后等待任务并释放资源。外部 SDK 已在途的回调仍必须通过 CommitAsync 提交。
7. `RunBackground` 的整个委托在后台线程执行，禁止直接访问 Unity API 或写共享运行状态；结果必须 `await scope.CommitAsync(...)`。返回 false 表示会话已过期，操作没有执行。
8. 任务可以读取其拥有的后台资源，资源在任务完成前不会释放。取消是协作式的，禁止不受跟踪的子任务和 fire-and-forget。
9. 默认退出超时 5 秒，期间 Editor 持续 pump，不阻塞主线程。超时/释放异常会保留失败状态并阻止下一次受管启动，不能通过清空引用“恢复”。确认工作结束后重启编辑器；原生 Job/线程的完成与释放由适配器负责。

## 外部工具适配

实现 `PlayModule` 并 `plan.Add(new MyAdapter())`：`Validate` 检查工具版本/能力，`Reset` 处理明确归属的值，`Start` 获取作用域资源，`VerifyStopped` 检查可观测残留。编辑器连接可跨 Play 保留，但运行对象绑定必须归属会话。

工具请求携带会话 Guid，并在 Unity 主线程调用 `PlaySessionRuntime.TryExecute(id, action)`；仅当前 Ready 会话可执行。传输层自行将请求调度到主线程，本包不包含网络服务。没有公开清理能力的依赖必须报告限制，不能反射破坏供应商内部状态。

## 编辑器入口

- `Tools > Play Sessions > Monitor`：查看模式、会话、资源/任务、错误代码与相关脚本。
- `Run Preflight`：检查当前声明与环境。
- `Enable Fast Play (Domain Reload Off)`：显式关闭 Domain Reload、保留 Scene Reload。
- `Use Standard Play (Reload Both)`：恢复两种重载；安装 package 本身不修改项目模式。

检查错误会请求取消 Play；运行桥接层也重复检查并拒绝受管初始化。不能保证取消动作抢在未接入的第三方 Unity 回调之前。

## 测试与迁移

将包名加入项目 manifest 的 `testables`，用 Test Runner 执行 `Faryhasiki.PlaySessions.Tests` 的 EditMode 测试。当前项目另有 `PlaySessionDemo.Tests`，其中 EditMode 测试实际多次进入/退出 Play，测试结束恢复原设置。

真实重载时发现清理未完成，会在本次编辑器进程中保留诊断并要求重启；不尝试恢复旧会话。核心语义与 Unity 桥接层分程序集，升级先运行核心测试，再在目标 Unity 中重跑实际 Play 测试；本版尚不宣称跨版本认证。

包不引用 demo。移除 `Assets/PlaySessionDemo` 即可移除本项目演示；移除包前先迁移组合入口及程序集引用。需要移植时保持包目录、`.meta` 和 GUID，可作为本地或 Git UPM 包接入。版本回退需同步检查项目适配代码。

## 验收状态

基础 demo、外部 package 简易验证、完整开源工程验证、用户评估分开记录。本项目的完整约定见根目录 `AGENTS.md`，实际结果见 `outputs/play-sessions/validation.md`。演示中的模拟工具按钮不属于外部 package 适配证据。
