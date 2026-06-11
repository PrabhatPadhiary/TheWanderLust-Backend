using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class InvitationsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public InvitationsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Create an invitation link for a trip. Only the trip owner can create invitations.
        /// </summary>
        [HttpPost("trips/{tripId}/invitations")]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] CreateInvitationDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip == null)
                return NotFound("Trip not found.");

            // Only the trip owner or members (editors) can create invitations
            var canInvite = trip.Members.Any(m => m.UserId == userId.Value && (m.Role == "owner" || m.Role == "member"));
            if (!canInvite)
                return Forbid();

            var validRoles = new[] { "member", "viewer" };
            var role = string.IsNullOrWhiteSpace(dto.Role) ? "member" : dto.Role.ToLower();
            if (!validRoles.Contains(role))
                return BadRequest("Role must be 'member' or 'viewer'.");

            var expiresInHours = dto.ExpiresInHours > 0 ? dto.ExpiresInHours : 48;

            var invitation = new Invitation
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                Role = role,
                ExpiresAt = DateTime.UtcNow.AddHours(expiresInHours),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Invitations.Add(invitation);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                invitation.Id,
                invitation.TripId,
                invitation.Role,
                invitation.ExpiresAt
            });
        }

        /// <summary>
        /// Get invitation details without joining (for showing a preview to the user).
        /// </summary>
        [HttpGet("join/{inviteId}")]
        public async Task<IActionResult> GetInvitation(Guid inviteId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var invitation = await _dbContext.Invitations
                .Include(i => i.Trip)
                .FirstOrDefaultAsync(i => i.Id == inviteId);

            if (invitation == null)
                return NotFound("Invitation not found.");

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return BadRequest("This invitation has expired.");

            if (invitation.UsedBy != null)
                return BadRequest("This invitation has already been used.");

            return Ok(new
            {
                invitation.Id,
                invitation.Role,
                invitation.ExpiresAt,
                Trip = new
                {
                    invitation.Trip.Id,
                    invitation.Trip.Name,
                    invitation.Trip.Description,
                    invitation.Trip.PrimaryDestination,
                    invitation.Trip.CoverPhotoUrl
                }
            });
        }

        /// <summary>
        /// Join a trip using an invitation link. The role is determined by the invitation, preventing URL tampering.
        /// </summary>
        [HttpPost("join/{inviteId}")]
        public async Task<IActionResult> Join(Guid inviteId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var invitation = await _dbContext.Invitations
                .Include(i => i.Trip)
                    .ThenInclude(t => t.Destinations)
                .Include(i => i.Trip)
                    .ThenInclude(t => t.Members)
                .FirstOrDefaultAsync(i => i.Id == inviteId);

            if (invitation == null)
                return NotFound("Invitation not found.");

            if (invitation.ExpiresAt < DateTime.UtcNow)
                return BadRequest("This invitation has expired.");

            if (invitation.UsedBy != null)
                return BadRequest("This invitation has already been used.");

            // Check if user is already a member
            var alreadyMember = invitation.Trip.Members.Any(m => m.UserId == userId.Value);
            if (alreadyMember)
                return Conflict("You are already a member of this trip.");

            // Add user as a trip member with the role specified in the invitation
            var member = new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = invitation.TripId,
                UserId = userId.Value,
                Role = invitation.Role,
                JoinedAt = DateTime.UtcNow
            };

            _dbContext.TripMembers.Add(member);

            // Mark invitation as used
            invitation.UsedBy = userId.Value;

            await _dbContext.SaveChangesAsync();

            var trip = invitation.Trip;

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.Description,
                trip.StartDate,
                trip.EndDate,
                trip.CoverPhotoUrl,
                trip.TravelersCount,
                trip.PrimaryDestination,
                trip.TotalBudget,
                trip.Currency,
                trip.Status,
                trip.CreatedAt,
                MemberRole = invitation.Role,
                Destinations = trip.Destinations.Select(d => new
                {
                    d.Id,
                    d.GooglePlaceId,
                    d.Name,
                    d.Latitude,
                    d.Longitude,
                    d.PhotoUrl,
                    d.Order,
                    d.StartDate,
                    d.EndDate
                })
            });
        }

        /// <summary>
        /// Get all invitations for a trip. Owner and editors can view.
        /// </summary>
        [HttpGet("trips/{tripId}/invitations")]
        public async Task<IActionResult> GetAll(Guid tripId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var invitations = await _dbContext.Invitations
                .Where(i => i.TripId == tripId)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new
                {
                    i.Id,
                    i.TripId,
                    i.Role,
                    i.ExpiresAt,
                    i.UsedBy,
                    i.CreatedAt
                })
                .ToListAsync();

            return Ok(invitations);
        }

        /// <summary>
        /// Revoke (delete) an invitation. Owner and editors can revoke.
        /// </summary>
        [HttpDelete("trips/{tripId}/invitations/{inviteId}")]
        public async Task<IActionResult> Revoke(Guid tripId, Guid inviteId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var invitation = await _dbContext.Invitations
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.TripId == tripId);

            if (invitation == null)
                return NotFound("Invitation not found.");

            if (invitation.UsedBy != null)
                return BadRequest("Cannot revoke an invitation that has already been used.");

            _dbContext.Invitations.Remove(invitation);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Invitation revoked." });
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
