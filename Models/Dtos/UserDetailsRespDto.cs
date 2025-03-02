
using System.ComponentModel.DataAnnotations;

namespace TheWanderLustWebAPI.Models.Dto
{
    public class UserDetailsRespDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string ProfilePicUrl { get; set; }
        public string Username { get; set; }
    }
}

