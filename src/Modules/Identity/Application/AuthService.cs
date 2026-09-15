using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Identity.Domain;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Application;

public class AuthService(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    IPermissionProvider permissionProvider) : IAuthService
{
    private const string DefaultRole = BuiltInRoles.Operator;

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<TokenResponse>(Error.Validation("Auth.InvalidInput", "用户名和密码不能为空"));
        }

        var user = await userRepository.GetByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null)
        {
            return Result.Failure<TokenResponse>(Error.Unauthorized("Auth.InvalidCredentials", "用户名或密码错误"));
        }
        if (!user.IsActive)
        {
            return Result.Failure<TokenResponse>(Error.Forbidden("Auth.UserDisabled", "该账号已被停用，请联系管理员"));
        }
        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Result.Failure<TokenResponse>(Error.Unauthorized("Auth.InvalidCredentials", "用户名或密码错误"));
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
            return Result.Failure<TokenResponse>(Error.Validation("Auth.InvalidInput", "用户名和密码不能为空"));
        }
        if (request.Password.Length < 6)
        {
            return Result.Failure<TokenResponse>(Error.Validation("Auth.WeakPassword", "密码长度至少 6 位"));
        }

        var username = request.Username.Trim();
        if (await userRepository.IsUsernameTakenAsync(username, cancellationToken))
        {
            return Result.Failure<TokenResponse>(Error.Conflict("Auth.UsernameTaken", "用户名已被占用"));
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = new User(username, passwordHash, request.DisplayName.Trim(), request.Email);

        var defaultRole = await roleRepository.GetByNameAsync(DefaultRole, cancellationToken);
        if (defaultRole is null)
        {
            defaultRole = new Role(DefaultRole, "操作员");
            roleRepository.Add(defaultRole);
        }
        user.AddRole(defaultRole.Id);

        userRepository.Add(user);
        await userRepository.SaveChangesAsync(cancellationToken);
        return await BuildTokenResponseAsync(user, cancellationToken);
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("Auth.UserNotFound", "用户不存在"));
        }
        if (!user.IsActive)
        {
            return Result.Failure<UserDto>(Error.Forbidden("Auth.UserDisabled", "该账号已被停用"));
        }

        var roles = await ResolveRoleNamesAsync(user, cancellationToken);
        var permissions = await ResolvePermissionsAsync(user.Id, cancellationToken);
        return Result.Success(ToDto(user, roles, permissions));
    }

    private async Task<Result<TokenResponse>> BuildTokenResponseAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await ResolveRoleNamesAsync(user, cancellationToken);
        var permissions = await ResolvePermissionsAsync(user.Id, cancellationToken);
        var token = tokenGenerator.GenerateAccessToken(user, roles, permissions);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        return Result.Success(new TokenResponse(
            token,
            "Bearer",
            expiresAt,
            ToDto(user, roles, permissions)));
    }

    private async Task<IReadOnlyList<string>> ResolvePermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var permissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);
        return permissions.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveRoleNamesAsync(User user, CancellationToken cancellationToken)
    {
        var roleIds = user.RoleIds;
        if (roleIds.Count == 0)
        {
            return [];
        }

        var allRoles = await roleRepository.GetAllAsync(cancellationToken);
        var roleMap = allRoles.ToDictionary(r => r.Id, r => r.Name);

        return roleIds
            .Where(roleMap.ContainsKey)
            .Select(id => roleMap[id])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static UserDto ToDto(User user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
        => new(user.Id, user.Username, user.DisplayName, user.Email, roles, permissions);
}
