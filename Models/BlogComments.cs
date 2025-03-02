using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Models;
public class BlogComments
{
    [Key]
    public int CommentId { get; set; }
    [Required]
    public int BlogId { get; set; }
    [Required]
    public string Content { get; set; }
    [Required]
    public string Author { get; set; }
    public DateTime CreatedAt { get; set; }
    [JsonIgnore]
    [ForeignKey("BlogId")]
    public Blog Blog { get; set; }
}

