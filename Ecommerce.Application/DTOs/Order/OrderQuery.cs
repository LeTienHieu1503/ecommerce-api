using Ecommerce.Domain.Common.Enums;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.DTOs.Order;

public class OrderQuery : PaginationParams
{
    public string? Search { get; set; }

    public OrderStatus? Status { get; set; }

    public string? SortBy { get; set; }

    public string? SortOrder { get; set; } = "asc";
}
