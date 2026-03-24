using Ecommerce.Domain.Common.Enums;
using Ecommerce.Domain.Common.Pagination;

namespace Ecommerce.Application.DTOs.Order;

public class OrderQuery : PaginationParams
{
    /// <summary>Optional text: numeric → match Order Id or UserId; else try OrderStatus name; else partial match on status name.</summary>
    public string? Search { get; set; }

    /// <summary>Exact status filter (combined with Search using AND).</summary>
    public OrderStatus? Status { get; set; }

    public string? SortBy { get; set; }

    public string? SortOrder { get; set; } = "asc";
}
