**简体中文** | [English](README-EN.md) | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Horizon

![GitHub Release](https://img.shields.io/github/v/release/backspace135/PCL-CE?label=release&logo=github&style=for-the-badge)
[![Issues](https://img.shields.io/github/issues/backspace135/PCL-CE?style=for-the-badge&label=issues&labelColor=444444&color=1F883D&logo=github)](https://github.com/backspace135/PCL-CE/issues)

[下载](https://github.com/backspace135/PCL-CE/releases/latest) |
[提交问题](https://github.com/backspace135/PCL-CE/issues/new/choose) |
[Horizon](https://github.com/horizon-rust/horizon)

</div>

PCL Horizon 是 [Horizon](https://github.com/horizon-rust/horizon) 的启动器。Horizon 是用 Rust 重写的 Minecraft: Java Edition，不需要 JVM，有自己的模组系统。

本启动器分支自 [PCL Community Edition](https://github.com/PCL-Community/PCL-CE)，沿用 PCL 的界面，但删除了 Java 版启动链、模组加载器与第三方账户等与 Horizon 无关的部分。请不要向 PCL 或 PCL CE 反馈 PCL Horizon 的问题。

## ✨ 功能

- **账户**：默认使用离线档案，不需要账户就能启动；也可以登录微软账户，每次启动前会确认账户拥有 Java 版。不支持第三方验证服务器。
- **Horizon 构建管理**：从 [horizon-rust/horizon](https://github.com/horizon-rust/horizon/releases) 的 Nightly 发布获取构建，安装前校验 `SHA256SUMS` 与 Ed25519 签名，每个构建独立存放。
- **自动准备**：下载 Horizon 需要的原版文件与 Java，调用 `horizon-prepare` 准备资源，同一构建的所有实例共用准备结果。
- **实例**：每个实例有独立的存档、资源包、光影、模组与设置，可以固定使用某个构建。
- **内容**：
  - 模组与光影来自 [Horizon 内容注册表](https://horizon-rust.github.io/horizon-mods/)，安装前校验 SHA-256、minisign 签名和撤销列表；
  - 资源包与数据包来自 [Modrinth](https://modrinth.com/)。
- **崩溃处理**：读取 Horizon 生成的崩溃报告。
- **启动器更新**：通过本仓库的 [GitHub Releases](https://github.com/backspace135/PCL-CE/releases) 更新。

## 💻 支持平台

| 操作系统 | 支持情况 | 环境要求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高，x64 | ✅ 支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| 更早的 Windows | ❌ 不支持 | / |
| macOS / Linux | ⚠️ 只能编译，不能运行 | [.NET 11 SDK](https://get.dot.net/11) |

PCL Horizon 是 WPF 程序，只能在 Windows 上运行。在 macOS 与 Linux 上可以编译源代码，但需要把编译产物拿到 Windows 上测试。

## 🛠️ 开发

```shell
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug -p:Platform=x64
```

移植方案与 Horizon 启动规范的要点见 [docs/horizon-launcher-port.md](docs/horizon-launcher-port.md)。仓库中的 `horizon-core/` 是 Horizon 的可移植引擎库，与启动器无关。

## 🙏 致谢

- [PCL](https://github.com/Meloong-Git/PCL)：龙腾猫跃开发的 Plain Craft Launcher 2
- [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) 及其所有[贡献者](https://github.com/PCL-Community/PCL-CE/graphs/contributors)
- [Horizon](https://github.com/horizon-rust/horizon)

## 🔒 许可证

- `Plain Craft Launcher 2/` 使用 [自定义许可证](Plain%20Craft%20Launcher%202/LICENCE)
- `其余所有目录` 使用 [Apache License 2.0](LICENSE)
