using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.Integrations;

public record ApiClientDto(
    Guid Id,
    string Name,
    string KeyPreview,
    string Scopes,
    bool IsActive,
    bool IsUsable,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    string? Remark,
    DateTime CreatedAt);

/// <summary>创建结果：明文密钥只在此处返回一次。</summary>
public record ApiClientCreatedDto(ApiClientDto Client, string PlainKey);

public record CreateApiClientRequest(string Name, string? Scopes = null, DateTime? ExpiresAt = null, string? Remark = null);

/// <summary>开放 API 客户端管理（由内部管理员通过 JWT 调用）。</summary>
public interface IApiClientService
{
    Task<Result<PagedResult<ApiClientDto>>> GetListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Result<ApiClientCreatedDto>> CreateAsync(CreateApiClientRequest request, CancellationToken cancellationToken = default);

    Task<Result<ApiClientDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class ApiClientService(IApiClientRepository repository) : IApiClientService
{
    public async Task<Result<PagedResult<ApiClientDto>>> GetListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await repository.QueryAsync(page, pageSize, cancellationToken);

        return Result.Success(new PagedResult<ApiClientDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 200 ? 20 : pageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<ApiClientCreatedDto>> CreateAsync(
        CreateApiClientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ApiClientCreatedDto>(Error.Validation("ApiClient.InvalidName", "客户端名称不能为空"));
        }

        var (plainKey, hash, preview) = ApiClient.GenerateKey();
        var client = new ApiClient(request.Name, hash, preview, request.Scopes, request.ExpiresAt, request.Remark);

        repository.Add(client);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new ApiClientCreatedDto(ToDto(client), plainKey));
    }

    public async Task<Result<ApiClientDto>> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var client = await repository.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ApiClientDto>(Error.NotFound("ApiClient.NotFound", "API 客户端不存在"));
        }

        client.SetActive(isActive);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(client));
    }

    private static ApiClientDto ToDto(ApiClient client) => new(
        client.Id,
        client.Name,
        client.KeyPreview,
        client.Scopes,
        client.IsActive,
        client.IsUsable,
        client.ExpiresAt,
        client.LastUsedAt,
        client.Remark,
        client.CreatedAt);
}
