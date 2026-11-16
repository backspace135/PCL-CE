using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PCL.Horizon;

/// <summary>
/// Horizon 发布包的签名校验，与 <c>horizon-rust/horizon</c> 的 <c>crates/horizon-release-sign</c> 保持一致。
/// Nightly 的 <c>SHA256SUMS.v2.sig</c> 每行一个 base64 Ed25519 签名，
/// 签名内容为 <c>horizon release &lt;版本&gt;\n</c> 加上 <c>SHA256SUMS</c> 的字节。
/// </summary>
public static class HorizonReleaseSigning
{
    public const string SumsName = "SHA256SUMS";
    public const string SignatureV2Name = "SHA256SUMS.v2.sig";
    public const string NightlyTag = "nightly";

    /// <summary>
    /// 来自 <c>crates/horizon-release-sign/release-keys.txt</c>。上游轮换密钥时需同步更新。
    /// </summary>
    public const string ReleaseKeysText =
        """
        # crates/horizon-release-sign/release-keys.txt
        MU4oiIQcCxkZvbwSjjXs7SySCuM6ywRQ3nGaJYZG0gw=
        """;

    public static IReadOnlyList<byte[]> TrustedKeys { get; } = ParseKeys(ReleaseKeysText);

    /// <summary>每行一个 base64 公钥，跳过空行和 <c>#</c> 注释。</summary>
    public static IReadOnlyList<byte[]> ParseKeys(string text)
    {
        var keys = new List<byte[]>();
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(line); }
            catch (FormatException) { throw new FormatException($"{line}: 不是 base64。"); }
            if (bytes.Length != Ed25519PublicKeyParameters.KeySize)
                throw new FormatException($"{line}: 不是 32 字节的 Ed25519 公钥。");
            keys.Add(bytes);
        }
        return keys;
    }

    /// <summary>签名覆盖的内容：<c>horizon release {tag}\n</c> 加上校验和列表。</summary>
    public static byte[] Message(string tag, ReadOnlySpan<byte> sums)
    {
        var head = Encoding.UTF8.GetBytes($"horizon release {tag}\n");
        var message = new byte[head.Length + sums.Length];
        head.CopyTo(message, 0);
        sums.CopyTo(message.AsSpan(head.Length));
        return message;
    }

    /// <summary>解析 <c>sha256sum</c> 格式（<c>&lt;hex&gt;  &lt;文件&gt;</c> 或 <c>&lt;hex&gt; *&lt;文件&gt;</c>），哈希转为小写。</summary>
    public static IReadOnlyList<(string Name, string Hex)> ParseSums(string text)
    {
        var result = new List<(string, string)>();
        foreach (var line in Lines(text))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var space = line.IndexOf(' ');
            if (space < 0) throw BadSumsLine(line);
            var hex = line[..space];
            var name = line[(space + 1)..];
            if (name.Length > 0 && name[0] is ' ' or '*') name = name[1..];
            if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit) || name.Length == 0) throw BadSumsLine(line);
            result.Add((name, hex.ToLowerInvariant()));
        }
        return result;
    }

    /// <summary>
    /// 校验 <paramref name="signature"/> 中是否有任一行由 <paramref name="keys"/> 中任一公钥为 <paramref name="tag"/> 签署了
    /// <paramref name="sums"/>，通过则返回解析后的列表，否则抛出 <see cref="HorizonReleaseException"/>。
    /// </summary>
    public static IReadOnlyList<(string Name, string Hex)> Verify(
        string tag, byte[] sums, string signature, IReadOnlyList<byte[]> keys)
    {
        if (keys.Count == 0)
            throw new HorizonReleaseException("没有可信的 Horizon 发布公钥，无法校验下载。");
        var message = Message(tag, sums);
        var signed = false;
        foreach (var line in Lines(signature))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            byte[] sig;
            try { sig = Convert.FromBase64String(trimmed); }
            catch (FormatException) { continue; }
            if (sig.Length != Ed25519PrivateKeyParameters.SignatureSize) continue;
            if (keys.Any(key => VerifyOne(key, message, sig)))
            {
                signed = true;
                break;
            }
        }
        if (!signed)
            throw new HorizonReleaseException(
                $"{tag} 的签名与 Horizon 发布公钥不符，未安装。下载内容可能被篡改。");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(sums); }
        catch (DecoderFallbackException) { throw new HorizonReleaseException($"{SumsName} 不是文本。"); }
        try { return ParseSums(text); }
        catch (FormatException ex) { throw new HorizonReleaseException(ex.Message, ex); }
    }

    /// <summary>
    /// 按标题严格解析 Nightly 版本号：<c>Nightly 2026-11-13 · 9f42064</c> → <c>nightly-2026-11-13-9f42064</c>，
    /// 其他格式返回 <see langword="null"/>。
    /// </summary>
    public static string? BuildTitleVersion(string title)
    {
        const string prefix = "Nightly ";
        if (!title.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = title[prefix.Length..];
        var separator = rest.IndexOf(" · ", StringComparison.Ordinal);
        if (separator < 0) return null;
        var date = rest[..separator];
        var commit = rest[(separator + 3)..];
        return IsDate(date) && IsCommit(commit) ? $"{NightlyTag}-{date}-{commit}" : null;
    }

    /// <summary>是否为完整的 Nightly 构建版本 <c>nightly-&lt;日期&gt;-&lt;提交&gt;</c>，即 v2 签名所针对的版本。</summary>
    public static bool IsBuild(string? version)
    {
        if (version is null || !version.StartsWith(NightlyTag + "-", StringComparison.Ordinal)) return false;
        var rest = version[(NightlyTag.Length + 1)..];
        return rest.Length > 11 && IsDate(rest[..10]) && rest[10] == '-' && IsCommit(rest[11..]);
    }

    /// <summary>标签是否属于 Nightly：<c>nightly</c> 或 <c>nightly-…</c>。</summary>
    public static bool IsNightlyTag(string tag) =>
        tag == NightlyTag || tag.StartsWith(NightlyTag + "-", StringComparison.Ordinal);

    /// <summary>
    /// 校验 <paramref name="version"/> 的 v2 签名后，返回 <paramref name="asset"/> 在 <c>SHA256SUMS</c> 中的十六进制哈希。
    /// 没有 v2 签名、签名不符或列表中没有该文件时抛出 <see cref="HorizonReleaseException"/>。
    /// </summary>
    public static string BuildDigest(
        string version, string asset, byte[]? sums, string? signatureV2, IReadOnlyList<byte[]> keys)
    {
        if (!IsBuild(version))
            throw new HorizonReleaseException($"“{version}”没有标明 Nightly 的日期和提交，无法校验，未安装。");
        if (signatureV2 is null)
            throw new HorizonReleaseException(
                $"{version} 没有 {SignatureV2Name}（针对该构建本身的签名），无法校验，未安装。请选择更新的 Nightly。");
        if (sums is null)
            throw new HorizonReleaseException($"{version} 没有 {SumsName}，未安装。");
        foreach (var (name, hex) in Verify(version, sums, signatureV2, keys))
            if (name == asset)
                return hex;
        throw new HorizonReleaseException($"{asset} 不在 {version} 的已签名校验和中，未安装。");
    }

    private static bool VerifyOne(byte[] key, byte[] message, byte[] signature)
    {
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(key, 0));
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(signature);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsDate(string text) =>
        text.Length == 10 && text.Select((c, i) => i is 4 or 7 ? c == '-' : char.IsAsciiDigit(c)).All(ok => ok);

    private static bool IsCommit(string text) =>
        text.Length is >= 7 and <= 40 && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>与 Rust 的 <c>str::lines</c> 一致：按 <c>\n</c> 分行，去掉行尾的 <c>\r</c>，末尾空行不算。</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var lines = text.Split('\n');
        var count = lines.Length;
        if (count > 0 && lines[^1].Length == 0) count--;
        for (var i = 0; i < count; i++)
            yield return lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
    }

    private static FormatException BadSumsLine(string line) =>
        new($"{SumsName}: “{line}”不是 `<sha256>  <文件>` 格式。");
}

/// <summary>Horizon 版本列表、下载、校验或安装失败。</summary>
public class HorizonReleaseException : Exception
{
    public HorizonReleaseException(string message) : base(message) { }
    public HorizonReleaseException(string message, Exception? inner) : base(message, inner) { }
}
