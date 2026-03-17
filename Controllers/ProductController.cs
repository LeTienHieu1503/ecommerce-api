using Ecommerce.Application.DTOs;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.DTOs.Category;
using Ecommerce.Application.DTOs.Product;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.API.Authorization.Policies;
using Ecommerce.Domain.Entities;
using Ecommerce.API.Responses;
using Ecommerce.Application.Interfaces;
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
    [Authorize(Policy = "product.read")]
    public async Task<IActionResult> GetProducts([FromQuery] ProductQuery query)
    {
        var result = await _service.GetAllAsync(query);

        return Success(result, "Get successfully");
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "product.read")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _service.GetByIdAsync(id);

        return Success(product);
    }

    [Authorize(Policy = "product.create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        var result = await _service.CreateAsync(dto);

        return CreatedSuccess(result);
    }

    [Authorize(Policy = "product.update")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);

        return UpdateSuccess(updated);
    }

    [Authorize(Policy = "product.delete")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);

        return DeleteSuccess();
    }
}
