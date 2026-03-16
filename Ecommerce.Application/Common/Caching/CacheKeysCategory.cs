using Ecommerce.Application.DTOs.Category;

namespace Ecommerce.Application.Common.Caching;

public static class CacheKeysCategory
{
    public static string Category(int id)
        => $"category:{id}";

    public static string CategoryListVersion()
        => "category:list:version";

    public static string CategoryList(CategoryQuery query, long version)
        => $"category:list:{query.Page}:{query.PageSize}:{query.Search}:{query.SortBy}:{query.SortOrder}:v{version}";
}