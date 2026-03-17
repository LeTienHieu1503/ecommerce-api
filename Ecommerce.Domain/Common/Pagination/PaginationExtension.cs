namespace Ecommerce.Domain.Common.Pagination;

public static class PaginationExtension
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize)
    {
        var totalCount = query.Count();

        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return await Task.FromResult(new PagedResult<T>(items, totalCount, page, pageSize));
    }
}
