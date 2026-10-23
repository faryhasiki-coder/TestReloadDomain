# UNITY-06 / NativeArray、Job 与 Unity 资源所有权

补全日期：2026-10-23。当前交付为 **3 个有实际断言的测试方法及其案例源码，未编译、未运行**。通过标准表示未来执行应满足的条件，不是本轮实测结果。

## 包名、源码与适配方案

- 会话适配包：`com.yongshi.easyreloadguard@0.1.0`。
- NativeArray / Job 用例引用已有 `com.unity.collections@2.6.2`；本次只补测试程序集的 asmdef 引用，**没有安装依赖**。
- 图形资源使用 Unity 的 `RenderTexture` 和 `Material`；测试选用当前工程已有 URP 的 `Universal Render Pipeline/Unlit` shader。
- 图形资源 owner 源码：[OwnedResourceCase.cs](../../Assets/DomainReloadCases/Runtime/OwnedResourceCase.cs)。
- 图形资源测试：[OwnedResourceTests.cs](../../Assets/DomainReloadCases/Tests/Editor/OwnedResourceTests.cs)。
- NativeArray / Job 测试及其局部 `JobLease` 实现：[NativeJobOwnershipTests.cs](../../Assets/DomainReloadCases/Tests/Editor/NativeJobOwnershipTests.cs)。
- 测试程序集引用：[DomainReloadCases.Tests.asmdef](../../Assets/DomainReloadCases/Tests/Editor/DomainReloadCases.Tests.asmdef)。
- 共用环境管理：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)；对应 [总矩阵 RES-01/02](../DomainReload-TestMatrix.md)。

资源由测试显式创建；静态初始化不会分配 GPU 资源或调度 Job。`JobLease` 持有自己的 NativeArray、JobHandle 和取消注册。会话 token 取消后，先 `Complete()`，读取结果，再 `Dispose()` 容器；清理入口可重复调用。这里的 token 用于发起清理，不声称能够取消一个已经调度的 Job。

`OwnedResourceCase` 持有一个自建 RenderTexture 和从共享源克隆的材质。退出 Play 时通过 `[OnExitPlayMode]` 释放纹理、解除副本绑定并销毁自有 Unity 对象。调用者持有的共享源材质不进入 owner 的销毁范围，直到测试自己的收尾阶段才销毁。

这些是本地资源适配代表案例，不是任何 ZString、UniTask 或第三方渲染包的真实适配。没有修改 Unity、Collections、URP 或 Easy Reload Guard 的供应商源码。

## 测试方法与通过标准

R1 表示 Domain Reload 关闭、Scene Reload 开启。跨 Play 方法连续执行 3 轮；所有方法当前结果均为 **未编译、未运行**。

| 矩阵 ID / 本页项 | 实际测试方法与范围 | 操作步骤 | 通过标准 | 当前结果 |
| --- | --- | --- | --- | --- |
| RES-01 | `SessionExitCompletesJobAndReleasesNativeArray`；R1 × 3；`NativeJobOwnershipTests` | 每轮创建 `Allocator.Persistent` NativeArray，调度实际 `IJob` 写入 42；退出时触发 owner 清理；再显式重复释放 | 退出后 Job 已 `Complete`，释放前读到 42，容器 `IsCreated=false`；释放次数为 1，重复调用不增加次数；每轮重新创建并独立释放 | 未编译、未运行 |
| RES-02-A | `OwnedGpuResources_AreReleasedWithoutDestroyingSharedMaterial`；R1 × 3；`OwnedResourceTests` | 在 EditMode 创建调用者持有的共享源材质；每轮申请 RenderTexture 和材质副本，绑定后退出；核对旧对象及共享源 | 每轮纹理确已 `Create`；材质为独立副本且绑定本轮纹理；退出 owner 已释放，自有纹理/材质在 Unity 判空语义下已销毁，owner 数回基线；共享源身份与原纹理绑定不变 | 未编译、未运行 |
| RES-02-B | `ExplicitRelease_IsIdempotentAndPreservesSharedSource`；EditMode-only；`OwnedResourceTests` | 创建共享源与资源 owner，连续调用两次 `Dispose`，检查对象和登记表 | 自有纹理与材质销毁，owner 标为已释放；共享源仍有效，登记数回基线；重复释放不产生第二次资源操作 | 未编译、未运行 |

## 证据范围与未覆盖项

目前未获得编译或执行结果，不能声明 NativeArray 安全检查、驱动、图形设备和当前 Unity 版本已经兼容。后续报告应区分环境不支持资源创建、脚本编译失败和行为断言失败。

- RES-01 已有实际调度和先完成后释放的断言，但没有人为延长 Job，不能保证退出瞬间 Job 必然仍在执行；“退出正好撞上长作业”的确定性故障注入仍未覆盖。
- RES-01 验证本案例所持容器的 `IsCreated`、结果和释放次数，不把它扩大成整个项目不存在 Native 内存泄漏的结论。复杂依赖链、多个容器、Burst 和异常 Job 尚未覆盖。
- RES-02 覆盖 RenderTexture 与材质副本，**尚未覆盖 ComputeBuffer**、异步 GPU 回读、共享资源引用计数及多相机持有情形。
- 本页没有实现释放过程卡死或停止超时的故障注入；不会把正常释放路径的实现当作故障分支已通过。线程/计时器的有界停止见 [外部工作案例](10-ExternalWork.md)。
- RenderTexture 用例需要可用图形设备与相应 shader，尚未验证无图形设备批处理环境。
- 只提供 R1 与注明的 EditMode-only 用例；R0/R2/R3、Player 和 IL2CPP 仍需分别验证。所有 Unity 对象只按案例拥有的范围清理。
