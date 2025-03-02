using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Models;
public class Blog
{
    [Key]
    public int Id { get; set; }
    public string Heading { get; set; }
    public string Tagline { get; set; }
    public string Content { get; set; }
    public string Location { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    [Required]
    [ForeignKey("User")]
    public string UserEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore]
    public User User{ get; set; }
    public List<BlogLikes> Likes { get; set; } = new();
    [JsonIgnore]
    public ICollection<ImageMetadata> ImagesMetadata { get; set; }
    [JsonIgnore]
    public ICollection<BlogComments> Comments { get; set; }
}

