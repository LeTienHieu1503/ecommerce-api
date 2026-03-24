namespace Ecommerce.Application.Common.Caching;

public static class CacheKeysOrder
{
    private const string Prefix = "ecommerce";

    public static string Order(int id) => $"{Prefix}:order:{id}";
}
