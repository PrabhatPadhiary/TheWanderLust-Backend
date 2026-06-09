namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateInvitationDto
    {
        public string Role { get; set; } = "member";
        public int ExpiresInHours { get; set; } = 48;
    }
}
