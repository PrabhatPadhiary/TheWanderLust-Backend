namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateTripChecklistItemDto
    {
        public string Title { get; set; }
        public string Category { get; set; } = "other";
        public DateTime? DueDate { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public int Order { get; set; }
    }

    public class UpdateTripChecklistItemDto
    {
        public string? Title { get; set; }
        public string? Category { get; set; }
        public DateTime? DueDate { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public bool? IsCompleted { get; set; }
        public int? Order { get; set; }
    }
}
