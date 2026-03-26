using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Email is Required")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Password is Required")]
        public string Password { get; set; } = null!;

        /// <summary>
        /// Mã nhận dạng trình duyệt/thiết bị — tùy chọn.
        /// Web client nên tạo một UUID ngẫu nhiên, lưu vào localStorage và gửi mỗi lần đăng nhập.
        /// Thiếu trường này thì bỏ qua lớp kiểm tra thiết bị (backward-compatible).
        /// </summary>
        [MaxLength(256)]
        public string? DeviceId { get; set; }
    }
}
