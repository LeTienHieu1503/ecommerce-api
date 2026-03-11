using Ecommerce.API.Common.Pagination;
using Ecommerce.API.DTOs.Category;
using Ecommerce.API.Services.Category.Implementations;
using Ecommerce.API.Services.Category.Interfaces;
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
    public async Task<IActionResult> GetCategories([FromQuery] CategoryQuery query)
    {
        var result = await _service.GetAllAsync(query);

        return Success(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await _service.GetByIdAsync(id);

        return Success(category);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
    {
        var category = await _service.CreateAsync(dto);

        return CreatedSuccess(category);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateCategoryDto dto)
    {
        var category = await _service.UpdateAsync(id, dto);

        return UpdateSuccess(category);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);

        return DeleteSuccess();
    }
}