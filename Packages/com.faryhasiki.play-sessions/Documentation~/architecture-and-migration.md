# 架构、生命周期与迁移

## 所有权边界

- Core：普通 C#。依赖图、状态机、作用域、声明、反射目录和静态状态检查；无 Unity 引用。
- Runtime：Unity SubsystemRegistration / BeforeSceneLoad 桥接，仅在 Editor 自动运行；无 UnityEditor 引用。
- Editor：Unity 版本边界、Play 前检查、兼容性登记、报告、Monitor 和强制重载。
- 项目集成：业务重置和供应商适配器；本包不认识任何具体供应商。

Unity 相关编辑器调用集中在 `Editor/UnitySessionBridge.cs`；包管理/程序集发现位于 `PlaySessionAudit.cs`。升级时先核对这两个文件和 Runtime 的初始化时机，Core 契约可以单独回归。

## 一次会话

1. Fast Play 前验证程序集目录、依赖图、版本、声明和可选遗漏检查。
2. SubsystemRegistration：再次建立目录和验证，按依赖顺序 Reset。
3. BeforeSceneLoad：全部 Reset 成功后按依赖顺序 Start。
4. ExitingPlayMode：使全部 scope 失效，发出取消，按逆依赖、逆注册顺序执行清理。
5. Editor update 非阻塞地等任务结束。全部成功回到 Idle；超时、任务故障和清理异常进入 Faulted。

部分 Reset/Start 失败也会清理已经创建的 scope，包括抛错的模块。故障诊断包含模块和阶段。重新进入 Fast Play 不能覆盖未结束的会话；实际域重载才会建立新的 host。

这套顺序只约束本包注册的模块。Unity 不保证不同库的同类 RuntimeInitialize 方法间的顺序；不要依赖它抢在未接入的第三方回调前执行。需要在外部库初始化完成后使用的资源放 Start，若其时机仍不可控，使用供应商明确的 ready 信号或要求完整重载。运行时初始化失败会请求停止 Play，但不能保证其他未接入脚本完全没执行。

字段重置是默认值赋值，复杂对象的语义保留在业务或供应商 API。反射检查不读取字段值，不执行未知状态的“全局清空”。类型级覆盖与 Persistent 均是责任声明，不是正确性证明。

## 升级流程

1. 保留当前包和项目集成代码；先启用正常 Domain Reload。
2. 升级 Unity 后核对桥接 API，执行包单元测试、真实 Play 循环、适配器回归和故障恢复测试。
3. 检查报告中的 `PS301`，在 Monitor 输入实际验证记录路径，再记录新 Unity 版本。仅编辑版本号不是验证。
4. 供应商包独立升级、独立调整精确版本/哈希声明，避免和 Unity 升级混为一次无依据放行。
5. 验证通过后再关闭 Domain Reload；用户验收与自动测试结果分开记录。

首版验证记录只覆盖 Unity 6000.3.8f1。未测试的 Unity 版本不会沿用默认批准。Monitor 将选项与验证记录保存到 `ProjectSettings/PlaySessionsSettings.asset`，适合版本控制；当前工程已生成默认配置。

## 拔除和故障维修

先停止 Play、开启正常 Domain Reload、实际重载一次。随后移除项目适配程序集和属性声明，再移除本 UPM 包及 manifest 的 testables 项。此时普通 Unity 重载重新负责静态状态；业务的正式游戏生命周期不能依赖这个 Editor 工具。

保留业务 Reset/Shutdown 函数有助于测试和迁移；它们与本包的耦合集中在属性与注册文件。旧代码或第三方代码通常无需修改。

强制重载调用 Unity 的 RequestScriptReload，停止 Play 后异步请求脚本程序集重载，并记录完成时间。它不会重新编译改动的源码，也不重启进程；编译错误应先修复，原生故障应按供应商方案或重启 Editor 处理。

## 检查范围

扫描 Assets 的 Player 程序集和主动声明的程序集，覆盖未分类静态字段、readonly 集合、事件 backing field、缺失所有者与冲突标记。Editor/Test 程序集、自动生成类型、未经声明的外部库内部状态、ScriptableObject/场景对象实例字段、反射晚加载代码、任意线程写入和原生内存不在完整性保证内。关闭 Scene Reload 会阻断，因为实例状态恢复尚未实现。

扫描不会证明方法幂等性、Dispose 正确性或所有资源被释放；因此必须保留行为测试。报告列出扫描程序集和未评审依赖，严格模式可阻断未评审依赖，默认不会宣称整个项目安全。

## 官方依据

- [Unity 6000.3：关闭 Domain Reload 的行为](https://docs.unity3d.com/6000.3/Documentation/Manual/domain-reloading.html)
- [SubsystemRegistration](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RuntimeInitializeLoadType.SubsystemRegistration.html)
- [RequestScriptReload](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorUtility.RequestScriptReload.html)
