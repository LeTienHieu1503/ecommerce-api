using Ecommerce.API.Common.Pagination;

namespace Ecommerce.API.DTOs.Product;

public class ProductQuery : PaginationParams
{
    public string? Search { get; set; }

    public int? CategoryId { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public string? SortBy { get; set; }

    //public string? SortOrder { get; set; } = "asc";
}