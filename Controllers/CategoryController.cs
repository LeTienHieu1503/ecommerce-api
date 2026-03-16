using Ecommerce.Application.DTOs;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Application.DTOs.Category;
using Ecommerce.Application.DTOs.Product;
using Ecommerce.Application.Common.Sorting;
using Ecommerce.Domain.Common.Pagination;
using Ecommerce.API.Authorization.Policies;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoryController : BaseApiController
{
    private readonly ICategoryService _service;

    public CategoryController(ICategoryService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = "category.read")]
    public async Task<IActionResult> GetCategories([FromQuery] CategoryQuery query)
    {
        var result = await _service.GetAllAsync(query);

        return Success(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "category.read")]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await _service.GetByIdAsync(id);

        return Success(category);
    }

    [Authorize(Policy = "category.create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
    {
        var category = await _service.CreateAsync(dto);

        return CreatedSuccess(category);
    }

    [Authorize(Policy = "category.update")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateCategoryDto dto)
    {
        var category = await _service.UpdateAsync(id, dto);

        return UpdateSuccess(category);
    }

    [Authorize(Policy = "category.delete")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);

        return DeleteSuccess();
    }
}
