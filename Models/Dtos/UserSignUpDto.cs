using System.ComponentModel.DataAnnotations;

namespace TheWanderLustWebAPI.Models.Dto
{
    public class UserSignUpDto
    {
        [Required]
        public string FirstName { get; set; }
        [Required]
        public string LastName { get; set; }
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        public string Username { get; set; }
        [Required]
        public string Password { get; set; }
        public IFormFile? ProfilePicture { get; set; }
    }
}

