using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// The phones that get your push notifications. The app registers its
    /// token after sign-in (and on every start) and removes it on log out.
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
