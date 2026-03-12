using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.DTOs.Category;

public class CategoryQuery : PaginationParams
{
    public string? Search { get; set; }

    public string? SortBy { get; set; }

    public string? SortOrder { get; set; } = "asc";
}
