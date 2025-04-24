
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
        public List<ImageMetadata> ImageMetadata { get; set; }
        public string UserEmail { get; set; }
        public int likeCount { get; set; }

    }

    public class BlogDTO
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string Heading { get; set; }
        public string Tagline { get; set; }
        public string Content { get; set; }
        public List<string> ImageUrls { get; set; }
        public string Location { get; set; }
        public int LikeCount { get; set; }
        public List<BlogLikes> Likes { get; set; }
        public List<ImageMetadata> ImagesMetaData { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Username { get; set; }
        public string ProfilePicUrl { get; set; }
        public BlogComments LatestComment { get; set; }
        public int CommentCount { get; set; }
    }
}

