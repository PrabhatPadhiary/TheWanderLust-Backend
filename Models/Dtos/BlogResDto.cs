
using System.ComponentModel.DataAnnotations;

namespace TheWanderLustWebAPI.Models.Dto
{
    public class BlogResDto
    {
        [Required]
        public string Heading { get; set; }
        [Required]
        public string Tagline { get; set; }
        [Required]
        public string Content { get; set; }
        [Required]
        public string Location { get; set; }
        public List<string>? Images { get; set; }
        public List<ImageMetadata> ImageMetadata{ get; set; }
        public string UserEmail { get; set; }
        public int likeCount { get; set; }

    }
}

