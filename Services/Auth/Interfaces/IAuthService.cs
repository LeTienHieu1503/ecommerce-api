using Ecommerce.API.DTOs.Auth;

namespace Ecommerce.API.Services.Auth.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto request);

    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
}