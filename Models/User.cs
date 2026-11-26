namespace TheWanderLustWebAPI.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string FirebaseId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        /// <summary>Profile photo (Cloudinary URL), or null for initials.</summary>
        public string? PhotoUrl { get; set; }
        /// <summary>A line or two about you, shown on your public profile (max 160).</summary>
        public string? Bio { get; set; }
        public string Role { get; set; } = "User";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}
