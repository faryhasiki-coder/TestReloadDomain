# PKG-03 / Unity.Mathematics

[案例详情与结果](../../TestCases/Cases/03-Mathematics.md) · [全部案例](../../TestCases/README.md)

包：`com.unity.mathematics@1.3.3`（本地已有），测试程序集：`MathematicsValidation.Tests`。

Test Runner → EditMode：

- MATH-01：向量运算已知结果。
- MATH-02：3 次 Fast Play 的调用方静态随机数续接，与显式种子重建对照。

2026-10-22 实测 **2/2 通过**。纯计算不需要适配器；业务 RNG 的会话状态由调用方管理。当前测试不依赖 Play Sessions，未修改数学包。
