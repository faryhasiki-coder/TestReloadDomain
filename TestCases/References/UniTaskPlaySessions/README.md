# UniTask / Play Sessions 历史参考

2026-10-22 按用户“切了分支，处理冗余”的要求从活动 Unity 编译范围移出。

- [原始 8 项测试](UniTaskValidationTests.cs.txt)：保持原文，用 .txt 保存，防止误作活动测试。
- [原始结果说明](HistoricalResults.md)：当时 7/8，通过的原包回归为 33/33；这些是历史结果，不能算入当前分支运行。
- [取消语义桥接参考](Integrations/UniTask/SessionUniTaskExtensions.cs)：需要 Faryhasiki.PlaySessions.Core，当前分支没有该包。
- 原路径分别是 `Assets/UniTaskValidation/Tests/Editor/UniTaskValidationTests.cs` 与 `Assets/Integrations/UniTask/`。历史说明中的旧路径、菜单和状态均属于原分支。

本目录位于项目根目录的 TestCases 下，不属于 Unity Assets 或 Packages，因此其中的 .cs / asmdef / meta 均不参与 Unity 编译和导入。保留 meta 仅为将来恢复原资产身份。

当前独立测试入口见 [UniTask README](../../../Assets/UniTaskValidation/README.md)。UT-A01 / UT-A02 / UT-A03 依赖原框架，重新接入前需要核对 API 并再次运行，不复制历史通过数。
