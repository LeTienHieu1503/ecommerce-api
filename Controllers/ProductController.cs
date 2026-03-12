using Ecommerce.API.Authorization.Policies;
using Ecommerce.API.Common.Pagination;
using Ecommerce.API.DTOs.Product;
using Ecommerce.API.Models;
using Ecommerce.API.Responses;
using Ecommerce.API.Services.Product.Implementations;
using Ecommerce.API.Services.Product.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController : BaseApiController
{
    private readonly IProductService _service;

    public ProductController(IProductService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] ProductQuery query)
    {
        var result = await _service.GetAllAsync(query);

        return Success(result, "Get successfully");
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _service.GetByIdAsync(id);

        return Success(product);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        var result = await _service.CreateAsync(dto);

        return CreatedSuccess(result);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);

        return UpdateSuccess(updated);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);

        return DeleteSuccess();
    }
}