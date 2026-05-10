using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.DTO
{
    public class RegisterRequestDto
    {
        [Required]
        [MinLength(2)]
        [MaxLength(50)]
        public string Username { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [MinLength(6)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public string[] Roles { get; set; }
    }
}
