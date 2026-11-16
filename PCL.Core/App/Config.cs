using PCL.Core.App.Configuration;

namespace PCL.Core.App;

/// <summary>
/// 全局配置类。
/// </summary>
// ReSharper disable InconsistentNaming
public static partial class Config
{
    /// <summary>
    /// 系统配置。
    /// </summary>
    [ConfigGroup("System")] partial class SystemConfigGroup
    {
        // /// <summary>
        // /// 系统缓存目录。
        // /// </summary>
        // [ConfigItem<string>("SystemSystemCache", "")] public partial string CacheDirectory { get; set; }

        /// <summary>
        /// 禁用硬件加速。
        /// </summary>
        [ConfigItem<bool>("SystemDisableHardwareAcceleration", false)] public partial bool DisableHardwareAcceleration { get; set; }

        /// <summary>
        /// 实时日志最大行数。
        /// </summary>
        [ConfigItem<int>("SystemMaxLog", 13)] public partial int MaxGameLog { get; set; }

        [ConfigItem<string>("Profile", "{\"lastUsed\":-1,\"profiles\":[]}", ConfigSource.SharedEncrypt)] public partial string Profiles { get; set; }
    }

    /// <summary>
    /// 网络配置。
    /// </summary>
    [ConfigGroup("Network")] partial class NetworkConfigGroup
    {
        [ConfigItem<bool>("SystemNetEnableDoH", true)] public partial bool EnableDoH { get; set; }

        [ConfigGroup("HttpProxy")] partial class HttpProxyConfigGroup
        {
            [ConfigItem<string>("SystemHttpProxy", "", ConfigSource.SharedEncrypt)] public partial string CustomAddress { get; set; }
            [ConfigItem<int>("SystemHttpProxyType", 1)] public partial int Type { get; set; }
            [ConfigItem<string>("SystemHttpProxyCustomUsername", "")] public partial string CustomUsername { get; set; }
            [ConfigItem<string>("SystemHttpProxyCustomPassword", "")] public partial string CustomPassword { get; set; }
        }
    }

    /// <summary>
    /// 调试配置
    /// </summary>
    [ConfigGroup("Debug")] partial class DebugConfigGroup
    {
        [ConfigItem<bool>("SystemDebugMode", false)] public partial bool Enabled { get; set; }
        [ConfigItem<int>("SystemDebugAnim", 9)] public partial int AnimationSpeed { get; set; }
        [ConfigItem<bool>("SystemDebugDelay", false)] public partial bool AddRandomDelay { get; set; }
        [ConfigItem<bool>("SystemDebugSkipCopy", false)] public partial bool DontCopy { get; set; }
        [ConfigItem<bool>("SystemDebugAllowRestrictedFeature", false)] public partial bool AllowRestrictedFeature { get; set; }
    }

    /// <summary>
    /// 下载配置。
    /// </summary>
    [ConfigGroup("Download")] partial class DownloadConfigGroup
    {
        // 保留原配置键，升级后仍沿用用户已设置的数值（值 + 1 为连接上限）。
        [ConfigItem<int>("ToolDownloadThread", 63)] public partial int ThreadLimit { get; set; }
        [ConfigItem<int>("ToolDownloadFileConnection", 7)] public partial int FileConnectionLimit { get; set; }
        [ConfigItem<int>("ToolDownloadSpeed", 42)] public partial int SpeedLimit { get; set; }
        [ConfigItem<DownloadHttpMode>("ToolDownloadHttpMode", DownloadHttpMode.Auto)] public partial DownloadHttpMode HttpMode { get; set; }
        [ConfigItem<int>("ToolDownloadSource", 1)] public partial int FileSource { get; set; }
        [ConfigItem<int>("ToolDownloadVersion", 1)] public partial int VersionListSource { get; set; }
        [ConfigItem<bool>("ToolDownloadAutoSelectVersion", true)] public partial bool AutoSelectInstance { get; set; }
        [ConfigItem<bool>("ToolFixAuthlib", true)] public partial bool FixAuthLib { get; set; }

        /// <summary>
        /// 第三方资源配置。
        /// </summary>
        [ConfigGroup("Comp")] partial class CompConfigGroup
        {
            [ConfigItem<bool>("ToolDownloadClipboard", false)] public partial bool ReadClipboard { get; set; }
            [ConfigItem<int>("ToolDownloadMod", 1)] public partial int CompSourceSolution { get; set; }
            [ConfigItem<int>("ToolDownloadQuickBehavior", 0)] public partial int QuickDownloadBehavior { get; set; }
        }
    }

    /// <summary>
    /// 工具配置。
    /// </summary>
    [ConfigGroup("Tool")] partial class ToolConfigGroup
    {
        [ConfigItem<bool>("ToolHelpChinese", true)] public partial bool AutoChangeLanguage { get; set; }
        // [ConfigItem<int>("ToolUpdateAlpha", 0, ConfigSource.SharedEncrypt)] public partial int Alpha { get; set; }
    }

    /// <summary>
    /// 更新配置。
    /// </summary>
    [ConfigGroup("Update")] partial class UpdateConfigGroup
    {
        /// <summary>
        /// 自动更新行为。
        /// </summary>
        [ConfigItem<LauncherAutoUpdateBehavior>("SystemSystemUpdate", LauncherAutoUpdateBehavior.DownloadAndAnnounce, ConfigSource.Local)] public partial LauncherAutoUpdateBehavior UpdateMode { get; set; }

        /// <summary>
        /// 更新分支。
        /// </summary>
        [ConfigItem<UpdateChannel>("SystemUpdateChannel", UpdateChannel.Release, ConfigSource.Local)] public partial UpdateChannel UpdateChannel { get; set; }
            
    }

    /// <summary>
    /// 个性化配置。
    /// </summary>
    [ConfigGroup("Preference")] partial class PreferenceConfigGroup
    {
        /// <summary>
        /// 启动时显示 Logo。
        /// </summary>
        [ConfigItem<bool>("UiLauncherLogo", true, ConfigSource.Local)] public partial bool ShowStartupLogo { get; set; }

        /// <summary>
        /// 锁定窗口大小。
        /// </summary>
        [ConfigItem<bool>("UiLockWindowSize", false)] public partial bool LockWindowSize { get; set; }

        /// <summary>
        /// 在启动游戏时显示你知道吗。
        /// </summary>
        [ConfigItem<bool>("UiShowLaunchingHint", true, ConfigSource.Local)] public partial bool ShowLaunchingHint { get; set; }

        /// <summary>
        /// 标题内容类型。
        /// </summary>
        [ConfigItem<LauncherTitleType>("UiLogoType", LauncherTitleType.Default, ConfigSource.Local)] public partial LauncherTitleType WindowTitleType { get; set; }

        /// <summary>
        /// 窗口标题文本。
        /// </summary>
        [ConfigItem<string>("UiLogoText", "", ConfigSource.Local)] public partial string WindowTitleCustomText { get; set; }

        /// <summary>
        /// 导航栏居左。
        /// </summary>
        [ConfigItem<bool>("UiLogoLeft", false, ConfigSource.Local)] public partial bool TopBarLeftAlign { get; set; }

        /// <summary>
        /// 全局字体。
        /// </summary>
        [ConfigItem<string>("UiFont", "", ConfigSource.Local)] public partial string Font { get; set; }

        /// <summary>
        /// MOTD 字体。
        /// </summary>
        [ConfigItem<string>("UiMotdFont", "", ConfigSource.Local)] public partial string MotdFont { get; set; }

        /// <summary>
        /// 本地化配置。
        /// </summary>
        [ConfigGroup("Localization", ConfigSource.Local)] partial class LocalizationConfigGroup
        {
            /// <summary>
            /// UI 语言。auto 表示跟随系统语言。
            /// </summary>
            [ConfigItem<string>("UiLanguage", "auto")] public partial string Language { get; set; }

            /// <summary>
            /// UI 展示格式所使用的区域性。auto 表示跟随系统区域格式。
            /// </summary>
            [ConfigItem<string>("UiFormatCulture", "auto")] public partial string FormatCulture { get; set; }

            /// <summary>
            /// 区域覆盖。auto 表示自动判断。
            /// </summary>
            [ConfigItem<string>("UiRegion", "auto")] public partial string Region { get; set; }
        }

        /// <summary>
        /// 界面主题配置。
        /// </summary>
        [ConfigGroup("Theme")] partial class ThemeConfigGroup
        {
            /// <summary>
            /// 配色主题模式。
            /// </summary>
            [ConfigItem<ColorMode>("UiDarkMode", ColorMode.System)] public partial ColorMode ColorMode { get; set; }

            /// <summary>
            /// 暗色配色主题。
            /// </summary>
            [ConfigItem<ColorTheme>("UiDarkColor", ColorTheme.CatBlue)] public partial ColorTheme DarkColor { get; set; }

            /// <summary>
            /// 亮色配色主题。
            /// </summary>
            [ConfigItem<ColorTheme>("UiLightColor", ColorTheme.CatBlue)] public partial ColorTheme LightColor { get; set; }

            /// <summary>
            /// 窗口透明度。
            /// </summary>
            [ConfigItem<int>("UiLauncherTransparent", 600, ConfigSource.Local)] public partial int WindowOpacity { get; set; }

            /// <summary>
            /// 自定义主题：色相 (H)。
            /// </summary>
            [ConfigItem<int>("UiLauncherHue", 180, ConfigSource.Local)] public partial int WindowHue { get; set; }

            /// <summary>
            /// 自定义主题：饱和度 (S)。
            /// </summary>
            [ConfigItem<int>("UiLauncherSat", 80, ConfigSource.Local)] public partial int WindowSat { get; set; }

            /// <summary>
            /// 自定义主题：明度 (L)。
            /// </summary>
            [ConfigItem<int>("UiLauncherLight", 20, ConfigSource.Local)] public partial int WindowLight { get; set; }

            /// <summary>
            /// 自定义主题：色相渐变。
            /// </summary>
            [ConfigItem<int>("UiLauncherDelta", 90, ConfigSource.Local)] public partial int WindowDelta { get; set; }

            /// <summary>
            /// 传说中的主题选择，但是没卵用。
            /// </summary>
            [ConfigItem<int>("UiLauncherTheme", 0, ConfigSource.Local)] public partial int ThemeSelected { get; set; }
        }

        /// <summary>
        /// 背景内容。
        /// </summary>
        [ConfigGroup("Background")] partial class BackgroundConfigGroup
        {
            /// <summary>
            /// 彩色底部填充。
            /// </summary>
            [ConfigItem<bool>("UiBackgroundColorful", true, ConfigSource.Local)] public partial bool BackgroundColorful { get; set; }

            /// <summary>
            /// 透明度。
            /// </summary>
            [ConfigItem<int>("UiBackgroundOpacity", 1000, ConfigSource.Local)] public partial int WallpaperOpacity { get; set; }

            /// <summary>
            /// 旋转。
            /// </summary>
            [ConfigItem<int>("UiBackgroundCarousel", 1000, ConfigSource.Local)] public partial int WallpaperCarousel { get; set; }

            /// <summary>
            /// 模糊遮罩。
            /// </summary>
            [ConfigItem<int>("UiBackgroundBlur", 0, ConfigSource.Local)] public partial int WallpaperBlurRadius { get; set; }

            /// <summary>
            /// 内容裁剪模式。
            /// </summary>
            [ConfigItem<int>("UiBackgroundSuit", 0, ConfigSource.Local)] public partial int WallpaperSuitMode { get; set; }

            /// <summary>
            /// 视频自动暂停。
            /// </summary>
            [ConfigItem<bool>("UiAutoPauseVideo", true, ConfigSource.Local)] public partial bool AutoPauseVideo { get; set; }
        }

        /// <summary>
        /// 高级材质。
        /// </summary>
        [ConfigGroup("Blur")] partial class BlurConfigGroup
        {
            /// <summary>
            /// 是否启用。
            /// </summary>
            [ConfigItem<bool>("UiBlur", false, ConfigSource.Local)] public partial bool IsEnabled { get; set; }

            /// <summary>
            /// 模糊半径。
            /// </summary>
            [ConfigItem<int>("UiBlurValue", 16, ConfigSource.Local)] public partial int Radius { get; set; }

            /// <summary>
            /// 采样率。
            /// </summary>
            [ConfigItem<int>("UiBlurSamplingRate", 70, ConfigSource.Local)] public partial int SamplingRate { get; set; }

            /// <summary>
            /// 模糊方法。
            /// </summary>
            [ConfigItem<int>("UiBlurType", 0, ConfigSource.Local)] public partial int KernelType { get; set; }
        }

        /// <summary>
        /// 自定义主页。
        /// </summary>
        [ConfigGroup("Homepage")] partial class HomepageConfigGroup
        {
            /// <summary>
            /// 主页来源类型。
            /// </summary>
            [ConfigItem<int>("UiCustomType", 0, ConfigSource.Local)] public partial int Type { get; set; }

            /// <summary>
            /// 预设选项。
            /// </summary>
            [ConfigItem<int>("UiCustomPreset", 0, ConfigSource.Local)] public partial int SelectedPreset { get; set; }

            /// <summary>
            /// 自定义 URL。
            /// </summary>
            [ConfigItem<string>("UiCustomNet", "", ConfigSource.Local)] public partial string CustomUrl { get; set; }
        }

        /// <summary>
        /// 功能隐藏。
        /// </summary>
        [ConfigGroup("Hide")]
        partial class HideConfigGroup
        {
            // 主页面
            [ConfigItem<bool>("UiHiddenPageDownload", false, ConfigSource.Local)] public partial bool PageDownload { get; set; }
            [ConfigItem<bool>("UiHiddenPageSetup", false, ConfigSource.Local)] public partial bool PageSetup { get; set; }

            // 子页面 设置
            [ConfigItem<bool>("UiHiddenSetupLaunch", false, ConfigSource.Local)] public partial bool SetupLaunch { get; set; }
            [ConfigItem<bool>("UiHiddenSetupUi", false, ConfigSource.Local)] public partial bool SetupUi { get; set; }
            [ConfigItem<bool>("UiHiddenSetupLauncherLanguage", false, ConfigSource.Local)] public partial bool SetupLauncherLanguage { get; set; }
            [ConfigItem<bool>("UiHiddenSetupLauncherMisc", false, ConfigSource.Local)] public partial bool SetupLauncherMisc { get; set; }
            [ConfigItem<bool>("UiHiddenSetupGameManage", false, ConfigSource.Local)] public partial bool SetupGameManage { get; set; }
            [ConfigItem<bool>("UiHiddenSetupJava", false, ConfigSource.Local)] public partial bool SetupJava { get; set; }
            [ConfigItem<bool>("UiHiddenSetupUpdate", false, ConfigSource.Local)] public partial bool SetupUpdate { get; set; }
            [ConfigItem<bool>("UiHiddenSetupAbout", false, ConfigSource.Local)] public partial bool SetupAbout { get; set; } // 修正名称
            [ConfigItem<bool>("UiHiddenSetupLog", false, ConfigSource.Local)] public partial bool SetupLog { get; set; } // 修正名称

            // 子页面 实例设置
            [ConfigItem<bool>("UiHiddenVersionSave", false, ConfigSource.Local)] public partial bool InstanceSave { get; set; }
            [ConfigItem<bool>("UiHiddenVersionScreenshot", false, ConfigSource.Local)] public partial bool InstanceScreenshot { get; set; }
            [ConfigItem<bool>("UiHiddenVersionMod", false, ConfigSource.Local)] public partial bool InstanceMod { get; set; }
            [ConfigItem<bool>("UiHiddenVersionResourcePack", false, ConfigSource.Local)] public partial bool InstanceResourcePack { get; set; }
            [ConfigItem<bool>("UiHiddenVersionShader", false, ConfigSource.Local)] public partial bool InstanceShader { get; set; }
            [ConfigItem<bool>("UiHiddenVersionServer", false, ConfigSource.Local)] public partial bool InstanceServer { get; set; }

            // 特定功能
            [ConfigItem<bool>("UiHiddenFunctionSelect", false, ConfigSource.Local)] public partial bool FunctionSelect { get; set; }
            [ConfigItem<bool>("UiHiddenFunctionHidden", false, ConfigSource.Local)] public partial bool FunctionHidden { get; set; }
        }
    }

    /// <summary>
    /// Horizon 配置。
    /// </summary>
    [ConfigGroup("Horizon")] partial class HorizonConfigGroup
    {
        /// <summary>
        /// Horizon 数据目录，存放构建、准备好的资源、实例和原版文件。留空则使用启动器所在目录下的 .horizon。
        /// </summary>
        [ConfigItem<string>("HorizonRoot", "", ConfigSource.Local)] public partial string Root { get; set; }
    }

    /// <summary>
    /// 启动配置。
    /// </summary>
    [ConfigGroup("Launch")] partial class LaunchConfigGroup
    {
        /// <summary>
        /// 强制使用高性能显卡。
        /// </summary>
        [ConfigItem<bool>("LaunchAdvanceGraphicCard", true)] public partial bool SetGpuPreference { get; set; }

        /// <summary>
        /// 选择的默认 Java 实例。
        /// </summary>
        [ConfigItem<string>("LaunchArgumentJavaSelect", "")] public partial string SelectedJava { get; set; }

        /// <summary>
        /// 游戏启动后启动器可见性。
        /// </summary>
        [ConfigItem<LauncherVisibility>("LaunchArgumentVisible", LauncherVisibility.DoNothing)] public partial LauncherVisibility LauncherVisibility { get; set; }
    }
}
