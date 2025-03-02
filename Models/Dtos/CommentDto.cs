
using System.ComponentModel.DataAnnotations;

namespace TheWanderLustWebAPI.Models.Dto
{
    public class CommentReqDto
    {
        public int BlogId { get; set; }
        public string Author { get; set; }
        public string Content { get; set; }
    }

    public class CommentRespDto
    {
        public string Comment { get; set; }
        public string Author { get; set; }
        public DateTime PostedAt { get; set; }
        public int CommentCount { get; set; }
    }
}

