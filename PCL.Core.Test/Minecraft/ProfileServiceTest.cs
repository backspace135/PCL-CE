using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Minecraft.Profile;

namespace PCL.Core.Test.Minecraft;

[TestClass]
public sealed class ProfileServiceTest
{
    [TestMethod]
    public void OfflineUuidMatchesVanillaServer()
    {
        // 原版服务端离线模式下 Notch 的 UUID
        Assert.AreEqual("b50ad385829d3141a2167e7d7539ba7f", ProfileService.GetOfflineUuid("Notch"));
    }

    [TestMethod]
    public void OfflineUuidDependsOnExactName()
    {
        Assert.AreNotEqual(ProfileService.GetOfflineUuid("Player"), ProfileService.GetOfflineUuid("player"));
    }
}
