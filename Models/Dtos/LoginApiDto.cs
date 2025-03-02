namespace TheWanderLustWebAPI.Models.Dto
{
    public class LoginApiDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}