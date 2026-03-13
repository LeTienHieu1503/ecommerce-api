namespace Ecommerce.Application.DTOs.Auth
{
    public class LoginResponseDto
    {
        public string Token { get; set; } = null!;

        public int Id { get; set; }

        public string Email { get; set; } = null!;

        public string Role { get; set; } = null!;
    }
}
