using Ecommerce.Application.DTOs.Auth;

namespace Ecommerce.Application.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request, string clientIp);
    Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request);
    Task<LoginResponseDto> RefreshTokenAsync(TokenRequestDto request, string clientIp);
    Task LogoutAsync(string token, int userId);
}