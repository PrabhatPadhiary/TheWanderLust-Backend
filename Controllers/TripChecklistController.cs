using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/trips/{tripId}/checklist")]
    [Authorize]
    public class TripChecklistController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripChecklistController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Get all checklist items for a trip. All members can view.
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

            var items = await _dbContext.TripChecklistItems
                .Include(c => c.AssignedToUser)
                .Where(c => c.TripId == tripId)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.TripId,
                    c.Title,
                    c.Category,
                    c.DueDate,
                    c.AssignedToUserId,
                    AssignedToName = c.AssignedToUser != null ? c.AssignedToUser.Name : null,
                    c.IsCompleted,
                    c.CompletedAt,
                    c.CompletedByUserId,
                    c.CreatedByUserId,
                    c.CreatedAt,
                    c.Order
                })
                .ToListAsync();

            return Ok(items);
        }

        /// <summary>
        /// Create a checklist item. Owner and members can create.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] CreateTripChecklistItemDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Title))
                return BadRequest("Title is required.");

            var validCategories = new[] { "packing", "bookings", "documents", "money", "safety", "other" };
            var category = string.IsNullOrWhiteSpace(dto.Category) ? "other" : dto.Category.ToLower();
            if (!validCategories.Contains(category))
                return BadRequest("Category must be one of: packing, bookings, documents, money, safety, other.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            // Look up assigned user's name if provided
            string? assignedToName = null;
            if (dto.AssignedToUserId.HasValue)
            {
                var assignedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == dto.AssignedToUserId.Value);
                assignedToName = assignedUser?.Name;
            }

            var item = new TripChecklistItem
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                Title = dto.Title,
                Category = category,
                DueDate = dto.DueDate.HasValue ? DateTime.SpecifyKind(dto.DueDate.Value, DateTimeKind.Utc) : null,
                AssignedToUserId = dto.AssignedToUserId,
                CreatedByUserId = userId.Value,
                CreatedAt = DateTime.UtcNow,
                Order = dto.Order
            };

            _dbContext.TripChecklistItems.Add(item);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                item.Id,
                item.TripId,
                item.Title,
                item.Category,
                item.DueDate,
                item.AssignedToUserId,
                AssignedToName = assignedToName,
                item.IsCompleted,
                item.CompletedAt,
                item.CompletedByUserId,
                item.CreatedByUserId,
                item.CreatedAt,
                item.Order
            });
        }

        /// <summary>
        /// Update a checklist item (title, category, due date, assignment, completion, order).
        /// Owner and members can update.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid tripId, Guid id, [FromBody] UpdateTripChecklistItemDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var item = await _dbContext.TripChecklistItems
                .Include(c => c.AssignedToUser)
                .FirstOrDefaultAsync(c => c.Id == id && c.TripId == tripId);

            if (item == null)
                return NotFound("Checklist item not found.");

            if (!string.IsNullOrWhiteSpace(dto.Title))
                item.Title = dto.Title;

            if (!string.IsNullOrWhiteSpace(dto.Category))
            {
                var validCategories = new[] { "packing", "bookings", "documents", "money", "safety", "other" };
                var category = dto.Category.ToLower();
                if (!validCategories.Contains(category))
                    return BadRequest("Category must be one of: packing, bookings, documents, money, safety, other.");
                item.Category = category;
            }

            if (dto.DueDate.HasValue)
                item.DueDate = DateTime.SpecifyKind(dto.DueDate.Value, DateTimeKind.Utc);

            if (dto.AssignedToUserId.HasValue)
            {
                if (dto.AssignedToUserId.Value == Guid.Empty)
                {
                    item.AssignedToUserId = null;
                    item.AssignedToUser = null;
                }
                else
                {
                    item.AssignedToUserId = dto.AssignedToUserId;
                    item.AssignedToUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == dto.AssignedToUserId.Value);
                }
            }

            if (dto.Order.HasValue)
                item.Order = dto.Order.Value;

            if (dto.IsCompleted.HasValue)
            {
                item.IsCompleted = dto.IsCompleted.Value;
                if (dto.IsCompleted.Value)
                {
                    item.CompletedAt = DateTime.UtcNow;
                    item.CompletedByUserId = userId.Value;
                }
                else
                {
                    item.CompletedAt = null;
                    item.CompletedByUserId = null;
                }
            }

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                item.Id,
                item.TripId,
                item.Title,
                item.Category,
                item.DueDate,
                item.AssignedToUserId,
                AssignedToName = item.AssignedToUser?.Name,
                item.IsCompleted,
                item.CompletedAt,
                item.CompletedByUserId,
                item.CreatedByUserId,
                item.CreatedAt,
                item.Order
            });
        }

        /// <summary>
        /// Toggle completion status of a checklist item. Owner and members can toggle.
        /// </summary>
        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> ToggleComplete(Guid tripId, Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var item = await _dbContext.TripChecklistItems
                .Include(c => c.AssignedToUser)
                .FirstOrDefaultAsync(c => c.Id == id && c.TripId == tripId);

            if (item == null)
                return NotFound("Checklist item not found.");

            item.IsCompleted = !item.IsCompleted;

            if (item.IsCompleted)
            {
                item.CompletedAt = DateTime.UtcNow;
                item.CompletedByUserId = userId.Value;
            }
            else
            {
                item.CompletedAt = null;
                item.CompletedByUserId = null;
            }

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                item.Id,
                item.TripId,
                item.Title,
                item.Category,
                item.DueDate,
                item.AssignedToUserId,
                AssignedToName = item.AssignedToUser?.Name,
                item.IsCompleted,
                item.CompletedAt,
                item.CompletedByUserId,
                item.CreatedByUserId,
                item.CreatedAt,
                item.Order
            });
        }

        /// <summary>
        /// Delete a checklist item. Owner and members can delete.
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

            var item = await _dbContext.TripChecklistItems
                .FirstOrDefaultAsync(c => c.Id == id && c.TripId == tripId);

            if (item == null)
                return NotFound("Checklist item not found.");

            _dbContext.TripChecklistItems.Remove(item);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Checklist item deleted." });
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
