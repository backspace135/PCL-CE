[简体中文](README.md) | [English](README-EN.md) | **繁體中文**

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Horizon

![GitHub Release](https://img.shields.io/github/v/release/backspace135/PCL-CE?label=release&logo=github&style=for-the-badge)
[![Issues](https://img.shields.io/github/issues/backspace135/PCL-CE?style=for-the-badge&label=issues&labelColor=444444&color=1F883D&logo=github)](https://github.com/backspace135/PCL-CE/issues)

[下載](https://github.com/backspace135/PCL-CE/releases/latest) |
[回報問題](https://github.com/backspace135/PCL-CE/issues/new/choose) |
[Horizon](https://github.com/horizon-rust/horizon)

</div>

PCL Horizon 是 [Horizon](https://github.com/horizon-rust/horizon) 的啟動器。Horizon 是以 Rust 重寫的 Minecraft: Java Edition，不需要 JVM，並有自己的模組系統。

本啟動器分支自 [PCL Community Edition](https://github.com/PCL-Community/PCL-CE)，沿用 PCL 的介面，但移除了 Java 版啟動流程、模組載入器與第三方帳號等與 Horizon 無關的部分。請不要向 PCL 或 PCL CE 回報 PCL Horizon 的問題。

## ✨ 功能

- **帳號**：預設使用離線檔案，不需要帳號就能啟動；也可以登入微軟帳號，每次啟動前會確認帳號擁有 Java 版。不支援第三方驗證伺服器。
- **Horizon 建置管理**：從 [horizon-rust/horizon](https://github.com/horizon-rust/horizon/releases) 的 Nightly 發布取得建置，安裝前校驗 `SHA256SUMS` 與 Ed25519 簽章，每個建置獨立存放。
- **自動準備**：下載 Horizon 需要的原版檔案與 Java，呼叫 `horizon-prepare` 準備資源，同一建置的所有實例共用準備結果。
- **實例**：每個實例有獨立的存檔、資源包、光影、模組與設定，可以固定使用某個建置。
- **內容**：
  - 模組與光影來自 [Horizon 內容登錄表](https://horizon-rust.github.io/horizon-mods/)，安裝前校驗 SHA-256、minisign 簽章與撤銷清單；
  - 資源包與資料包來自 [Modrinth](https://modrinth.com/)。
- **當機處理**：讀取 Horizon 產生的當機報告。
- **啟動器更新**：透過本儲存庫的 [GitHub Releases](https://github.com/backspace135/PCL-CE/releases) 更新。

## 💻 支援平台

| 作業系統 | 支援情況 | 環境需求 |
|---|---|---|
| Windows 10 1809 (17763) 或更新版本，x64 | ✅ 支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| 更早的 Windows | ❌ 不支援 | / |
| macOS / Linux | ⚠️ 只能編譯，無法執行 | [.NET 11 SDK](https://get.dot.net/11) |

PCL Horizon 是 WPF 程式，只能在 Windows 上執行。在 macOS 與 Linux 上可以編譯原始碼，但需要把編譯產物拿到 Windows 上測試。

## 🛠️ 開發

```shell
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug -p:Platform=x64
```

移植方案與 Horizon 啟動規範的重點見 [docs/horizon-launcher-port.md](docs/horizon-launcher-port.md)（簡體中文）。儲存庫中的 `horizon-core/` 是 Horizon 的可攜引擎函式庫，與啟動器無關。

## 🙏 致謝

- [PCL](https://github.com/Meloong-Git/PCL)：龍騰貓躍開發的 Plain Craft Launcher 2
- [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) 及其所有[貢獻者](https://github.com/PCL-Community/PCL-CE/graphs/contributors)
- [Horizon](https://github.com/horizon-rust/horizon)

## 🔒 授權條款

- `Plain Craft Launcher 2/` 使用 [自訂授權條款](Plain%20Craft%20Launcher%202/LICENCE)
- `其餘所有目錄` 使用 [Apache License 2.0](LICENSE)
