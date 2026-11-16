using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Authentication.Utils;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL;

/// <summary>
///     微软账户登录。Horizon 只为拥有 Minecraft: Java Edition 的正版账户启动。
/// </summary>
public static class HorizonLogin
{
    /// <summary>
    ///     登录当前选中的微软账户（令牌过期时刷新）；没有选中账户时走设备码登录并新建档案。
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
    ///     登录并确认账户拥有 Java 版。已处理的错误（未购买、XSTS 账户问题等）以 <c>$$</c> 抛出。
    /// </summary>
    public static McProfile Login(bool forceRefresh, CancellationToken token)
    {
        var existing = ProfileService.Current;
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
