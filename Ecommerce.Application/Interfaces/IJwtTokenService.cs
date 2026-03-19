using Ecommerce.Domain.Entities;
using System.Security.Claims;

namespace Ecommerce.Application.Interfaces
{
    public interface IJwtTokenService
    {
        string GenerateToken(User user);
        string GenerateToken(User user, string sessionId, long sessionVersion, string ipHash);
        string GenerateRefreshToken();
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    }
}