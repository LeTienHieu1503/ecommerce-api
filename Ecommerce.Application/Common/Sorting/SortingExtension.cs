using System.Linq.Dynamic.Core;

namespace Ecommerce.Application.Common.Sorting;

public static class SortingExtension
{
    public static IQueryable<T> ApplySorting<T>(
        this IQueryable<T> query,
        string? sortBy,
        string? sortOrder)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return query.OrderBy("Id");

        var direction = sortOrder?.ToLower() == "desc" ? "descending" : "ascending";

        return query.OrderBy($"{sortBy} {direction}");
    }
}
