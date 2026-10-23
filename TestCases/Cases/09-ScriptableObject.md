# UNITY-04 / ScriptableObject 资产与运行副本

补例日期：2026-10-23。**已补测试源码；当前结果统一为未编译、未运行。** 测试分别处理持续加载的源资产和每局创建的运行副本，不把 Scene Reload 当作资产一定重建的保证。

## 类型、包名与实际路径

- 类型：项目侧 ScriptableObject 源码案例，目标 Unity 6000.3.8f1。
- 使用包：`com.yongshi.easyreloadguard@0.1.0`。
- 资产类型与所有者：[ScriptableStateCase.cs](../../Assets/DomainReloadCases/Runtime/ScriptableStateCase.cs)。
- 自动化入口：[ScriptableStateTests.cs](../../Assets/DomainReloadCases/Tests/Editor/ScriptableStateTests.cs)。
- 共用运行设置：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。
- 临时源资产将由未来测试运行创建在 `Assets/DomainReloadCases/GeneratedTestAssets/ScriptableState_<GUID>/Source.asset`。**本次没有创建或修改 .asset。**

## 适配方案

源资产的 `AuthorValue` 是作者配置；`RuntimeCount` 与事件订阅是会话状态。项目侧 owner 保留资产引用，在 `[ResetMethod]` 中重置会话状态，在 `[OnExitPlayMode]` 中解除订阅、释放运行副本并清理源资产的会话状态，不保存运行修改。

`Instantiate` 和 `CreateInstance` 产生的副本都进入 owner 的独立列表。退出时先从源事件退订，再同步销毁这些自行创建的副本；源资产不进入该销毁列表。这里使用公开 ResetMethod/OnExit 方案，**不修改供应商包，也不对 PackageCache 打补丁**。

## 用例

共同模式为 **R1（Domain Reload 关、Scene Reload 开），连续 3 轮 Play→Stop**。

| ID | 实际测试方法 | 操作步骤 | 通过标准 |
| --- | --- | --- | --- |
| SO-01 | `SO01_LoadedAssetKeepsAuthorConfigurationWithoutPersistingRuntimeState` | 创建作者配置为 17 的测试资产，记录 SHA256 和实例 ID，绑定 static owner；每轮修改非序列化值、添加一份订阅并触发事件；退出后再次触发并核对资产 | 持续加载资产及 static 引用保持一致；每轮 RuntimeCount 为 0、无旧订阅；本轮触发仅响应一次；退出后旧订阅不响应、状态清零；作者配置仍为 17，磁盘资产 SHA256 不变 |
| SO-02 | `SO02_RuntimeCopiesUnsubscribeAndAreDestroyedBeforeNextSession` | 每轮各创建 Instantiate 与 CreateInstance 副本，修改克隆配置并由源事件触发；退出后保留旧托管引用，再进入下一轮 | 克隆继承配置 17，CreateInstance 使用默认配置 7；改克隆不回写源资产；每个当前副本只响应一次；退出后副本 Unity 对象销毁、自有列表为空、源订阅为空；源资产存活且 SHA256 不变 |

测试在运行时只保存自己刚创建的源资产建立基线；测量阶段不调用保存。收尾删除唯一 GUID 目录及其 `.meta`，不删除其他案例或用户资产。

## 当前结果与未覆盖范围

- 2 个测试方法已有实际断言，**未编译、未运行**，当前没有通过结果。
- SHA256 仅保护本例创建的磁盘资产；并未完整覆盖 Inspector 脏标记、所有编辑器保存路径、导入重写或真实业务资产迁移。
- 副本释放采用 `DestroyImmediate` 以同步完成此案例的退出所有权清理，仅作用于直接创建并登记的副本；不代表对所有 Unity 资源都应使用相同销毁策略。
- 不覆盖 R0/R2/R3、Addressables/AssetBundle、跨场景资产卸载、多个独立 owner 共用事件通道、异步任务、Player/IL2CPP。
- 对资产存活、身份和退出清理的判断仍需将来运行验证；完整设计边界见 [测试矩阵](../DomainReload-TestMatrix.md)。
