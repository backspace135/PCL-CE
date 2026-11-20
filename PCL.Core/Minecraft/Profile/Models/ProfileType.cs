namespace PCL.Core.Minecraft.Profile.Models;

/// <summary>
/// 档案类型。
/// </summary>
public enum ProfileType
{
    /// <summary>微软账户，启动前确认拥有 Minecraft: Java Edition。</summary>
    Microsoft,

    /// <summary>离线档案，不需要账户，启动时使用离线 UUID 与占位令牌。</summary>
    Offline
}
