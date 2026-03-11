using System.ComponentModel.DataAnnotations;

namespace Ecommerce.API.DTOs.Auth
{
    public class RegisterRequestDto
    {
        [Required]
        public string Email { get; set; } = null!;
        [Required]
        public string Password { get; set; } = null!;
    }
}
