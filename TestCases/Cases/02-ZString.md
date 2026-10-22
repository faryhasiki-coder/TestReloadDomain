# PKG-02 / ZString

## 包与来源

- 包名：`com.cysharp.zstring`；选定版本：`2.6.0`，MIT。
- 用途：低分配字符串构建，UTF-16 / UTF-8 缓冲，预编译格式化模板和 TMP 输出。
- [版本包清单](https://github.com/Cysharp/ZString/blob/2.6.0/src/ZString.Unity/Assets/Scripts/ZString/package.json)；[构建器源码](https://github.com/Cysharp/ZString/blob/2.6.0/src/ZString/Utf16ValueStringBuilder.cs)；[发布页](https://github.com/Cysharp/ZString/releases/tag/2.6.0)。
- 固定 UPM 地址：`https://github.com/Cysharp/ZString.git?path=src/ZString.Unity/Assets/Scripts/ZString#2.6.0`。
- 当前状态：已读源码，**未安装、未运行**。拟落点 `Assets/ZStringValidation/` 与 `Assets/Integrations/ZString/`，尚未创建。

## 适配器方案

不重置 ZString 全部静态字段。短期 builder 使用 using/finally 当次释放，避免跨 await 持有 notNested=true 的线程缓冲；不可变 prepared-format 和供应商缓冲可复用。

需要适配的是业务注册的自定义 formatter：为自己使用的 T 保存 `Utf16ValueStringBuilder.FormatterCache<T>.TryFormatDelegate` 原值，注册新委托，结束时恢复；UTF-8 独立处理。不要让委托永久捕获场景对象。固定版本源码公开了委托槽和 RegisterTryFormat，可用公开 API 完成，不需要反射。

计划适配器名 `ZStringFormatterAdapter`；在当前分支可用独立 IDisposable 管理注册，未来接 Play Sessions 时只把恢复操作挂到 scope 的停止/释放入口。恢复前须确认槽仍是自己安装的委托，避免覆盖另一拥有者后来的注册；同一类型并发注册应明确禁止或建立所有权协议。

## 测试要点与通过标准

| ID | 测试步骤 | 通过时的可观察结果 | 实际结果 |
| --- | --- | --- | --- |
| ZS-01 | 固定输入 `score=42`，UTF-16 / UTF-8 格式化；含中英文和空输入 | 文本、UTF-8 解码结果逐字匹配基线；边界输入不串入上一轮内容 | 未运行 |
| ZS-02 | 为案例独有类型注册格式化委托 A，结束后换会话 B | B 仅输出 B 的内容；恢复后委托等于原值；无旧场景闭包残留 | 未运行 |
| ZS-03 | 正常、抛异常两条 using/finally 路径释放 builder；随后再次创建 | 下一次构建成功，无 NestedStringBuilderCreationException；写入长度/结果正确 | 未运行 |
| ZS-04 | 正常重载基线 + 连续 3 次 Fast Play，反复注册、格式化、退订恢复 | 每轮输出相同，业务注册数归零，没有旧委托被再次调用 | 未运行 |

这些是通过标准，不是实测数字。不得因为 README 宣传低分配就写“零 GC 测试通过”；池缓存保留也不等于泄漏。

## 接入前检查

2.6.0 的包清单最低版本字段是 2018.4，当前 master README 又列出更新的依赖说明；以固定 tag 的实际编译和 DLL 依赖为准。该版本 asmdef 包含 Unity.TextMeshPro 引用和 uGUI 2.0.0 的版本定义，需在本项目 Unity 6 环境核对。

如后续安装，先核对项目已有 Unsafe/相关 DLL，再选择局部或全局范围。常规 UPM 会修改本工程 Packages 清单并在 Library/PackageCache 解析代码；UPM 还可能写入 Unity 自身缓存。本轮没有开始安装，也没有用最新 README 代替 2.6.0 验证。
