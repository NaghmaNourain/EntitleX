namespace EntitleX.Api.Infrastructure;

/// <summary>
/// Shared list paging: prefer <c>page</c> + <c>pageSize</c> (1-based, max 100);
/// legacy <c>take</c>/<c>skip</c> remain accepted and map onto the same contract.
/// </summary>
public static class Pagination
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static NormalizedPage Normalize(
        int? page = null,
        int? pageSize = null,
        int? take = null,
        int? skip = null)
    {
        int resolvedPage;
        int resolvedPageSize;
        int resolvedSkip;

        if (page is > 0 || pageSize is > 0)
        {
            resolvedPage = page is > 0 ? page.Value : DefaultPage;
            resolvedPageSize = Math.Clamp(pageSize is > 0 ? pageSize.Value : DefaultPageSize, 1, MaxPageSize);
            resolvedSkip = (resolvedPage - 1) * resolvedPageSize;
        }
        else if (take is > 0 || skip is > 0)
        {
            resolvedPageSize = Math.Clamp(take is > 0 ? take.Value : DefaultPageSize, 1, MaxPageSize);
            resolvedSkip = Math.Max(0, skip ?? 0);
            resolvedPage = (resolvedSkip / resolvedPageSize) + 1;
        }
        else
        {
            resolvedPage = DefaultPage;
            resolvedPageSize = DefaultPageSize;
            resolvedSkip = 0;
        }

        return new NormalizedPage(resolvedPage, resolvedPageSize, resolvedSkip);
    }
}

public readonly record struct NormalizedPage(int Page, int PageSize, int Skip);

/// <summary>Consistent list envelope for request / approval / entitlement lists.</summary>
public sealed record PagedEnvelope<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
