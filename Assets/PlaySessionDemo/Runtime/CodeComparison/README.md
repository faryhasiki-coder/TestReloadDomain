# 捡金币：接入 Play Sessions 后，源代码具体改了什么？

这是一个可运行的最小样例。规则只有一条：拾取金币时 `Coins++`，每次新的 Play 应从 0 开始。

Unity 菜单：`Tools > Play Sessions > Demo > Open Coin Code Comparison`。
场景：`Assets/PlaySessionDemo/Scenes/CoinCodeComparison.unity`。

## 1. 并排读这两个文件

- [Before/CoinWallet.cs](Before/CoinWallet.cs)：普通静态金币计数，没有插件引用。
- [After/CoinWallet.cs](After/CoinWallet.cs)：相同业务，增加归属声明和重置函数。

忽略为了让两份代码同时运行而区分的命名空间，实际业务改动如下（省略注释）：

```diff
+using Faryhasiki.PlaySessions;

 public static class CoinWallet
 {
+    [field: SessionOwned("sample.wallet")]
     public static int Coins { get; private set; }

     public static void CollectCoin() => Coins++;

+    internal static void ResetForPlay() => Coins = 0;
 }
```

这里有 3 行新增代码。拾取金币的算法、方法名和调用方式都相同。

## 2. 将重置函数交给插件

现有 [DemoComposition.cs](../DemoComposition.cs) 的 `Configure` 增加 1 行：

```csharp
plan.Add("sample.wallet", _ => { }, reset: CodeComparison.After.CoinWallet.ResetForPlay);
```

- `SessionOwned` 声明状态属于哪个模块，让检查器核对归属；**属性不会自动重置字段**。静态自动属性需要 `field:`，因为实际保存数值的是编译器生成的字段。
- `ResetForPlay` 写清楚这个状态的初值，插件会在每次会话启动、场景加载前调用它。
- `_ => { }` 是这个最小样例的空启动动作：这里只管理数值，没有要创建的服务、事件或资源。
- 本例在下一次 Play 开始时归零，停止时仍可保留最后的数值供观察；不把“停止”和“下一次开始”混成一个动作。

因此，在本项目已经有插件引用和组合入口的前提下，**实际接入为 3 行业务新增 + 1 行注册**。对比界面、独立对照程序集、场景和测试都是演示辅助，不能算作这 4 行里的隐藏工作。

新项目首次接入还需要：安装包、在业务 asmdef 中引用 `Faryhasiki.PlaySessions.Core`，以及声明一个 `ISessionComposition` 入口。本项目已经在 `DemoComposition.cs` 中声明了 `[assembly: PlaySessionAssembly(...)]`、`AlwaysLinkAssembly` 和 `Preserve`；完整首次接入写法见 [包 README](../../../../Packages/com.faryhasiki.play-sessions/README.md)。不能把这些前置条件省略后声称任何项目都只需改 4 行。

## 3. 实际操作

当前项目设置是 Domain Reload 关闭、Scene Reload 开启。打开样例并 Play，点击 3 次“拾取 1 枚金币”，停止，再次 Play：

| 时刻 | 未接入版 | 接入版 |
| --- | ---: | ---: |
| 刚编译后第一次 Play | 0 | 0 |
| 点击 3 次 | 3 | 3 |
| 停止后再次 Play | 3 | 0 |
| 再点 1 次 | 4 | 1 |

测试或此前操作可能已改变左侧值；它会在当前脚本域继续累加，这是本例要展示的行为。不要在两次 Play 之间编辑脚本：脚本重编译导致的域重载会重新初始化左侧。开启 Domain Reload 的标准模式下，两边通常都会从 0 开始，所以对比时要使用上述 Fast Play 设置。

`Before` 使用独立、无插件引用的 asmdef，也没有 `PlaySessionAssembly` 声明：它代表真实的未接入代码，插件不会扫描或重置它。它在这个受控样例中保留数值是为了对照，不能据此把实际业务排除在管理范围外。

## 4. 这个例子说明什么

安装插件不会自动改写现有源码。接入要明确提供状态归属、重置逻辑和注册；金币业务本身不需要重写。这个简单需求也可以使用 Unity 自带的 `SubsystemRegistration` 回调手动重置，因此例子并不证明插件能减少这类小功能的代码量。插件的更多价值在统一管理多个模块、事件退订、资源释放和过期异步结果，原有完整 demo 用于展示那些能力。

本次只验证这个数值样例；外部 package 简易验证、完整开源项目验证均未执行，接入方式和代码质量仍待用户评估。

## 本次验证记录（2026-10-21）

- Unity 6000.3.8f1 编译通过，声明与环境检查为 0 项问题。
- `PlaySessionDemo.Tests`：4/4 通过。新增测试实际进入、退出两次 Play，验证左侧保留金币数，右侧每次从 0 开始；原有重复 Play、退出时任务清理和不支持配置诊断测试也通过。
- 新场景运行：通过按钮使用的 `CollectCoin` 方法拾取 3 枚金币，两边均为 3；停止并再次 Play 后为左 3、右 0。已检查真实 Game View 截图，按钮的鼠标点击未做自动化输入测试。
- 标准重载基线：两边从 0 开始，拾取后均为 3；停止并再次 Play 后两边均为 0。
- 设置记录：原值为 `enabled=true, options=DisableDomainReload`；为验证基线临时切到 `enabled=false, options=None`（Unity 返回的实际值）；最终恢复原值，Scene Reload 始终开启。
- Console 中观察到 Unity AI 服务 `NoSubscription` 和编辑器 `ScriptReloadProperties` 的字体引用警告；没有样例编译错误。未清空 Console。
- 基础验证：本样例技术检查通过；简易验证：未执行；完整验证：未执行；用户评估：待评估。未将旧的核心包测试结果当成本次重跑结果。

截图与机器可读记录位于项目 `outputs/coin-code-comparison/`；过程截图位于 `work/coin-code-comparison/`。核心 package 源码及原有演示场景没有修改。
