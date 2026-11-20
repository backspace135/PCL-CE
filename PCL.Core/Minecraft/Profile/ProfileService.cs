using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.IoC;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Utils;
using PCL.Core.Utils.Secret;

namespace PCL.Core.Minecraft.Profile;

[LifecycleScope("profile", "档案服务")]
[LifecycleService(LifecycleState.Loaded)]
public partial class ProfileService
{
    private static readonly ProfileManagement<McProfile> _profiles = new();
    private static readonly object _saveLock = new();
    private static readonly object _loadStateLock = new();
    private static Task? _loadTask;
    private static bool _loadStarted;
    private static bool _canPersist = true;

    public static IReadOnlyList<McProfile> Profiles => _profiles.GetAll();
    public static McProfile? Current => _profiles.Current;
    public static int LastUsedProfile => _profiles.LastUsed;
    public static bool IsLoaded => _profiles.IsLoaded;
    public static bool IsCreatingProfile { get; set; }

    [LifecycleStart]
    private static Task _Start()
    {
        lock (_loadStateLock)
        {
            if (!_loadStarted)
            {
                _loadStarted = true;
                _loadTask = Task.Run(() =>
                {
                    try
                    {
                        Load();
                    }
                    catch (Exception ex)
                    {
                        Context.Error("后台加载档案失败", ex);
                    }
                });
            }
        }

        return Task.CompletedTask;
    }

    [LifecycleStop]
    private static void _Stop()
    {
        if (IsLoaded) Save();
    }

    public static void Load()
    {
        if (_profiles.IsLoaded) return;

        var encryptedProvider = ConfigService.GetProvider(ConfigSource.SharedEncrypt);
        var hasConfiguredProfiles = encryptedProvider.GetValue<string>("Profile", out var configured);
        if (!hasConfiguredProfiles && ConfigService.GetProvider(ConfigSource.Shared).GetValue<string>("Profile", out var rawProfile))
        {
            if (_TryLoad(rawProfile))
            {
                _EnsureProfileIds();
                Save();
                return;
            }
            Context.Error("档案加密配置无法读取；为避免覆盖原始凭据，本次不会保存档案配置。");
            _canPersist = false;
            _profiles.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
            return;
        }
        if (hasConfiguredProfiles)
        {
            if (_TryLoad(configured))
            {
                _EnsureProfileIds();
                return;
            }
            Context.Error("档案配置格式无效；为避免覆盖原始凭据，本次不会保存档案配置。");
            _canPersist = false;
            _profiles.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
            return;
        }

        _profiles.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
    }

    public static void Save()
    {
        if (!IsLoaded || !_canPersist) return;
        lock (_saveLock)
            Config.System.ProfilesConfig.SetValue(_profiles.Serialize(), forceNewValue: true);
    }

    public static void Add(McProfile profile, bool select = true)
    {
        _EnsureLoaded();
        if (string.IsNullOrWhiteSpace(profile.ProfileId)) profile.ProfileId = Guid.NewGuid().ToString("N");
        _profiles.Add(profile, select);
        Save();
    }

    public static void Update(McProfile origin, McProfile current)
    {
        _EnsureLoaded();
        _profiles.Update(origin, current);
        Save();
    }

    public static void Remove(McProfile profile)
    {
        _EnsureLoaded();
        _profiles.Delete(profile);
        Save();
    }

    public static void Select(McProfile? profile)
    {
        _EnsureLoaded();
        _profiles.Select(profile);
        Save();
    }

    public static void SelectAt(int index)
    {
        _EnsureLoaded();
        _profiles.SelectAt(index);
        Save();
    }

    public static void Clear()
    {
        _EnsureLoaded();
        _profiles.Clear();
        Save();
    }

    public static async Task<McProfile> AuthenticateAsync(ProfileType profileType, AuthenticationRequest request,
        McProfile? existing, bool select, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        _EnsureLoaded();

        var canUseExisting = existing?.ProfileType == profileType;

        if (canUseExisting && !request.ForceRefresh &&
            existing!.ExpiresAt is { } expires &&
            expires > DateTimeOffset.UtcNow.AddMinutes(1) &&
            !string.IsNullOrWhiteSpace(existing.AccessToken))
        {
            if (select) Select(existing);
            return existing;
        }

        var provider = new MicrosoftProvider(request.ClientId);
        AuthenticationResult? result = null;

        if (canUseExisting)
        {
            try
            {
                result = await provider.RefreshAsync(existing!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IdentityModelAuthenticationException ex)
            {
                Context.Warn("刷新档案失败，将尝试重新认证", ex);
                if (ex.Error?.StartsWith("xsts_", StringComparison.OrdinalIgnoreCase) == true)
                    throw;
                if (!string.Equals(ex.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase) &&
                    request.RefreshFailureHandler is not null &&
                    await request.RefreshFailureHandler(ex, token).ConfigureAwait(false))
                {
                    if (select) Select(existing!);
                    return existing!;
                }
            }
            catch (Exception ex)
            {
                if (request.RefreshFailureHandler is not null &&
                    await request.RefreshFailureHandler(ex, token).ConfigureAwait(false))
                {
                    if (select) Select(existing!);
                    return existing!;
                }
                throw;
            }
        }

        if (result is null)
        {
            var loginRequest = request with { RefreshToken = null };
            result = await provider.AuthenticateAsync(loginRequest, token).ConfigureAwait(false);
        }

        var matched = canUseExisting ? existing : _FindMatchingProfile(result);
        return ApplyAuthenticationResult(result, matched, select);
    }

    public static McProfile ApplyAuthenticationResult(AuthenticationResult result, McProfile? existing = null, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        var profile = existing?.Clone() ?? new McProfile { ProfileId = Guid.NewGuid().ToString("N") };
        profile.ProfileType = result.ProfileType;
        profile.UserName = result.UserName;
        profile.Uuid = result.Uuid;
        profile.AccessToken = result.AccessToken;
        profile.RefreshToken = result.RefreshToken;
        profile.ClientToken = result.ClientToken;
        profile.TokenType = result.TokenType;
        profile.ExpiresAt = result.ExpiresAt;
        profile.RawJson = result.RawJson;
        profile.Provider = result.Provider ?? profile.Provider;
        if (existing is null) Add(profile, select);
        else
        {
            Update(existing, profile);
            if (select) Select(profile);
        }
        return profile;
    }

    public static bool HasMicrosoftProfile => Profiles.Any(p => p.ProfileType == ProfileType.Microsoft);

    /// <summary>
    /// 新建并保存离线档案；同名的离线档案已存在时直接使用它。
    /// </summary>
    public static McProfile AddOffline(string userName, bool select = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        _EnsureLoaded();
        var existing = Profiles.FirstOrDefault(profile =>
            profile.ProfileType == ProfileType.Offline &&
            string.Equals(profile.UserName, userName, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (select) Select(existing);
            return existing;
        }

        var created = new McProfile
        {
            ProfileId = Guid.NewGuid().ToString("N"),
            ProfileType = ProfileType.Offline,
            UserName = userName,
            Uuid = GetOfflineUuid(userName),
            // 离线档案没有真实令牌，会话 JSON 仍要求非空，用随机值占位
            AccessToken = Guid.NewGuid().ToString("N")
        };
        Add(created, select);
        return created;
    }

    /// <summary>
    /// 离线 UUID：对 <c>OfflinePlayer:玩家名</c> 取 MD5 得到的第 3 版 UUID（32 位小写，无连字符），与原版服务端、HMCL 等一致。
    /// </summary>
    public static string GetOfflineUuid(string userName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + userName));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexStringLower(hash);
    }

    private static McProfile? _FindMatchingProfile(AuthenticationResult result)
        => Profiles.FirstOrDefault(profile =>
            profile.ProfileType == result.ProfileType &&
            string.Equals(profile.Uuid, result.Uuid, StringComparison.Ordinal));

    private static void _EnsureLoaded()
    {
        if (IsLoaded) return;

        Task? task;
        lock (_loadStateLock) task = _loadTask;
        if (task is not null)
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignored
            }

            if (IsLoaded) return;
            Context.Warn("档案加载未完成，暂以空档案继续");
            _canPersist = false;
            _profiles.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
            return;
        }

        Load();
    }

    private static bool _TryLoad(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var node = JsonCompat.ParseNode(json);
            if (node is not JsonObject root || root["profiles"] is not JsonArray)
                return false;
            _profiles.LoadFromString(json);
            return true;
        }
        catch (Exception ex)
        {
            Context.Warn("读取档案配置失败", ex);
            return false;
        }
    }

    private static void _EnsureProfileIds()
    {
        var changed = false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.ProfileId) && ids.Add(profile.ProfileId)) continue;
            do profile.ProfileId = Guid.NewGuid().ToString("N");
            while (!ids.Add(profile.ProfileId));
            changed = true;
        }
        if (changed) Save();
    }
}
