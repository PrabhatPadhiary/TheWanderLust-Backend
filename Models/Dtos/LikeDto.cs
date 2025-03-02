namespace TheWanderLustWebAPI.Models.Dto
{
    public class LikeDto
    {
        public int BlogId { get; set; }
        public string UserEmail { get; set; }
    }

    public class LikeResponseDto
    {
        public int LikeCount { get; set; }
    }
}