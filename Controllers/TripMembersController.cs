using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/trips/{tripId}/members")]
    [Authorize]
    public class TripMembersController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripMembersController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Change a member's role. Owner only.
        /// </summary>
        [HttpPut("{memberId}/role")]
        public async Task<IActionResult> ChangeRole(Guid tripId, Guid memberId, [FromBody] ChangeRoleDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var callerRole = await GetMemberRole(tripId, userId.Value);
            if (callerRole == null)
                return NotFound("Trip not found.");
            if (callerRole != "owner")
                return Forbid();

            var validRoles = new[] { "member", "viewer" };
            var newRole = string.IsNullOrWhiteSpace(dto?.Role) ? "" : dto.Role.ToLower();
            if (!validRoles.Contains(newRole))
                return BadRequest("Role must be 'member' or 'viewer'.");

            var member = await _dbContext.TripMembers
                .FirstOrDefaultAsync(m => m.TripId == tripId && m.UserId == memberId);

            if (member == null)
                return NotFound("Member not found.");

            // Can't change the owner's role
            if (member.Role == "owner")
                return BadRequest("Cannot change the owner's role.");

            member.Role = newRole;
            await _dbContext.SaveChangesAsync();

            return Ok(new { member.UserId, member.Role });
        }

        /// <summary>
        /// Remove a member from the trip. Owner only.
        /// </summary>
        [HttpDelete("{memberId}")]
        public async Task<IActionResult> Remove(Guid tripId, Guid memberId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var callerRole = await GetMemberRole(tripId, userId.Value);
            if (callerRole == null)
                return NotFound("Trip not found.");
            if (callerRole != "owner")
                return Forbid();

            // Owner can't remove themselves
            if (memberId == userId.Value)
                return BadRequest("Owner cannot remove themselves from the trip.");

            var member = await _dbContext.TripMembers
                .FirstOrDefaultAsync(m => m.TripId == tripId && m.UserId == memberId);

            if (member == null)
                return NotFound("Member not found.");

            _dbContext.TripMembers.Remove(member);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Member removed from trip." });
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

    public class ChangeRoleDto
    {
        public string Role { get; set; }
    }
}
