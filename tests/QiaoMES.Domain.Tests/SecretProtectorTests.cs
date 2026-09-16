using QiaoMES.Assistant.Domain;

namespace QiaoMES.Domain.Tests;

/// <summary>
/// 大模型 API Key 的加密存储测试。
/// <para>
/// 这块必须测：只要有一处写错（nonce/tag 顺序、口令为空、篡改后不报错），
/// 要么密钥永远解不开、要么"加密"变成摆设。尤其是<b>篡改与换口令必须失败</b>这一条。
/// </para>
/// </summary>
public class SecretProtectorTests
{
    private const string Passphrase = "QiaoMES_Dev_Secret_Key_Change_Me_0123456789_At_Least_32_Chars";

    [Fact]
    public void 加密后可原样解回且密文不含明文()
    {
        var protectedValue = SecretProtector.Protect("sk-abcdef1234567890", Passphrase);

        Assert.StartsWith("v1.", protectedValue);
        Assert.DoesNotContain("sk-abcdef", protectedValue);
        Assert.Equal("sk-abcdef1234567890", SecretProtector.Unprotect(protectedValue, Passphrase));
    }

    [Fact]
    public void 同一明文两次加密密文不同但都能解开()
    {
        var first = SecretProtector.Protect("sk-same-value", Passphrase);
        var second = SecretProtector.Protect("sk-same-value", Passphrase);

        // 随机 nonce：密文必须不同（否则等于把"两次配了同一个 Key"这件事暴露出来）
        Assert.NotEqual(first, second);
        Assert.Equal("sk-same-value", SecretProtector.Unprotect(first, Passphrase));
        Assert.Equal("sk-same-value", SecretProtector.Unprotect(second, Passphrase));
    }

    [Fact]
    public void 换了口令_解不开且不抛异常()
    {
        var protectedValue = SecretProtector.Protect("sk-secret", Passphrase);

        // 对应「更换 Jwt:SecretKey 后旧密钥失效」的场景：必须优雅返回 null，而不是抛异常炸掉接口
        Assert.Null(SecretProtector.Unprotect(protectedValue, "换了另一把密钥_Change_Me_0123456789_ABCDE"));
    }

    [Fact]
    public void 密文被篡改_解不开且不抛异常()
    {
        var protectedValue = SecretProtector.Protect("sk-secret-value", Passphrase);
        var payload = Convert.FromBase64String(protectedValue[3..]);
        payload[^1] ^= 0xFF; // 翻转最后一个字节，GCM 校验必须失败

        Assert.Null(SecretProtector.Unprotect("v1." + Convert.ToBase64String(payload), Passphrase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("这不是我们生成的格式")]
    [InlineData("v2.AAAA")] // 版本不认识 → 当作读不出来，而不是硬解
    [InlineData("v1.@@@不是base64")]
    [InlineData("v1.")]
    public void 非法输入_一律返回空(string? value)
        => Assert.Null(SecretProtector.Unprotect(value, Passphrase));

    [Fact]
    public void 空明文_拒绝加密()
        => Assert.Throws<ArgumentException>(() => SecretProtector.Protect("   ", Passphrase));

    [Fact]
    public void 空口令_拒绝加密()
        => Assert.Throws<InvalidOperationException>(() => SecretProtector.Protect("sk-x", string.Empty));

    [Theory]
    [InlineData("sk-abcdef1234567890", "sk-a********7890")]
    [InlineData("short", "*****")]
    public void 掩码_只留首尾(string value, string expected)
        => Assert.Equal(expected, SecretProtector.Mask(value));

    [Fact]
    public void 掩码_空值返回空()
    {
        Assert.Null(SecretProtector.Mask(null));
        Assert.Null(SecretProtector.Mask("  "));
    }
}
