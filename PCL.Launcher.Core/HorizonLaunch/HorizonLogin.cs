using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Authentication.Utils;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL;

/// <summary>
///     档案登录：离线档案直接使用，微软账户需确认拥有 Minecraft: Java Edition。
/// </summary>
public static class HorizonLogin
{
    /// <summary>
    ///     登录当前选中的档案（微软账户令牌过期时刷新）；没有选中档案时走设备码登录并新建微软档案。
    ///     输入为是否强制刷新令牌，输出为登录后的档案。
    /// </summary>
    public static readonly Loading.LoaderTask<bool, McProfile> LoginLoader = new(
        Lang.Text("Minecraft.Launch.Stage.Login"),
        task => task.output = Login(task.input, task.AbortedToken),
        priority: ThreadPriority.BelowNormal)
    {
        reloadTimeout = 1,
        DisplayNameFactory = () => Lang.Text("Minecraft.Launch.Stage.Login")
    };

    /// <summary>
    ///     登录当前档案。离线档案不联网直接返回；微软账户登录并确认拥有 Java 版，
    ///     已处理的错误（未购买、XSTS 账户问题等）以 <c>$$</c> 抛出。
    /// </summary>
    public static McProfile Login(bool forceRefresh, CancellationToken token)
    {
        var existing = ProfileService.Current;
        if (existing is { ProfileType: ProfileType.Offline })
        {
            LogWrapper.Info("Profile", $"验证方式：离线（{existing.UserName}）");
            return existing;
        }

        LogWrapper.Info("Profile", $"验证方式：正版（{(existing is null ? "尚未登录" : existing.UserName)}）");
        McProfile profile;
        try
        {
            profile = ProfileService
                .AuthenticateAsync(
                    ProfileType.Microsoft,
                    new AuthenticationRequest
                    {
                        ForceRefresh = forceRefresh,
                        DeviceCodeHandler = LoginProfile.ShowDeviceCodeLoginAsync,
                        RefreshFailureHandler = (_, _) => Task.FromResult(false)
                    },
                    existing,
                    true,
                    token)
                .GetAwaiter()
                .GetResult();
        }
        catch (IdentityModelAuthenticationException ex)
            when (LoginProfile.HandleMicrosoftXstsError(ex)
                  || LoginProfile.HandleMicrosoftNotOwnedError(ex)
                  || LoginProfile.HandleMicrosoftCreateProfileError(ex))
        {
            throw new IdentityModelException("$$", ex);
        }

        ProfileService.IsCreatingProfile = false;
        if (!MojangUtils.CheckLicenseAsync(profile, token).GetAwaiter().GetResult())
        {
            LoginProfile.HandleMicrosoftNotOwnedError(
                new IdentityModelAuthenticationException("minecraft_not_owned",
                    "The Microsoft account does not own Minecraft."));
            throw new IdentityModelException("$$");
        }

        LogWrapper.Info("Profile", "正版验证完成");
        return profile;
    }
}
