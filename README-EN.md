[简体中文](README.md) | **English** | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Horizon

![GitHub Release](https://img.shields.io/github/v/release/backspace135/PCL-CE?label=release&logo=github&style=for-the-badge)
[![Issues](https://img.shields.io/github/issues/backspace135/PCL-CE?style=for-the-badge&label=issues&labelColor=444444&color=1F883D&logo=github)](https://github.com/backspace135/PCL-CE/issues)

[Download](https://github.com/backspace135/PCL-CE/releases/latest) |
[Report an issue](https://github.com/backspace135/PCL-CE/issues/new/choose) |
[Horizon](https://github.com/horizon-rust/horizon)

</div>

PCL Horizon is a launcher for [Horizon](https://github.com/horizon-rust/horizon), a Rust rewrite of Minecraft: Java Edition that runs without a JVM and has its own mod system.

It is forked from [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) and keeps the PCL interface, but the Java Edition launch chain, mod loaders and third-party accounts have been removed. Please do not report PCL Horizon issues to PCL or PCL CE.

## ✨ Features

- **Accounts**: An offline profile is used by default, so no account is needed to launch. You can also sign in with a Microsoft account; ownership of Java Edition is then checked before every launch. Third-party authentication servers are not supported.
- **Horizon builds**: builds come from the Nightly releases of [horizon-rust/horizon](https://github.com/horizon-rust/horizon/releases). `SHA256SUMS` and its Ed25519 signature are verified before a build is installed, and each build lives in its own directory.
- **Automatic preparation**: the launcher downloads the vanilla files and Java that Horizon needs and runs `horizon-prepare`. All instances of a build share the prepared resources.
- **Instances**: each instance has its own worlds, resource packs, shaders, mods and settings, and can be pinned to a build.
- **Content**:
  - mods and shaders come from the [Horizon content registry](https://horizon-rust.github.io/horizon-mods/); SHA-256, minisign signatures and the revocation list are checked before installing;
  - resource packs and data packs come from [Modrinth](https://modrinth.com/).
- **Crash handling**: shows the crash reports written by Horizon.
- **Launcher updates**: delivered through the [GitHub Releases](https://github.com/backspace135/PCL-CE/releases) of this repository.

## 💻 Supported platforms

| Operating system | Support | Requirements |
|---|---|---|
| Windows 10 1809 (17763) or later, x64 | ✅ Supported | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Older Windows | ❌ Not supported | / |
| macOS / Linux | ⚠️ Build only, cannot run | [.NET 11 SDK](https://get.dot.net/11) |

PCL Horizon is a WPF application and only runs on Windows. The source can be built on macOS and Linux, but the output has to be tested on Windows.

## 🛠️ Development

```shell
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug -p:Platform=x64
```

See [docs/horizon-launcher-port.md](docs/horizon-launcher-port.md) (Chinese) for the port plan and a summary of Horizon's launch specification. `horizon-core/` in this repository is Horizon's portable engine library and is not part of the launcher.

## 🙏 Credits

- [PCL](https://github.com/Meloong-Git/PCL): Plain Craft Launcher 2 by LTCat
- [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) and all of its [contributors](https://github.com/PCL-Community/PCL-CE/graphs/contributors)
- [Horizon](https://github.com/horizon-rust/horizon)

## 🔒 License

- `Plain Craft Launcher 2/` is under a [custom license](Plain%20Craft%20Launcher%202/LICENCE)
- `All other directories` are under the [Apache License 2.0](LICENSE)
