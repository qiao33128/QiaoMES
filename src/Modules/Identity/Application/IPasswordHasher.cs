namespace QiaoMES.Identity.Application;

/// <summary>
/// 密码哈希服务。
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hashedPassword);
}
