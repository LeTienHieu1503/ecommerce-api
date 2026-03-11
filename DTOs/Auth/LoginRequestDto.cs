using System.ComponentModel.DataAnnotations;

namespace Ecommerce.API.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required]
        public string Email { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;
    }
}
