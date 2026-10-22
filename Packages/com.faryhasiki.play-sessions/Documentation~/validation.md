# 验证记录

日期：2026-10-22（Asia/Shanghai）。包版本 0.1.0，Unity 6000.3.8f1，Input System 1.20.1，Unity Test Framework 1.6.0，Windows Editor。

## 已执行

- Unity 编译成功，`EditorUtility.scriptCompilationFailed = false`。
- 首轮 Core/兼容性测试 25/25 通过；首轮实际 Play 集成 4/4 通过，保存了逐项结果。
- 加固后的联合测试共 33 项完成，MCP job `015701e95fa045d0a4769b359869b972` 返回 `succeeded`、completed=33、total=33、failures=[]。包括新增的两项生命周期重入测试、Unity 升级阻断、原生重启标记，以及真实 Input System 键盘事件。
- 联合测试发生实际 Domain Reload 后，MCP 未保留逐项 result（其 TestJobManager 明确不跨域持久化完整结果）；原始成功状态/进度已保存，未伪造逐项报告。
- 正常 Domain Reload 与连续三次 Fast Play：字段和集合恢复、事件订阅仅一次、退出订阅归零。
- Input System：每轮创建 InputAction，临时键盘输入仅触发一次回调，退出后引用清空、Disable/解绑/Dispose；供应商源码未修改。
- 实际编译的独立托管 DLL：公开 API 外部适配，20 次核心会话，无订阅/集合累积；测试 DLL 没有 Unity 或 Play Sessions 引用。
- 强制重载实测：Edit 状态将 Wallet.Coins 改为 73，调用 ForceReload；实际重载后变成 0，host=Idle，完成时间为 `2026-10-22T09:01:16.785896Z`（北京时间 17:01:16）。
- 全项目元数据审计：扫描 Assembly-CSharp、PlaySessionDemo，4 个模块；0 Error、13 个原生插件元数据提示、17 个未评审的非内置包。提示是导入器清单，不代表所有平台的原生插件都已在当前进程加载。
- 原始 Enter Play Mode 配置文件无变更；SampleScene 的 SHA256 保持 `07FCE8B04804DDCCB3026DB5B43D8DC41BA138E49D50D36A5C24877081EACDCA`。

## 证据位置

当前工程 `outputs/play-sessions/`：

- `initial-core-tests.json`、`initial-integration-tests.json`：首轮逐项测试。
- `test-job.json`：最终联合测试工具原始状态。
- `recovery.json`：强制重载后的实际值。
- `audit.json`：元数据检查、依赖清单和范围限制。
- `monitor.png`：实际 Editor Monitor 截图，已检查状态、恢复按钮与诊断列表的排版。

## 未覆盖 / 待验收

- 当前项目含真实 Input System 接入，但不是完整第三方框架或完整开源项目的迁移验收。
- DLL 验证是受控托管夹具；未验证未知商业 DLL、原生 SDK、原生线程或设备驱动的恢复。
- 新 Unity 版本只验证“版本变化会阻断”的机制，没有在第二个 Unity 版本实测。
- Scene Reload 关闭、Player/IL2CPP 自动生命周期不属于首版范围。
- 离线 batch 的检查函数及导出在当前 Editor 已执行；未另外关闭工程启动 batch 进程，因此尚未验证独立进程退出码及首次离线导入。
- 用户对接入成本、API 和真实项目迁移质量的评估尚未完成。

Console 中仍有既有 Unity AI Assistant `NoSubscription` 消息；它们不是本包编译错误，本次没有清空或修改该服务。
