# PKG-03 / Unity.Mathematics

## 包、路径与适配

- 包名：`com.unity.mathematics@1.3.3`，当前工程已有的间接依赖。
- 用途：向量、矩阵等数值计算；含可显式持有 state 的随机数结构。
- 本地包清单 revision：`1695a8503482a3131be78cc26308a93f82c05b04`；随包许可证：Unity Companion License。
- [官方源码](https://github.com/Unity-Technologies/Unity.Mathematics)、[包文档](https://docs.unity3d.com/Packages/com.unity.mathematics@1.3/manual/index.html)。
- 用例：[MathematicsValidationTests.cs](../../Assets/MathematicsValidation/Tests/Editor/MathematicsValidationTests.cs)。
- **适配器方案：纯函数无需适配器；只对业务持有的 Random state 做明确重建/保存。** 本轮测试用显式种子重建作对照，没有改数学库，也没有依赖 Play Sessions。

## 测试要点、通过标准和实测

| ID | 测试要点 | 通过结果 | 本轮实测 |
| --- | --- | --- | --- |
| MATH-01 | float3 加法、点积、长度、叉积的已知值 | (1,2,3)+(3,2,1)=(4,4,4)；点积=10；(3,4,0) 长度=5；X×Y=Z | 通过 |
| MATH-02 | 同一脚本域中 3 次 Fast Play，保留静态 RNG 和逐会话重新播种的对照 | 保留者续接序列，重新播种者每轮从同一序列开始；每轮纯运算值稳定 | 通过 |

种子 1234 的前四个 NextUInt 实测：

```text
332584830, 1855942592, 4018585649, 3348358577
```

显式重建的每轮结果都等于该序列。未重置的保留者第二轮变为：

```text
3751476255, 2211002830, 987888982, 2888360369
```

这说明业务静态 RNG 不会因 Fast Play 自动重新开始；它不是数学库缺陷。需要每局可重复随机布局时，由业务 reset 创建 `new Unity.Mathematics.Random(seed)`。需要跨局持续的随机流时，续接本来就是预期。

## 验证边界

本轮 **2/2 通过**，已实际进入/退出 Play 3 次，测试后恢复设置。无新增依赖安装，未修改数学包。

不推广到所有 SIMD/Burst 路径、浮点跨平台确定性、全部数学 API、NativeArray/Job 或 IL2CPP。此次显式重置是测试对照；自动会话框架适配仍待接回对应框架后独立验收。
