# SRC-02 / 3D Snake

## 来源和本地案例路径

- [仓库](https://github.com/striderzz/Snake-Game-Unity-Project)，MIT，提交 `95e45d8d4b6c35998b05df4588063244b50a30c6`。
- 完整本地工程：[outputs/test-cases/projects/Snake](../../outputs/test-cases/projects/Snake/)。
- 场景：[Level.unity](../../outputs/test-cases/projects/Snake/Assets/Scenes/Level.unity)、[Menu.unity](../../outputs/test-cases/projects/Snake/Assets/Scenes/Menu.unity)。
- 原 Unity 6000.2.2f1 / URP 17.2.0；本工程为 6000.3.8f1 / URP 17.3.0，差异需要在独立副本核验。
- 当前状态：完整源码与许可证已下载，**未接入、未运行**。这是 GitHub 补充筛选的案例，并非此前 B 站贪吃蛇教程的配套仓库。

用途：网格驱动的 3D 贪吃蛇，食物增长、尖刺与墙体碰撞、死亡时添加物理组件、重开场景。

## 源码修改入口与适配边界

| 文件 | 测试关注点 | 后续修改范围（有实际需要才改） |
| --- | --- | --- |
| [GameManager.cs](../../outputs/test-cases/projects/Snake/Assets/Scripts/GameManager.cs) | 静态懒创建 Instance、Score、IsGameOver、UI 引用 | 定义实例的 reset/stop 入口；OnDestroy 仅在自身仍为 owner 时清引用；接会话框架时只注册一个业务入口 |
| [SnakeMovement.cs](../../outputs/test-cases/projects/Snake/Assets/Scripts/SnakeMovement.cs) | cells/segments、方向、移动计时；死亡时实例化材质并加刚体/碰撞体 | 如增加场景内重开，集中重置这些字段，销毁归自己持有的段与材质；保留移动/碰撞算法 |
| [Events.cs](../../outputs/test-cases/projects/Snake/Assets/Scripts/Events.cs) | Menu / Level / ResetGame 调用场景加载 | 保留按钮语义，记录场景内重开与退出后再次 Play 的独立用例 |
| [GenerateFood.cs](../../outputs/test-cases/projects/Snake/Assets/Scripts/GenerateFood.cs)、[GenerateSpike.cs](../../outputs/test-cases/projects/Snake/Assets/Scripts/GenerateSpike.cs) | 随机布局、生成对象集合、与蛇的位置冲突 | 为测试增加固定布局/种子入口；退出时只清理自己的实例，不能全局扫除所有对象 |

该项目的 singleton 使用 Unity 对象判空，销毁后的引用可能被正确识别并重建。**不能仅因它是 static 就声称 Fast Play 已失败或必须改包。** 先测原版；需要改动时才落到以上文件，并与原版比较。

## 测试要点和通过标准

| ID | 操作 | 通过时应得到什么 | 当前结果 |
| --- | --- | --- | --- |
| SNAKE-01 | 固定输入走格、反向输入、吃一个食物、撞墙/尖刺/自身 | 每步只进一格；不允许 180° 反向；吃一次长一节且 Score+1；碰撞只触发一次 GameOver | 未运行 |
| SNAKE-02 | 死亡后点 ResetGame，循环 3 次 | 新场景 Score=0、IsGameOver=false；cells 和 segments 都为 StartSize+1；HUD/死亡面板状态正确 | 未运行 |
| SNAKE-03 | 正常重载基线 + 3 次 Fast Play | 每轮只有一个活 GameManager；无旧 UI 引用；初始蛇长/计时/方向正确，输入一次只走预期步数 | 未运行 |
| SNAKE-04 | 多次死亡、重开、退出，观察物理段和实例化材质 | 本案例的段、刚体、碰撞体与自建材质计数回到各阶段基线；退出后无案例对象残留 | 未运行 |

通过标准中的 StartSize+1 来自源码 BuildSnake 的包含头部循环，不能写成“蛇长必为 5”。断言以场景实际 StartSize 配置为准。

## 为什么保留这个案例

它的 Unity 版本较新，玩法小，便于观察最小源码改动能否换来清晰的生命周期边界；也可以得到“原版已正常，无需适配”的有效结论。相较 2048，它更适合先完成一次完整工程接入和用户评估。
