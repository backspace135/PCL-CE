# 把 PCL-CE 改成 Horizon 启动器：精简与移植方案

更新于 2026-11-16。Horizon 规格依据 `horizon-rust/horizon` 主仓库 `85b5bcca7c` 的 `docs/api/`（Launch API 0.1.x，实验性）。

## 背景

- Horizon 是用 Rust 重写的 Minecraft: Java Edition 26.3。游戏本身不用 JVM，有自己的模组系统，不支持 Forge、Fabric、OptiFine 等。
- 本仓库的 `horizon-core/` 是 Horizon 的可移植引擎库，跟启动无关。启动器要对接的是 Horizon 发布包里的 `horizon-prepare` 和 `horizon-client` 两个可执行文件。
- PCL-CE 是 WPF 程序，只能在 Windows 上运行，所以改出来的启动器只覆盖 Windows x64 和 ARM64。

## 一、Horizon 对启动器的要求

完整说明见 Horizon 主仓库的 `docs/api/launch-guide.md`（按步骤写）、`launch.md`（接口约定）、`launch-reference.md`（参数）和 `content.md`（模组、资源包）。下面是要点。

1. **只为正版账户启动。** 启动器负责微软登录（device code → Xbox Live → XSTS → Minecraft），并在每次启动前检查账户是否拥有 Java 版。必须用自己的 Azure client ID，不能用 Horizon 的。
2. **选版本、下载、验签。**
   - 版本来自 GitHub release `horizon-rust/horizon`。最新版的标签是 `nightly`，每个版本还会以 `nightly-<date>` 标签保存 7 天，然后删除。
   - Windows 包是 `horizon-windows-x64.zip` 和 `horizon-windows-arm64.zip`。
   - 解压前先校验 `SHA256SUMS`，再用 Ed25519 验 `SHA256SUMS.v2.sig`，签名内容是 `horizon release nightly-<date>-<commit>\n` 加上 `SHA256SUMS`。公钥在 `crates/horizon-release-sign/release-keys.txt`。v2 签名的格式目前只写在 `crates/horizon-release-sign/src/lib.rs` 中，`docs/api` 还没收录。
   - 每个版本解压到独立目录并保持完整。
3. **查询能力。** `horizon-client --launch-api` 和 `horizon-prepare --launch-api` 各输出一个 JSON，从中读取 `launch_api`、`minecraft`（需要的原版版本）、`java_major` 和 `options`。没列出的参数会被拒绝，退出码为 2。
4. **准备原版文件和 Java。** 需要 26.3 的版本 JSON、client jar、libraries 和 assets，第一次还要从版本 JSON 的 `downloads.server` 下载 server jar。另外需要 Java 25（只在准备阶段用）。所有文件按 `research-lock.json` 校验。
5. **准备资源。**
   - 命令：`horizon-prepare --prepare --minecraft-dir D | (--version-json --client-jar --libraries-dir --assets-dir) --server-jar J --java-home H --output PREPARED --complete --progress json`。
   - stdout 每行是一个 JSON：`{"message"}`、`{"fraction"}` 或 `{"error"}`。退出码 0 表示成功，1 表示失败，2 表示参数错误。
   - 首次准备需要几分钟。同一个版本的所有实例共用一个 PREPARED 目录。
6. **每次启动前检查。** 运行 `horizon-prepare --check --output PREPARED`，不通过就重新准备。
7. **启动。**
   - 命令：`horizon-client --resources PREPARED --game-dir 实例 --home 实例 --width W --height H [--fullscreen] [--quickPlaySingleplayer 世界 | --quickPlayMultiplayer 主机:端口] --session-stdin`。
   - 会话 JSON `{"name","uuid","access_token","user_type":"msa","xuid"?}` 从 stdin 传入。
   - 没有 JVM 参数，也没有内存设置。
8. **结束后处理。**
   - 日志在 `<home>/logs/latest.log`。
   - 退出码 0 表示正常退出，1 表示出错，2 表示参数错误，101 表示 panic。
   - 退出码为 1 或 101 时，stderr 最后一行是 `Horizon: crash report saved to <path>`。
9. **内容管理。**

   | 类型 | 位置 | 开关方式 |
   |---|---|---|
   | 模组 `.hmod` | `horizon-mods/` | `horizon-mods.txt` 中的 `mod.<id>:true` |
   | 光影 `.hshader` | `shaderpacks/` | `horizon-graphics.txt` 中的 `pickedShaderPack:` |
   | 资源包 | `resourcepacks/` | `options.txt` |
   | 数据包 | `saves/<w>/datapacks/` | — |

   - 模组注册表是 `https://horizon-rust.github.io/horizon-mods/index.json`。安装前要校验 SHA-256、minisign 签名和 `revoked[]` 列表，文件不超过 64 MiB。
   - Modrinth 只能提供资源包和数据包，不能提供 Java 模组。CurseForge 不支持。

## 二、PCL-CE 现状与对应关系

PCL-CE 现有的原版下载、Java 检测与下载、微软登录正好满足第 1、4 步，**要保留**。要拆掉的是 JVM 启动链和所有模组加载器相关的部分。

现有启动链：`PCL.Launcher.Core/Launch/Core.cs` 中的 `McLaunchStart` → GetJava → Login → `DlClientFix` → `McLaunchArgumentMain`（`Arguments*.cs`）→ Natives → `Run.cs` 中的 `_CreateGameProcessStartInfo`（启动 javaw）→ Watcher → CrashAnalysis。

`McLaunchStart` 有 6 个调用点：
- `Plain Craft Launcher 2/Pages/PageLaunch/PageLaunchLeft.xaml.cs`
- `Plain Craft Launcher 2/Pages/PageInstance/PageInstanceOverallViewModel.cs`（2 处）
- `Plain Craft Launcher 2/Pages/PageInstance/PageInstanceSavesViewModel.cs`
- `Plain Craft Launcher 2/Pages/PageInstance/ServerCard.xaml.cs`
- `PCL.Launcher.Core/Event/CustomEvent.cs`

耦合情况：App 层 281 个 .cs 文件中有 87 个引用了 `McInstance`、`InstanceList`、`ModLoaderType`、`GameDownload` 或 Forge、Fabric、OptiFine 相关类型。最重的是 `PageInstanceInstall.xaml.cs` 和 `PageDownloadInstall.xaml.cs`。

## 三、清单

### 保留（通用基础设施）

- `PCL.Core.SourceGenerators`，以及 `PCL.Core.Wpf` 的控件、主题、动画、弹窗和多语言。
- `PCL.Core`：`App/`（IoC、配置、i18n、任务、单实例）、`IO/`、`Logging/`、`Platform/`、`UI/`、`Utils/`、`ViewModel/`。
- `PCL.Launcher.Core`：`Loading/`（LoaderTask 进度链）、`Network/` 下载引擎、`Updates/`（端点换掉）、`Ui/` 接口。
- 应用层：`Controls/`（`MinecraftServer*` 除外）、`Modules/Base`、`Modules/UI`、`FormMain` 外壳；设置页中的界面、日志、语言、杂项、更新和关于。

### 保留但裁剪

| 模块 | 处理 |
|---|---|
| `PCL.Core/Minecraft` | 留微软 OAuth（换成自己的 `MS_CLIENT_ID`，加正版检查）、Java 扫描和 NBT 读取。删掉 Yggdrasil/Authlib 外置登录和离线账户 |
| `PCL.Launcher.Core/Download` | 只留原版 client 下载，版本由 `--launch-api` 的 `minecraft` 字段决定，另加 server jar 下载。删掉 Forge、NeoForge、Cleanroom、Fabric、LegacyFabric、OptiFine、LiteLoader、LabyMod 安装器 |
| Java 下载 | 保留，版本由 `java_major` 决定（目前是 25） |
| 实例子页：存档、截图、资源包、服务器列表 | 保留界面，底层改用 `HorizonInstance` |

### 改写

| 现有 | 改成 |
|---|---|
| `Launch/` 整条 JVM 启动链 | `HorizonLaunch`：检查版本 → `--check`（不通过则准备）→ 获取会话 → 拼 `horizon-client` 参数 → 通过 stdin 写入会话 → 等待退出 → 处理日志和崩溃 |
| `McInstance` / `InstanceList` | 新写 `HorizonInstance`，包含实例目录、绑定的 Horizon 版本和窗口参数。不要去改 McInstance |
| 版本管理 | `HorizonReleaseService`：拉取 release 列表（GitHub API，失败时用 `https://mcp.playhorizon.dev/github/public`）→ 下载 → 校验 SHA-256 和 Ed25519 → 解压到 `versions/<tag>/` |
| — | `HorizonPrepareService`：封装 `horizon-prepare`，把 JSON 进度接到现有 LoaderTask 进度条上 |
| `Minecraft/CrashAnalysis`（按 Java 日志规则匹配） | 从 stderr 最后一行读取崩溃报告路径，展示并支持导出。删掉规则库 |
| `Comp`（Modrinth、CurseForge） | 模组改用 Horizon 注册表；Modrinth 只用于资源包和数据包；删掉 CurseForge |
| `LocalComp` | 改为读写 `horizon-mods/`、`horizon-mods.txt` 和 `shaderpacks/` |
| 品牌标识 | `metadata.json`、图标、`PCL.Core/App/Paths.cs` 中的 `PCLCE` 目录名、`HKCU\Software\PCL`、AUMID（`AumidHelper.cs`）、`Updates/UpdateManager.cs` 更新端点、四个语言 XAML 中的 `ce.pclc.cc` 链接 |

### 删除

- `PCL.Launcher.Core/Modpack`。`.mrpack` 转换需要 `horizon-port-pack`，以后再做。
- `CompDependency`；`Minecraft/` 中的 JarInJar、Library 和 natives 相关代码；`Launch/Arguments*.cs`。
- `PCL.Core/Link`（EasyTier、大厅、Natayark/NAID）、`Network/WebServer/NaidCallbackServer.cs`、`Modules/GameLink`、`Pages/PageTools`，以及设置页的 GameLink 和 Feedback。
- 主页新闻：`Model/Homepage`、`ViewModel/Homepage`、`PageHomepage`。
- `Modules/Music`、`AprilFools.cs`、`PromoteService`、`McLaunchPrecheck` 中的赞助提示。Sentry 遥测要么删掉，要么换成自己的 DSN。
- `Resources/` 下的 `java-wrapper.jar`、`legacyfix.jar`、`forge-installer.jar`、`transformer.jar`、`lwjgl-unsafe-agent.jar`、log4j2 XML、`linkd.exe`、`mcmod.buf`。
- 页面：`PageDownload` 中各模组加载器和 Comp 的页面；`PageInstance` 中的加载器安装、整合包导出和 JarInJar；`PageLaunch` 中的 Authlib 登录；设置页的 JVM 参数和内存设置。
- 构建期密钥：`CURSEFORGE_API_KEY`、`NAID_CLIENT_ID`、`NAID_CLIENT_SECRET`、`LINK_SERVER_ROOT`。

## 四、步骤

从外往里删，每一步都要能编译通过，每个模块单独提交。

1. **换品牌标识。** 改名称、图标、目录名、注册表键、AUMID 和更新端点，确保能编译通过。
2. **删外围功能。** 依次删 Link、主页、音乐、愚人节、工具页、整合包、CurseForge。
3. **删模组加载器。** 先删页面（`PageDownloadInstall`、`PageInstanceInstall`），再删 `Download/` 里的安装器。下载页只保留原版。
4. **新增 `PCL.Launcher.Core/Horizon/`。** 包括 ReleaseService、PrepareService、LaunchService 和 HorizonInstance，用单元测试和命令行先跑通。
5. **切换启动链。** 把 6 个 `McLaunchStart` 调用点改接 HorizonLaunch，然后删掉旧的 `Launch/`、JVM 参数和 natives 代码。
6. **改写实例页。** 包括实例列表与选择、实例设置、存档、资源包、截图和服务器列表。
7. **内容管理。** 接 Horizon 模组注册表、光影，以及 Modrinth 资源包和数据包。

完成后代码规模预计减少四到五成。主要工作量在第 4 到第 6 步。

## 五、待定

- **Minecraft 文件来源。** 由启动器给正版账户下载（复用原版下载），还是从官方启动器目录导入，或者两种都支持。
- **Nightly 只保留 7 天。** 是否在本地保留旧版本作为回退。
- **离线账户。** Horizon 的规范要求只为正版账户启动。本方案默认删除离线账户。
- **`horizon-core/` 的用途。** 启动器目前用不到它。以后如果要在启动器里读取存档或 NBT，可以考虑通过 FFI 复用。
