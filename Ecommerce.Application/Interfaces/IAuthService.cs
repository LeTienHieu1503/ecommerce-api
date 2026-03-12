using Ecommerce.Application.DTOs.Auth;

namespace Ecommerce.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
}
