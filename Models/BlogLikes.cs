using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Models;
public class BlogLikes
{
    [Key]
    public int Id { get; set; }
    [Required]
    public int BlogId { get; set; }
    [JsonIgnore]
    public Blog Blog { get; set; }
    [Required]
    public string UserEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

