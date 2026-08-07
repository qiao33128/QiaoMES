using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Identity.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Application;

public class AuthService(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator) : IAuthService
{
    private const string DefaultRole = "operator";

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<TokenResponse>(new Error("Auth.InvalidInput", "用户名和密码不能为空"));
        }

        var user = await userRepository.GetByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<TokenResponse>(new Error("Auth.InvalidCredentials", "用户名或密码错误"));
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Result.Failure<TokenResponse>(new Error("Auth.InvalidCredentials", "用户名或密码错误"));
        }

        user.RecordLogin();
        userRepository.Update(user);
        await userRepository.SaveChangesAsync(cancellationToken);

        return await BuildTokenResponseAsync(user, cancellationToken);
    }

    public async Task<Result<TokenResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<TokenResponse>(new Error("Auth.InvalidInput", "用户名和密码不能为空"));
        }
        if (request.Password.Length < 6)
        {
            return Result.Failure<TokenResponse>(new Error("Auth.WeakPassword", "密码长度至少 6 位"));
        }

        var username = request.Username.Trim();
        if (await userRepository.IsUsernameTakenAsync(username, cancellationToken))
        {
            return Result.Failure<TokenResponse>(new Error("Auth.UsernameTaken", "用户名已被占用"));
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = new User(username, passwordHash, request.DisplayName.Trim(), request.Email);

        var defaultRole = await roleRepository.GetByNameAsync(DefaultRole, cancellationToken);
        if (defaultRole is null)
        {
            defaultRole = new Role(DefaultRole, "普通操作员");
            roleRepository.Add(defaultRole);
        }
        user.AddRole(defaultRole);

        userRepository.Add(user);
        await userRepository.SaveChangesAsync(cancellationToken);
        return await BuildTokenResponseAsync(user, cancellationToken);
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(new Error("Auth.UserNotFound", "用户不存在"));
        }

        var roles = await ResolveRoleNamesAsync(user, cancellationToken);
        return Result.Success(ToDto(user, roles));
    }

    private async Task<Result<TokenResponse>> BuildTokenResponseAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await ResolveRoleNamesAsync(user, cancellationToken);
        var token = tokenGenerator.GenerateAccessToken(user, roles);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        return Result.Success(new TokenResponse(
            token,
            "Bearer",
            expiresAt,
            ToDto(user, roles)));
    }

    private async Task<IReadOnlyList<string>> ResolveRoleNamesAsync(User user, CancellationToken cancellationToken)
    {
        if (user.Roles.Count == 0) return [];

        var allRoles = await roleRepository.GetAllAsync(cancellationToken);
        var roleMap = allRoles.ToDictionary(r => r.Id, r => r.Name);
        return user.Roles
            .Where(ur => !ur.IsDeleted)
            .Select(ur => roleMap.GetValueOrDefault(ur.RoleId, "operator"))
            .Distinct()
            .ToList();
    }

    private static UserDto ToDto(User user, IReadOnlyList<string> roles)
    {
        return new UserDto(user.Id, user.Username, user.DisplayName, user.Email, roles);
    }
}
