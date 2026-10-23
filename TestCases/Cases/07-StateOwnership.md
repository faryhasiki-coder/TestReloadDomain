# UNITY-02 / 静态状态、集合与所有权

补例日期：2026-10-23。**已补测试源码；当前结果统一为未编译、未运行。** 下列通过标准是未来执行时的断言要求，不是通过记录。

## 类型、包名与实际路径

- 类型：项目侧业务源码案例，目标 Unity 6000.3.8f1。
- 使用包：`com.yongshi.easyreloadguard@0.1.0`。
- 案例：[StateOwnershipCase.cs](../../Assets/DomainReloadCases/Runtime/StateOwnershipCase.cs)。
- 自动化入口：[StateOwnershipTests.cs](../../Assets/DomainReloadCases/Tests/Editor/StateOwnershipTests.cs)。
- 共用设置保存/恢复入口：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。
- 场景模式：全部为 **R1（Domain Reload 关、Scene Reload 开），连续 3 轮 Play→Stop**。

## 适配方案

项目侧用 `[ResetStatic]` 处理计数、初始化标志及静态自动属性；`ResetMode.Clear` 保留 readonly 集合身份。`[ResetMethod]` 重建状态根和可替换集合，并清掉旧集合中的上一局数据；`[OnExitPlayMode]` 关闭旧状态树的写入口并解除其事件订阅。纯计算缓存和偏好用 `[ReloadSafe]` 说明保留理由。

直接使用的闭合泛型通过首次写入时的 `PlaySession.RegisterReset` 显式登记，登记执行后解除标记，下一局写入重新登记。这里只验证 `int`、`string` 两个实际闭合类型，不宣称静态扫描能发现全部泛型。**适配仅在项目案例源码中完成，不修改供应商包或 PackageCache。**

## 用例

| ID | 实际测试方法 | 操作步骤（各 3 轮 R1） | 通过标准 |
| --- | --- | --- | --- |
| ST-01 | `ST01_InitializationFlagAndCounterRestartForThreeSessions` | 进入后读取基线；连续调用两次业务初始化；修改会话计数；退出再进 | 每轮计数与初始化次数为 0、标志为 false；两次初始化调用只产生一次初始化；下轮仍能初始化 |
| ST-02 | `ST02_DestroyedUnityReferenceDoesNotPreventRecreation` | 创建单例；销毁目标但保留 static 托管引用；再次取得单例；退出重进 | Unity 判空已为 null，但旧托管引用仍存在；取得的是有效的新对象；同局重复取得返回同一个对象；上一局对象已销毁 |
| ST-03 | `ST03_CollectionStrategiesHaveDifferentAliasContracts` | 分别填充 Default、readonly Clear、重建集合，保留外部别名，下一轮读取并再次写入 | Default 字段为 null，但旧别名仍有旧元素；Clear 保留身份且清空元素，之后可继续写入；重建获得新实例，旧别名按本例所有权协议也清空 |
| ST-04 | `ST04_PropertyAndRegisteredClosedGenericsResetEverySession` | 首轮前先登记并污染两个泛型值；每轮修改属性、重复写 `int` 类型及写 `string` 类型；退出再进 | 属性及两个已登记闭合类型每轮归零；两种泛型互不串值；一次性登记执行后下一轮仍可重新登记 |
| ST-05 | `ST05_RetainedPureCacheAndPreferenceHaveSeparateOwnership` | 填入整数平方缓存并改偏好；每轮修改会话计数；退出再进；最终恢复测试前的缓存条目和偏好 | 缓存身份、结果及偏好保留；每局计数归零；缓存类型只接受纯整数数据，不持有场景对象 |

普通 C# 状态树的 OBJ-01 使用同一源码，其独立方法与通过标准见 [对象生命周期案例](08-ObjectLifetime.md)。

## 当前结果与未覆盖范围

- 5 个 ST 方法已有真实断言，**未编译、未运行**；没有本轮通过数或执行证据。
- 进入重置由正常 Play 回调触发；测试没有手调被测 reset 来代替跨 Play 验证。
- 本组不覆盖 R0/R2/R3、所有泛型构造、线程并发、进程重启或 Player/IL2CPP。
- 本例没有新增 InitialValue 字段。项目现有 InitialValue 的首次捕获仍要求 fresh domain；测试不绕过该关口，也不把第一次观察值当作必然的源码初值。
- 更完整的设计边界见 [Domain Reload 测试矩阵](../DomainReload-TestMatrix.md)。
