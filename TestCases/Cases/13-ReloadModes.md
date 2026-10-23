# 四种重载组合与对象身份

日期：2026-10-23。类型：项目源码案例。当前结果：**源码已补，未编译验证、未运行；没有新增通过记录。**

## 包与案例路径

- Unity 6000.3.8f1，Unity Test Framework 1.6.0；静态值的项目侧适配使用已有 `com.yongshi.easyreloadguard@0.1.0` 的 `[ResetStatic]`，不修改包源码。
- 运行时：[ReloadModeProbe.cs](../../Assets/DomainReloadCases/Runtime/ReloadModeProbe.cs)。记录域身份、Unity 实例 ID、托管实例标识、字段及真实生命周期计数。
- 测试：[ReloadModeMatrixTests.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadModeMatrixTests.cs)，共用 [ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。普通配置沿用 [PlayReentryTests.cs](../../Assets/DomainReloadCases/Tests/Editor/PlayReentryTests.cs) 的泄漏/修复对照。

## 测试要点、步骤与通过标准

以下四项均映射 DR-01，R2/R3 另涉及 OBJ-04。每项独立连续 3 轮，不改脚本，不手动调用 reset。`Explicit` 是要求未来单独选中运行，不能计作默认测试集已经执行。

| 方法 | Domain / Scene | 测试要点与通过标准 | 本次结果 |
| --- | --- | --- | --- |
| `R0_ReloadDomainAndScene` | 开 / 开 | 每轮域身份改变，static 会话值为 0；记录场景实例及字段，作为完整重载对照 | 未运行 |
| `R1_ReloadSceneOnly` | 关 / 开 | 域身份始终不变，污染过的 static 每轮仍为 0；记录场景重建和字段结果 | 未运行 |
| `R2_ReloadDomainOnly` | 开 / 关，Explicit | 每轮域身份改变、static 为 0；独立记录实例复用和字段，不能套用 R0 场景回调次数 | 未运行 |
| `R3_ReloadNeither` | 关 / 关，Explicit | 域身份不变、static reset 为 0；独立记录实例复用和字段，不能套用 R1 通过结论 | 未运行 |

未来运行步骤：

1. 从满足现有 InitialValue 关口的新脚本域开始，单独选择目标方法。测试保存原有效重载选项、后台运行值、设置资产脏状态和活动场景。
2. 在 `Assets/DomainReloadCases/GeneratedTestAssets/Modes-<GUID>/` 创建本用例独占场景，包含默认 Camera/Light 以及 Active、Inactive、Disabled 三种探针。该目录仅在未来执行时创建。
3. 每轮进入前将 static 改为 77，改写非序列化字段与属性；记录域身份、native ID、managed ID。进入后按表断言 static/域身份，并核对 inactive/disabled 状态仍保留。
4. 输出本轮实例身份、序列化/非序列化字段、属性及 Awake/OnEnable/Start 计数；static 再改为 99 后退出。重复 3 次。
5. 收尾退出 Play、关闭测试场景、恢复原活动场景及环境，删除本用例唯一目录。不会保存用户场景。

## 记录与覆盖边界

- **自动断言**：域是否按配置改变、会话 static 是否重置、inactive/disabled 标记、完成 3 轮。
- **行为记录**：各模式下的实例身份、字段与回调计数。日志值没有统一硬编码成“场景对象总会重建”，也不等于实例业务状态已经适配。
- OBJ-04 的“复用实例需要哪些显式 reset”仍需项目侧修复对照；这四项没有实现所有实例资源的重置方案。完整 OnDisable/OnDestroy 行为由 [对象生命周期案例](08-ObjectLifetime.md) 的 R1 代表实现覆盖，四模式扩展仍待补。
- R2/R3 可能暴露当前框架或其他包不支持的配置。阻止进入、额外换域、失败或未观察到回调必须如实记录，不能算兼容性通过。
- 真实 Domain Reload 只恢复 UTF 顶层枚举器位置，观测基线和轮数因此存入 SessionState；方法直接返回实际观测枚举器，Enter/Exit 指令直接交给 UTF。此处是源码设计说明，未经执行验证。
- DR-03 重编译/重开编辑器清除污染的对照与 DR-04 性能采样仍为专项设计，见 [完整矩阵](../DomainReload-TestMatrix.md)。真实程序集重载可使用 [初始化案例](12-Initialization.md) 的被动观察菜单另行记录。
