using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PCL.Horizon;

/// <summary>
/// minisign 签名校验（<see href="https://jedisct1.github.io/minisign/"/>），支持旧式（<c>Ed</c>，签署文件本身）与
/// 预哈希（<c>ED</c>，签署文件的 BLAKE2b-512）两种签名，并校验可信注释的全局签名。
/// </summary>
public static class HorizonMinisign
{
    private const int KeyIdSize = 8;
    private const int PublicKeySize = 32;
    private const int SignatureSize = 64;

    /// <summary>
    /// 用 <paramref name="publicKey"/>（base64，<c>RW…</c>）校验 <paramref name="signatureText"/>（<c>.minisig</c> 文本）
    /// 是否签署了 <paramref name="data"/>。不通过时抛出 <see cref="HorizonRegistryException"/>。
    /// </summary>
    public static void Verify(string publicKey, string signatureText, byte[] data)
    {
        var key = _Decode(publicKey.Trim(), 2 + KeyIdSize + PublicKeySize, "公钥");
        if (key[0] != 'E' || key[1] != 'd')
            throw new HorizonRegistryException("不支持的 minisign 公钥算法。");

        var lines = signatureText.Replace("\r", "").Split('\n')
            .Where(l => l.Length > 0).ToArray();
        if (lines.Length < 4 || !lines[0].StartsWith("untrusted comment:", StringComparison.Ordinal) ||
            !lines[2].StartsWith("trusted comment: ", StringComparison.Ordinal))
            throw new HorizonRegistryException("minisign 签名格式无效。");

        var signature = _Decode(lines[1], 2 + KeyIdSize + SignatureSize, "签名");
        if (!signature.AsSpan(2, KeyIdSize).SequenceEqual(key.AsSpan(2, KeyIdSize)))
            throw new HorizonRegistryException("签名不是由该项目的密钥签署的。");

        var signed = (signature[0], signature[1]) switch
        {
            ((byte)'E', (byte)'d') => data,
            ((byte)'E', (byte)'D') => _Blake2b512(data),
            _ => throw new HorizonRegistryException("不支持的 minisign 签名算法。")
        };
        var pk = key.AsSpan(2 + KeyIdSize, PublicKeySize).ToArray();
        var sig = signature.AsSpan(2 + KeyIdSize, SignatureSize).ToArray();
        if (!_Verify(pk, signed, sig))
            throw new HorizonRegistryException("文件的签名校验失败，可能已被篡改。");

        var trustedComment = Encoding.UTF8.GetBytes(lines[2]["trusted comment: ".Length..]);
        var globalSignature = _Decode(lines[3], SignatureSize, "全局签名");
        if (!_Verify(pk, [.. sig, .. trustedComment], globalSignature))
            throw new HorizonRegistryException("签名的可信注释校验失败。");
    }

    private static byte[] _Decode(string base64, int expectedLength, string what)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new HorizonRegistryException($"minisign {what}不是 base64。"); }
        if (bytes.Length != expectedLength)
            throw new HorizonRegistryException($"minisign {what}长度无效。");
        return bytes;
    }

    private static byte[] _Blake2b512(byte[] data)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static bool _Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.VerifySignature(signature);
    }
}
