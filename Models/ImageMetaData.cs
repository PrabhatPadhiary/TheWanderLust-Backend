using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Models
{
    public class ImageMetadata
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Url { get; set; }
        
        public int Width { get; set; }
        
        public int Height { get; set; }
        
        public int BlogId { get; set; }
        [ForeignKey("BlogId")]
        [JsonIgnore]
        public Blog Blog { get; set; }
    }
}