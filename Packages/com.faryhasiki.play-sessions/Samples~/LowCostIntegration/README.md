# 新旧代码接入对照

`NewWallet` 每次 Play 归零，只增加一个字段标记。`OldWallet` 没有本包相关属性，通过同文件示例中的独立 `LegacyAdapter` 接入，恢复为 10。

程序集声明一次 PlaySessionAssembly；适配器通过 assembly 属性发现。两个 CollectCoin 业务方法完全一致。真实项目可将适配器放在单独的集成目录或程序集。
