using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace TheWanderLustWebAPI.Models;

public class User
{
    [Key]
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Role { get; set; }
    public string Token { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? RefreshToken {get; set; }
    public DateTime RefreshTokenExpiryTime  { get; set; }

    [JsonIgnore]
    public List<Blog> Blogs{ get; set; } = new();
}

