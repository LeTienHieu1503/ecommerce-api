using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Email is Required")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Password is Required")]
        public string Password { get; set; } = null!;

        [MaxLength(256)]
        public string? DeviceId { get; set; }
    }
}
