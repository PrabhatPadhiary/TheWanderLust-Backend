
using System.ComponentModel.DataAnnotations;

namespace TheWanderLustWebAPI.Models.Dto
{
    public class BlogReqDto
    {
        [Required]
        public string Heading { get; set; }
        [Required]
        public string Tagline { get; set; }
        [Required]
        public string Content { get; set; }
        [Required]
        public string Location { get; set; }
        public List<IFormFile>? Images { get; set; }
        public List<int>? ImageWidths { get; set; }
        public List<int>? ImageHeights { get; set; }
        public string UserEmail { get; set; }
        public int likeCount { get; set; }

    }
}

