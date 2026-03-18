using Ecommerce.Application.DTOs.Auth;

namespace Ecommerce.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request);
    Task LogoutAsync(string token, int userId);
}
