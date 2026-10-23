# UNITY-03 / 普通对象与 MonoBehaviour 生命周期

补例日期：2026-10-23。**已补测试源码；当前结果统一为未编译、未运行。** 本案例区分状态对象是否存活、是否重建，以及实际参与场景序列化的字段。

## 类型、包名与实际路径

- 类型：项目侧对象生命周期源码案例，目标 Unity 6000.3.8f1。
- 使用包：`com.yongshi.easyreloadguard@0.1.0`。
- 普通状态树：[StateOwnershipCase.cs](../../Assets/DomainReloadCases/Runtime/StateOwnershipCase.cs)；其 OBJ-01 方法在 [StateOwnershipTests.cs](../../Assets/DomainReloadCases/Tests/Editor/StateOwnershipTests.cs)。
- MonoBehaviour 探针：[ObjectLifetimeProbe.cs](../../Assets/DomainReloadCases/Runtime/ObjectLifetimeProbe.cs)。
- 字段/生命周期测试：[ObjectLifetimeTests.cs](../../Assets/DomainReloadCases/Tests/Editor/ObjectLifetimeTests.cs)。
- 运行设置：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。
- 临时场景和 Prefab 将由未来测试运行创建在 `Assets/DomainReloadCases/GeneratedTestAssets/ObjectLifetime_<GUID>/`；**本次未生成或打开场景**。

## 适配方案

普通对象每局 `new` 时自然得到新状态树，不给所有实例字段逐一加 Reset。由 static 持有的状态根通过项目侧 `[ResetMethod]` 重建，旧根通过 `[OnExitPlayMode]` 关闭写入口并解除自有事件。MonoBehaviour 探针本身不增加静态 reset；测试观察它的序列化数据和真实 Awake/OnEnable/Start/OnDisable/OnDestroy。

本组使用包的公开特性和入口，不修改供应商包。测试创建自己的附加场景，使用包含 Camera/Light 的 `DefaultGameObjects`；恢复原活动场景，只关闭自身场景、删除自身 GUID 目录及其 `.meta`，不保存用户场景。

## 用例

| ID | 实际测试方法 / 模式 | 操作步骤 | 通过标准 |
| --- | --- | --- | --- |
| OBJ-01 | `OBJ01_NewTreesNeedNoFieldAnnotationsAndOldRootsRejectWrites`；3 轮 R1 | 每轮创建一棵局部树，同时取得 static 根；改根与子对象、订阅事件；退出后保留旧根，下一轮尝试旧入口写入 | 新树及新 static 根都从基线开始；旧根被替换且已释放、订阅清空、`TryAdd` 拒绝写入；旧调用不影响新根；退出后旧订阅不再响应 |
| OBJ-02 字段结构 | `OBJ02_SerializedObjectDistinguishesFieldsFromRuntimeOnlyState`；**纯 EditMode** | 通过 `SerializedObject.FindProperty` 查询 public 字段、private SerializeField、List、NonSerialized、自动属性背后字段及 nullable | public/private 声明的序列化字段与 List 可找到；本例 NonSerialized、未声明序列化的属性背后字段、nullable 不可找到；此项不声称验证跨 Play 或脚本热重载 |
| OBJ-02 运行实例 | `OBJ02_PrefabInstancesRestoreSerializedConfigurationAndRuntimeInitializers`；3 轮 R1 | 作者对象写入序列化配置和不同的临时非序列化值，保存到测试 Prefab；每轮实例化，等待真实 Start，修改全部字段后退出 | 序列化值为作者配置 `11`、`22`、`[3,5,8]`；NonSerialized/属性/nullable 使用本例新对象初值 `30`、`40`、`60`；运行修改不带到下一实例；上一局对象已销毁 |
| OBJ-03 | `OBJ03_SceneAndDynamicObjectLifetimesHaveObservableCallbacks`；3 轮 R1 | 预存 active、inactive、disabled、从未激活四类对象；进入后启用其中两类，反复禁用/启用 active 组件；创建并销毁动态对象，随后退出 | active 能实际 Awake/OnEnable/Start；尚未启用对象不提前 Start，启用后 Start 一次；组件重启不会重复 Start；动态对象销毁回调可见；所有本轮对象退出，包括从未激活的对象 |

这里 R1 为 Domain Reload 关、Scene Reload 开。OBJ-03 不要求每种对象具有相同的 OnEnable 次数，也不把从未激活对象是否收到 OnDestroy 当作它已经清理的唯一证据。

## 当前结果与未覆盖范围

- 4 个方法已实现：3 个三轮 R1 测试、1 个 EditMode 字段结构测试；**均未编译、未运行**。
- Prefab 非序列化初值、真实对象身份及各生命周期断言尚需未来实际执行验证；不能把当前源码断言写成引擎实测结论。
- 不覆盖 R0 对照、R2/R3 对象复用、OBJ-04、热重载保存规则、完整字段类型矩阵、Player/IL2CPP 或资源任务清理。
- 这些代表用例不意味着“所有没有 SerializeField 的字段都需要 reset”；是否保留取决于对象生命周期与状态根所有权。完整边界见 [测试矩阵](../DomainReload-TestMatrix.md)。
