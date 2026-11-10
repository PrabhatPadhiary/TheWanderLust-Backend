using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/trips/{tripId}/itinerary")]
    [Authorize]
    public class ItineraryController : ControllerBase
    {
        private static readonly string[] ValidSlots = { "morning", "afternoon", "evening", "anytime" };
        private readonly AppDbContext _dbContext;

        /// <summary>Normalised slot, null for none; false if it isn't a valid slot.</summary>
        private static bool TryParseSlot(string? value, out string? slot)
        {
            slot = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
            return slot == null || ValidSlots.Contains(slot);
        }

        public ItineraryController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Get all itinerary items for a trip. All members can view.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll(Guid tripId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");

            var items = await _dbContext.ItineraryItems
                .Where(i => i.TripId == tripId)
                .OrderBy(i => i.ScheduledDate)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.StartTime)
                .Select(i => new
                {
                    i.Id,
                    i.TripId,
                    i.DestinationId,
                    i.TripPlaceId,
                    i.Title,
                    i.Category,
                    i.ScheduledDate,
                    i.StartTime,
                    i.EndTime,
                    i.Notes,
                    i.Slot,
                    i.SortOrder,
                    i.CreatedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        /// <summary>
        /// Create an itinerary item. Owner and members can create.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] CreateItineraryItemDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Title))
                return BadRequest("Title is required.");

            var validCategories = new[] { "stay", "food", "activity", "transport", "custom" };
            var category = string.IsNullOrWhiteSpace(dto.Category) ? "custom" : dto.Category.ToLower();
            if (!validCategories.Contains(category))
                return BadRequest("Category must be one of: stay, food, activity, transport, custom.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            if (!TryParseSlot(dto.Slot, out var slot))
                return BadRequest("Slot must be one of: morning, afternoon, evening, anytime.");

            // Verify destination belongs to this trip
            var destinationExists = await _dbContext.TripDestinations
                .AnyAsync(d => d.Id == dto.DestinationId && d.TripId == tripId);
            if (!destinationExists)
                return BadRequest("Destination not found in this trip.");

            var item = new ItineraryItem
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                DestinationId = dto.DestinationId,
                TripPlaceId = dto.TripPlaceId,
                Title = dto.Title,
                Category = category,
                ScheduledDate = DateTime.SpecifyKind(dto.ScheduledDate, DateTimeKind.Utc),
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Notes = dto.Notes,
                Slot = slot,
                SortOrder = dto.SortOrder ?? 0,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ItineraryItems.Add(item);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                item.Id,
                item.TripId,
                item.DestinationId,
                item.TripPlaceId,
                item.Title,
                item.Category,
                item.ScheduledDate,
                item.StartTime,
                item.EndTime,
                item.Notes,
                item.Slot,
                item.SortOrder,
                item.CreatedAt
            });
        }

        /// <summary>
        /// Update an itinerary item. Owner and members can update.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid tripId, Guid id, [FromBody] UpdateItineraryItemDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var item = await _dbContext.ItineraryItems
                .FirstOrDefaultAsync(i => i.Id == id && i.TripId == tripId);

            if (item == null)
                return NotFound("Itinerary item not found.");

            if (dto.DestinationId.HasValue)
            {
                var destinationExists = await _dbContext.TripDestinations
                    .AnyAsync(d => d.Id == dto.DestinationId.Value && d.TripId == tripId);
                if (!destinationExists)
                    return BadRequest("Destination not found in this trip.");
                item.DestinationId = dto.DestinationId.Value;
            }

            if (dto.TripPlaceId.HasValue)
                item.TripPlaceId = dto.TripPlaceId.Value == Guid.Empty ? null : dto.TripPlaceId;

            if (!string.IsNullOrWhiteSpace(dto.Title))
                item.Title = dto.Title;

            if (!string.IsNullOrWhiteSpace(dto.Category))
            {
                var validCategories = new[] { "stay", "food", "activity", "transport", "custom" };
                var category = dto.Category.ToLower();
                if (!validCategories.Contains(category))
                    return BadRequest("Category must be one of: stay, food, activity, transport, custom.");
                item.Category = category;
            }

            if (dto.ScheduledDate.HasValue)
                item.ScheduledDate = DateTime.SpecifyKind(dto.ScheduledDate.Value, DateTimeKind.Utc);

            if (dto.StartTime != null)
                item.StartTime = dto.StartTime;

            if (dto.EndTime != null)
                item.EndTime = dto.EndTime;

            if (dto.Notes != null)
                item.Notes = dto.Notes;

            if (dto.Slot != null)
            {
                if (!TryParseSlot(dto.Slot, out var slot))
                    return BadRequest("Slot must be one of: morning, afternoon, evening, anytime.");
                item.Slot = slot;
            }

            if (dto.SortOrder.HasValue)
                item.SortOrder = dto.SortOrder.Value;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                item.Id,
                item.TripId,
                item.DestinationId,
                item.TripPlaceId,
                item.Title,
                item.Category,
                item.ScheduledDate,
                item.StartTime,
                item.EndTime,
                item.Notes,
                item.Slot,
                item.SortOrder,
                item.CreatedAt
            });
        }

        /// <summary>
        /// Save a drag-and-drop in one go: each item's day, part of day and
        /// order. Only items of this trip are touched. Owner and members.
        /// </summary>
        [HttpPut("arrange")]
        public async Task<IActionResult> Arrange(Guid tripId, [FromBody] List<ArrangeItineraryItemDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return BadRequest("Nothing to arrange.");
            if (dtos.Count > 200)
                return BadRequest("Too many items at once.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var ids = dtos.Select(d => d.Id).ToList();
            var items = await _dbContext.ItineraryItems
                .Where(i => i.TripId == tripId && ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            foreach (var dto in dtos)
            {
                if (!items.TryGetValue(dto.Id, out var item))
                    continue; // deleted meanwhile (e.g. by another traveller)
                if (!TryParseSlot(dto.Slot, out var slot))
                    return BadRequest("Slot must be one of: morning, afternoon, evening, anytime.");
                if (dto.ScheduledDate != default)
                    item.ScheduledDate = DateTime.SpecifyKind(dto.ScheduledDate, DateTimeKind.Utc);
                item.Slot = slot;
                item.SortOrder = dto.SortOrder;
                if (dto.ClearTime)
                {
                    item.StartTime = null;
                    item.EndTime = null;
                }
            }

            await _dbContext.SaveChangesAsync();

            return Ok(items.Values.Select(item => new
            {
                item.Id,
                item.TripId,
                item.DestinationId,
                item.TripPlaceId,
                item.Title,
                item.Category,
                item.ScheduledDate,
                item.StartTime,
                item.EndTime,
                item.Notes,
                item.Slot,
                item.SortOrder,
                item.CreatedAt
            }));
        }

        /// <summary>
        /// Delete an itinerary item. Owner and members can delete.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid tripId, Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var item = await _dbContext.ItineraryItems
                .FirstOrDefaultAsync(i => i.Id == id && i.TripId == tripId);

            if (item == null)
                return NotFound("Itinerary item not found.");

            _dbContext.ItineraryItems.Remove(item);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Itinerary item deleted." });
        }

        /// <summary>
        /// Delete all itinerary items whose ScheduledDate falls outside the given date range.
        /// Called by the frontend after destination dates are updated.
        /// </summary>
        [HttpDelete("outside-range")]
        public async Task<IActionResult> DeleteOutsideRange(Guid tripId, [FromBody] DateRangeDto dto)
        {
            if (dto.StartDate == default || dto.EndDate == default)
                return BadRequest("startDate and endDate are required.");

            if (dto.EndDate < dto.StartDate)
                return BadRequest("endDate must be after startDate.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var startDate = DateTime.SpecifyKind(dto.StartDate.Date, DateTimeKind.Utc);
            var endDate = DateTime.SpecifyKind(dto.EndDate.Date, DateTimeKind.Utc);

            var orphanedItems = await _dbContext.ItineraryItems
                .Where(i => i.TripId == tripId
                    && (i.ScheduledDate.Date < startDate || i.ScheduledDate.Date > endDate))
                .ToListAsync();

            if (orphanedItems.Count == 0)
                return Ok(new { deleted = 0 });

            _dbContext.ItineraryItems.RemoveRange(orphanedItems);
            await _dbContext.SaveChangesAsync();

            return Ok(new { deleted = orphanedItems.Count });
        }

        private async Task<Guid?> GetCurrentUserId()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return user?.Id;
        }

        private async Task<string?> GetMemberRole(Guid tripId, Guid userId)
        {
            var member = await _dbContext.TripMembers
                .FirstOrDefaultAsync(m => m.TripId == tripId && m.UserId == userId);
            return member?.Role;
        }
    }
}
