using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// Your notifications: the phones that get them by push (the app registers
    /// its token after sign-in and on every start, and removes it on log out),
    /// and the in-app list behind the bell on Home.
    /// </summary>
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public NotificationsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public record PushTokenDto(string Token, string? Platform);

        /// <summary>
        /// Register this phone for your notifications. A phone that was signed
        /// in to another account moves to yours (one person per phone).
        /// </summary>
        [HttpPost("token")]
        public async Task<IActionResult> Register([FromBody] PushTokenDto dto)
        {
            var token = dto?.Token?.Trim();
            if (!IsExpoToken(token))
                return BadRequest("Not an Expo push token.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var platform = dto!.Platform?.Trim().ToLowerInvariant();
            if (platform is not ("ios" or "android")) platform = null;

            var existing = await _dbContext.PushTokens.FirstOrDefaultAsync(t => t.Token == token);
            if (existing == null)
            {
                _dbContext.PushTokens.Add(new PushToken
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    Token = token!,
                    Platform = platform,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.UserId = userId.Value;
                existing.Platform = platform ?? existing.Platform;
                existing.LastSeenAt = DateTime.UtcNow;
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The same phone registered twice at once: the other request won.
            }
            return NoContent();
        }

        /// <summary>Stop sending to this phone (log out, or notifications turned off).</summary>
        [HttpDelete("token")]
        public async Task<IActionResult> Unregister([FromBody] PushTokenDto dto)
        {
            var token = dto?.Token?.Trim();
            if (string.IsNullOrEmpty(token))
                return BadRequest("Token is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            await _dbContext.PushTokens
                .Where(t => t.Token == token && t.UserId == userId.Value)
                .ExecuteDeleteAsync();
            return NoContent();
        }

        // ── The bell ─────────────────────────────────────────────────────

        /// <summary>
        /// Your notifications, newest first. Pages: pass the last one's
        /// createdAt as `before` for the next page.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateTime? before, [FromQuery] int limit = 30)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            limit = Math.Clamp(limit, 1, 50);
            var query = _dbContext.UserNotifications.Where(n => n.UserId == userId.Value);
            if (before != null)
            {
                var cutoff = DateTime.SpecifyKind(before.Value.ToUniversalTime(), DateTimeKind.Utc);
                query = query.Where(n => n.CreatedAt < cutoff);
            }

            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.Body,
                    n.TripId,
                    n.CreatedAt,
                    IsRead = n.ReadAt != null,
                    Actor = n.Actor == null ? null : new { n.Actor.Id, n.Actor.Name, n.Actor.PhotoUrl },
                })
                .ToListAsync();

            return Ok(items);
        }

        /// <summary>How many are unread (the dot on the bell).</summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var count = await _dbContext.UserNotifications
                .CountAsync(n => n.UserId == userId.Value && n.ReadAt == null);
            return Ok(new { count });
        }

        public record MarkReadDto(DateTime? UpTo);

        /// <summary>
        /// Mark as read everything up to `upTo` (the newest one you've seen),
        /// or everything when it's left out.
        /// </summary>
        [HttpPost("read")]
        public async Task<IActionResult> MarkRead([FromBody] MarkReadDto? dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var query = _dbContext.UserNotifications.Where(n => n.UserId == userId.Value && n.ReadAt == null);
            if (dto?.UpTo != null)
            {
                var upTo = DateTime.SpecifyKind(dto.UpTo.Value.ToUniversalTime(), DateTimeKind.Utc);
                query = query.Where(n => n.CreatedAt <= upTo);
            }
            var now = DateTime.UtcNow;
            await query.ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now));
            return NoContent();
        }

        private static bool IsExpoToken(string? token) =>
            !string.IsNullOrEmpty(token)
            && token.Length <= 200
            && (token.StartsWith("ExponentPushToken[") || token.StartsWith("ExpoPushToken["))
            && token.EndsWith("]");

        private async Task<Guid?> GetCurrentUserId()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return user?.Id;
        }
    }
}
