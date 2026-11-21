using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// Announcements: notes from the trip's owner to everyone on it, shown at
    /// the top of the trip. Everyone on the trip can read them; only the owner
    /// can post, edit and delete. A new one is pushed to the others straight
    /// away (and lands in their bell); edits don't notify.
    /// </summary>
    [ApiController]
    [Route("api/trips/{tripId}/announcements")]
    [Authorize]
    public class TripAnnouncementsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly NotificationSender _sender;
        private readonly ILogger<TripAnnouncementsController> _logger;

        public TripAnnouncementsController(AppDbContext dbContext, NotificationSender sender,
            ILogger<TripAnnouncementsController> logger)
        {
            _dbContext = dbContext;
            _sender = sender;
            _logger = logger;
        }

        public record AnnouncementDto(string Text);

        [HttpGet]
        public async Task<IActionResult> GetAll(Guid tripId)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            if (await GetRole(tripId, user.Id) == null)
                return NotFound("Trip not found.");

            var items = await _dbContext.TripAnnouncements
                .Where(a => a.TripId == tripId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.Text,
                    a.CreatedAt,
                    a.EditedAt,
                    Author = a.Author == null ? null : new { a.Author.Id, a.Author.Name, a.Author.PhotoUrl },
                })
                .ToListAsync();
            return Ok(items);
        }

        [HttpPost]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] AnnouncementDto dto)
        {
            var text = Clean(dto?.Text);
            if (text == null)
                return BadRequest("Write something to announce.");
            if (text.Length > TripAnnouncement.MaxLength)
                return BadRequest($"Keep it under {TripAnnouncement.MaxLength} characters.");

            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var announcement = new TripAnnouncement
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                AuthorId = user.Id,
                Text = text,
                CreatedAt = DateTime.UtcNow,
            };
            _dbContext.TripAnnouncements.Add(announcement);
            await _dbContext.SaveChangesAsync();

            // Tell everyone else on the trip, now (not grouped like other changes).
            try
            {
                var trip = await _dbContext.Trips
                    .Where(t => t.Id == tripId)
                    .Select(t => new { t.Name, MemberIds = t.Members.Select(m => m.UserId).ToList() })
                    .FirstAsync();
                var firstName = (user.Name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "The owner";
                await _sender.SendAsync(
                    trip.MemberIds.Where(id => id != user.Id),
                    tripId, user.Id, "announcement",
                    title: trip.Name,
                    body: $"{firstName}: {text}",
                    pushTitle: $"📌 {trip.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Announcements: couldn't notify trip {TripId}", tripId);
            }

            return Ok(Shape(announcement, user));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid tripId, Guid id, [FromBody] AnnouncementDto dto)
        {
            var text = Clean(dto?.Text);
            if (text == null)
                return BadRequest("Write something to announce.");
            if (text.Length > TripAnnouncement.MaxLength)
                return BadRequest($"Keep it under {TripAnnouncement.MaxLength} characters.");

            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var announcement = await _dbContext.TripAnnouncements
                .Include(a => a.Author)
                .FirstOrDefaultAsync(a => a.Id == id && a.TripId == tripId);
            if (announcement == null)
                return NotFound("Announcement not found.");

            if (announcement.Text != text)
            {
                announcement.Text = text;
                announcement.EditedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
            return Ok(Shape(announcement, announcement.Author));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid tripId, Guid id)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var deleted = await _dbContext.TripAnnouncements
                .Where(a => a.Id == id && a.TripId == tripId)
                .ExecuteDeleteAsync();
            if (deleted == 0)
                return NotFound("Announcement not found.");
            return Ok(new { message = "Announcement deleted." });
        }

        private static object Shape(TripAnnouncement a, User? author) => new
        {
            a.Id,
            a.Text,
            a.CreatedAt,
            a.EditedAt,
            Author = author == null ? null : new { author.Id, author.Name, author.PhotoUrl },
        };

        /// <summary>Trimmed text, null when empty; runs of 3+ blank lines squeezed.</summary>
        private static string? Clean(string? text)
        {
            var t = text?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            return System.Text.RegularExpressions.Regex.Replace(t.Replace("\r\n", "\n"), "\n{3,}", "\n\n");
        }

        private async Task<User?> GetCurrentUser()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
        }

        /// <summary>owner | member | viewer, or null when not on the trip.</summary>
        private async Task<string?> GetRole(Guid tripId, Guid userId)
        {
            var role = await _dbContext.TripMembers
                .Where(m => m.TripId == tripId && m.UserId == userId)
                .Select(m => m.Role)
                .FirstOrDefaultAsync();
            if (role != null) return role;
            // Older trips: the creator may have no member row.
            var isCreator = await _dbContext.Trips.AnyAsync(t => t.Id == tripId && t.UserId == userId);
            return isCreator ? "owner" : null;
        }
    }
}
