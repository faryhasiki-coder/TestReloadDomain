# Changelog

## [0.1.0] - 2026-10-22

- 初版：ResetStatic/ResetMethod/OnExitPlayMode/ReloadSafe 特性、PlaySession 会话边界、重置引擎、ExitingEditMode 关口、强制重载、适配器（含 UniTask 2.5.11、Input System 1.17.0 内置声明）、离线检查与基线、批处理入口、Project Settings 页、EditMode 测试。
- 延迟执行改用 `EditorCompat.RunOnNextUpdate`，替代 `delayCall`。原因：编辑器在后台由外部工具驱动时 `delayCall` 不执行，“取消后重载并进入 Play”会卡住。
- 在 Unity 6000.3.1f1 上验证：批处理流水线中编译无错误、无警告，EditMode 测试 27/27 通过；在用户打开的编辑器中通过 com.unity.pipeline 完成测试、连续 3 轮人工 Play→Stop 重入，以及强制重载链路验证。
