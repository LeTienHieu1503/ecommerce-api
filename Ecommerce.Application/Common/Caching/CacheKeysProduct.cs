using Ecommerce.Application.Common.Sorting;
using Ecommerce.Application.DTOs.Product;

namespace Ecommerce.Application.Common.Caching;

public static class CacheKeysProduct
{
    private const string Prefix = "ecommerce";

    public static string Product(int id) => $"{Prefix}:product:{id}";

    public static string ProductListVersion() => $"{Prefix}:products:version";

    public static string ProductList(ProductQuery query, long version) =>
        $"{Prefix}:products:v{version}:" +
        $"{query.Page}:" +
        $"{query.PageSize}:" +
        $"{query.Search}:" +
        $"{query.CategoryId}:" +
        $"{query.MinPrice}:" +
        $"{query.MaxPrice}:" +
        $"{query.SortBy}:" +
        $"{query.SortOrder}";
}
