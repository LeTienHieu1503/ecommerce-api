namespace Ecommerce.Application.DTOs.Auth
{
    public class TokenRequestDto
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
    }
}
