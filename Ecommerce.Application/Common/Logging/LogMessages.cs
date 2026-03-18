namespace Ecommerce.Application.Common.Logging;

public static class LogMessages
{
    public const string ProductCacheHit = "Cache hit for product {ProductId}";
    public const string ProductCacheMiss = "Cache miss for product {ProductId}";
    public const string ProductNotFound = "Product not found {ProductId}";
    public const string ProductUpdating = "Updating product {ProductId}";
    public const string ProductDeleting = "Soft deleting product {ProductId}";
    public const string ProductCreating = "Creating product {ProductName} in category {CategoryId}";


    public const string CategoryCacheHit = "Cache hit for category {CategoryId}";
    public const string CategoryCacheMiss = "Cache miss for category {CategoryId}";
    public const string CategoryGetting = "Getting category {CategoryId}";
    public const string CategoryCreating = "Creating category {CategoryName}";
    public const string CategoryUpdating = "Updating category {CategoryId}";
    public const string CategoryDeleting = "Deleting category {CategoryId}";
    public const string CategoryNotFound = "Category not found {CategoryId}";
    public const string CategoryHasProducts = "Cannot delete category {CategoryId} because it has products";

    public const string AuthEmailNotFound = "Login failed: email not found {Email}";
    public const string AuthWrongPassword = "Login failed: wrong password for {Email}";
    public const string AuthLoginSuccess = "User login success {UserId}";
}
