using System.Security.Cryptography;
using System.Text;

namespace QiaoMES.Shared.Security;

/// <summary>
/// 外部服务凭据的静态加密（AES-GCM）。
/// <para>
/// 使用方：智能问数的大模型 <c>ApiKey</c>（它确实要**加密后落库** —— 明文躺在库里，
/// 任何拿到库备份/快照的人就等于拿到了它）。<br/>
/// 迭代服务的管理员密钥**不走这里加密**：那份密钥写在服务器上的配置文件里
/// （<c>0600</c>、与 <c>.env</c> 同一档保护），只借用 <see cref="Mask"/> 做界面回显。
/// </para>
/// <para>
/// **密钥从哪来**：复用 <c>Jwt:SecretKey</c>（它已经稳定存在于服务器 <c>.env</c> 与 GitHub Secrets 里），
/// 用 PBKDF2 派生 32 字节密钥。<br/>
/// 为什么不直接用 ASP.NET Core DataProtection：它默认把密钥环放在容器内的
/// <c>~/.aspnet/DataProtection-Keys</c>，**容器一重建密钥环就丢**，反而会导致"配置还在但解不开"，
/// 除非再挂一个卷或换 EF 密钥环存储 —— 对一个自用系统来说不值当。
/// </para>
/// <para>
/// ⚠️ 若 <c>Jwt:SecretKey</c> 被更换，已保存的凭据将无法解密：此时 <see cref="Unprotect"/> 返回 <c>null</c>，
/// 界面提示「密钥已失效，请重新填写」，不会抛异常。
/// </para>
/// <para>
/// 📌 它原先住在 Assistant 模块里；因为「凭据掩码 / 加密」属于通用能力，
/// 不该让另一个功能为了用一个小工具去引用某个业务模块，所以下沉到
/// <c>QiaoMES.Shared</c>（纯工具类，不依赖任何模块）。
/// </para>
/// </summary>
public static class SecretProtector
{
    /// <summary>格式版本前缀，便于以后换算法时平滑迁移。</summary>
    private const string Version = "v1";

    /// <summary>
    /// 固定盐：同一份 Jwt 密钥在任意实例上派生出一致的密钥，避免多实例解不开。
    /// <para>
    /// 🔴 字符串里保留 <c>Assistant</c> 是**刻意的**：换盐对安全性没有本质提升，
    /// 却会让所有已保存的密钥立刻解不开（要所有人重填一遍），不划算。
    /// </para>
    /// </summary>
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("QiaoMES.Assistant.SecretProtector.v1");

    private const int NonceSize = 12; // AES-GCM 的标准 nonce 长度
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    /// <summary>加密并编码为 <c>v1.&lt;base64(nonce|tag|cipher)&gt;</c>。</summary>
    public static string Protect(string plaintext, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(DeriveKey(passphrase), TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return $"{Version}.{Convert.ToBase64String(payload)}";
    }

    /// <summary>解密；格式不对 / 口令换了 / 数据被改过都返回 <c>null</c>（调用方提示重新填写即可）。</summary>
    public static string? Unprotect(string? protectedValue, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            var parts = protectedValue.Split('.', 2);
            if (parts.Length != 2 || parts[0] != Version)
            {
                return null;
            }

            var payload = Convert.FromBase64String(parts[1]);
            if (payload.Length <= NonceSize + TagSize)
            {
                return null;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipher = payload.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using (var aes = new AesGcm(DeriveKey(passphrase), TagSize))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // 换过 Jwt:SecretKey 或数据被篡改 —— 一律当作"读不出来"
            return null;
        }
    }

    /// <summary>掩码，用于界面回显：只留前 4 与后 4 个字符。</summary>
    public static string? Mask(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        return plaintext.Length <= 8
            ? new string('*', plaintext.Length)
            : $"{plaintext[..4]}{new string('*', Math.Min(8, plaintext.Length - 8))}{plaintext[^4..]}";
    }

    private static byte[] DeriveKey(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new InvalidOperationException(
                "缺少加密口令（Jwt:SecretKey），无法加密/解密已保存的外部服务凭据");
        }

        return Rfc2898DeriveBytes.Pbkdf2(passphrase, Salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
