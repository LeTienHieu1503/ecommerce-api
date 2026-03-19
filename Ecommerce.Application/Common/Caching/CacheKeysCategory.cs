using Ecommerce.Application.DTOs.Category;

namespace Ecommerce.Application.Common.Caching;

public static class CacheKeysCategory
{
    private const string Prefix = "ecommerce";

    public static string Category(int id) => $"{Prefix}:category:{id}";

    public static string CategoryListVersion() => $"{Prefix}:categories:version";

    public static string CategoryList(CategoryQuery query, long version) =>
        $"{Prefix}:categories:v{version}:" +
        $"{query.Page}:" +
        $"{query.PageSize}:" +
        $"{query.Search}:" +
        $"{query.SortBy}:" +
        $"{query.SortOrder}";
}
