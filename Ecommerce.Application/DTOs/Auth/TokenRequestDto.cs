using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Auth
{
    public class TokenRequestDto
    {
        [Required(ErrorMessage = "AccessToken is Required")]
        public string AccessToken { get; set; } = null!;

        [Required(ErrorMessage = "RefreshToken is Required")]
        public string RefreshToken { get; set; } = null!;
    }
}
