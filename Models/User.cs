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
        /// <summary>
        /// Where the photo came from: "google" (copied from their Google
        /// account at sign-in, and updated when that changes), "upload"
        /// (chosen in Wayraa: never replaced), "removed" (they removed it:
        /// never brought back), or null (nothing yet).
        /// </summary>
        public string? PhotoSource { get; set; }
        /// <summary>The Google photo address last copied, to notice when it changes.</summary>
        public string? GooglePhotoSourceUrl { get; set; }
        /// <summary>A line or two about you, shown on your public profile (max 160).</summary>
        public string? Bio { get; set; }
        public string Role { get; set; } = "User";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}
