# horizon-core

Horizon 的可移植核心 crate，从 Horizon 仓库（`backspace135/horizon` @ `85b5bcca7c`）中拆分出来。
它们包含原版 Minecraft: Java Edition 26.3 的规则和数据，不含窗口、GPU、声音、手柄、剪贴板或 HTTP 客户端，
因此可以在任何目标平台上编译。

| Crate | 负责内容 |
|---|---|
| `horizon-core` | 模拟基础组件：Java `Random`、`jmath`（与 HotSpot 结果一致的 `Math`）、坐标、噪声、密度函数、调色板存储、固定的游戏版本 |
| `horizon-world` | 不含游戏循环的世界数据：NBT、区域文件、区块、`datafix/`、`worldgen/`、`level/`（方块行为、刻、红石）、实体物理、状态效果、饥饿、伤害 |
| `horizon-protocol` | 网络协议 777：编解码、分帧、加密、数据包 |
| `horizon-items` | 方块与物品：方块属性、物品堆叠与组件、附魔、冷却、收纳袋 |
| `horizon-command` | 命令解析（Brigadier）：原版命令树、参数类型、目标选择器、补全建议 |
| `horizon-client-core` | 文本、语言、SNBT、NBT 路径、文本组件、记分板、Boss 栏、游戏规则 |
| `horizon-assets` | 资源解码：方块状态与物品模型、模型烘焙、纹理图集 |
| `horizon-threads`、`horizon-guest-stack` | 工作线程调度、采样以及栈空间预留 |
| `horizon-java-properties` | Java properties 文件 |
| `horizon-mod-runtime` | 与引擎无关的模组存储和附加数据索引（`horizon-protocol` 依赖它） |

## 与 Horizon 仓库的差异

- `horizon-items` 用自己的 `enabled_mods` 模块读取已启用模组快照（`horizon-enabled-mods.json`），
  而不依赖 `horizon-mod-package`，因为后者的构建会引入模组 API 生成器和游戏源码。文件格式相同。
- `horizon-client-core` 内嵌的翻译文件位于 `crates/horizon-client-core/assets/horizon/lang/`
  （在 Horizon 中为 `crates/horizon-client/assets/horizon/lang/`）。
- `horizon-guest-stack` 的工作线程审计只检查 `horizon-threads`；游戏本身的工作线程在 Horizon 中审计。
- `horizon-client-core` 的语言键测试只检查其自身源码中出现的 `horizon.*` 键；Horizon 检查的是游戏客户端各 crate。

## 构建与测试

```sh
cargo check --workspace --all-targets
cargo test --workspace
```

需要原版数据的测试会在 `.local/horizon-client/26.3` 中查找数据（用 Horizon 的 `horizon-prepare`
从你自己的 Minecraft 安装中生成）。切勿提交 Mojang 的文件。

## 许可证

LGPL-3.0-or-later，见 [LICENSE](LICENSE)。
