using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// The signed-in user's own profile: details, travel stats, display name,
    /// and deleting the account.
    /// </summary>
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private const int MaxNameLength = 60;
        private const string FormerTraveller = "Former traveller";
        private readonly AppDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(AppDbContext dbContext, CloudinaryService cloudinaryService, ILogger<UsersController> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        /// <summary>
        /// Profile plus travel stats. Trips count every trip the user is on
        /// (owned or joined); destinations are distinct places across them.
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();

            var tripIds = _dbContext.TripMembers
                .Where(m => m.UserId == user.Id)
                .Select(m => m.TripId)
                .Union(_dbContext.Trips.Where(t => t.UserId == user.Id).Select(t => t.Id));

            var statuses = await _dbContext.Trips
                .Where(t => tripIds.Contains(t.Id))
                .Select(t => t.Status)
                .ToListAsync();

            var destinations = await _dbContext.TripDestinations
                .Where(d => tripIds.Contains(d.TripId))
                .Select(d => d.GooglePlaceId)
                .Distinct()
                .CountAsync();

            var placesSaved = await _dbContext.TripPlaces
                .Where(p => tripIds.Contains(p.TripDestination.TripId))
                .CountAsync();

            var favourites = await _dbContext.Favourites.CountAsync(f => f.UserId == user.Id);
            var journals = await _dbContext.Journals.CountAsync(j => j.UserId == user.Id);

            return Ok(new
            {
                user.Id,
                user.Name,
                user.Email,
                user.PhotoUrl,
                user.CreatedAt,
                Stats = new
                {
                    Trips = statuses.Count,
                    UpcomingTrips = statuses.Count(s => s == "planning" || s == "in_progress"),
                    CompletedTrips = statuses.Count(s => s == "completed"),
                    Destinations = destinations,
                    PlacesSaved = placesSaved,
                    Favourites = favourites,
                    Journals = journals
                }
            });
        }

        /// <summary>
        /// Update the display name (shown to other travellers on shared trips).
        /// </summary>
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();

            var name = dto?.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return BadRequest("Name is required.");
            if (name.Length > MaxNameLength)
                return BadRequest($"Name must be {MaxNameLength} characters or fewer.");

            user.Name = name;
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.FirebaseId,
                user.Name,
                user.Email,
                user.PhotoUrl,
                user.Role,
                user.CreatedAt,
                user.IsActive
            });
        }

        private const long MaxPhotoBytes = 10 * 1024 * 1024;
        private static readonly string[] PhotoTypes = { "image/jpeg", "image/png", "image/webp", "image/heic", "image/heif" };

        /// <summary>
        /// Set the profile photo (one image, multipart field "file"). Replaces
        /// the previous one; shown on journals and shared trips.
        /// </summary>
        [HttpPost("me/photo")]
        [RequestSizeLimit(MaxPhotoBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadPhoto([FromForm] IFormFile file)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("Choose a photo to upload.");
            if (file.Length > MaxPhotoBytes)
                return BadRequest("That photo is too large (10 MB max).");
            if (!string.IsNullOrEmpty(file.ContentType)
                && file.ContentType != "application/octet-stream"
                && !PhotoTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest("Profile photos must be JPEG, PNG, WebP or HEIC images.");

            // One photo per user: clear the folder, then upload the new one.
            var folder = $"users/{user.Id}";
            try
            {
                await _cloudinaryService.DeleteFolderAsync(folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clear old profile photo for user {UserId}.", user.Id);
            }

            string url;
            try
            {
                url = await _cloudinaryService.UploadImageAsync(file, folder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile photo upload failed for user {UserId}.", user.Id);
                return StatusCode(502, "Couldn't upload the photo. Please try again.");
            }

            user.PhotoUrl = url;
            await _dbContext.SaveChangesAsync();
            return Ok(new { user.PhotoUrl });
        }

        /// <summary>Remove the profile photo (back to initials).</summary>
        [HttpDelete("me/photo")]
        public async Task<IActionResult> DeletePhoto()
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();

            if (user.PhotoUrl != null)
            {
                user.PhotoUrl = null;
                await _dbContext.SaveChangesAsync();
                try
                {
                    await _cloudinaryService.DeleteFolderAsync($"users/{user.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete profile photo for user {UserId}.", user.Id);
                }
            }
            return Ok(new { PhotoUrl = (string?)null });
        }

        /// <summary>
        /// Permanently delete the signed-in user's account and data (required
        /// by the App Store and Google Play for apps with sign-up).
        ///
        /// Shared trips survive: a trip the user owns that has other members is
        /// handed to the longest-standing editor (or viewer if there are no
        /// editors). Trips only they are on are deleted. Their checklist items
        /// on surviving trips are credited to the trip's owner, expenses they
        /// paid stay (so totals add up) but show "Former traveller", and their
        /// journal likes and comments are removed. Favourites, journals and
        /// memberships go with the user row (cascade).
        ///
        /// All database changes happen in one transaction. Afterwards the
        /// Firebase login and the user's journal photos (Cloudinary) are
        /// deleted; failures there are logged and reported, not rolled back.
        /// </summary>
        [HttpDelete("me")]
        public async Task<IActionResult> DeleteMe()
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();

            var userId = user.Id;
            var firebaseUid = user.FirebaseId;
            var journalIds = await _dbContext.Journals
                .Where(j => j.UserId == userId)
                .Select(j => j.Id)
                .ToListAsync();

            int tripsTransferred = 0, tripsDeleted = 0;

            await using (var tx = await _dbContext.Database.BeginTransactionAsync())
            {
                // 1. Trips the user owns: hand shared ones on, delete solo ones.
                var ownedTrips = await _dbContext.Trips
                    .Include(t => t.Members)
                    .Where(t => t.UserId == userId
                        || t.Members.Any(m => m.UserId == userId && m.Role == "owner"))
                    .ToListAsync();

                foreach (var trip in ownedTrips)
                {
                    var heir = trip.Members
                        .Where(m => m.UserId != userId)
                        .OrderBy(m => m.Role == "member" ? 0 : 1)
                        .ThenBy(m => m.JoinedAt)
                        .FirstOrDefault();

                    if (heir == null)
                    {
                        _dbContext.Trips.Remove(trip);
                        tripsDeleted++;
                    }
                    else
                    {
                        trip.UserId = heir.UserId;
                        heir.Role = "owner";
                        tripsTransferred++;
                    }
                }
                await _dbContext.SaveChangesAsync();

                // 2. Checklist items they created on trips that remain: credit
                //    them to the trip's (possibly new) owner.
                var checklistItems = await _dbContext.TripChecklistItems
                    .Include(c => c.Trip)
                    .Where(c => c.CreatedByUserId == userId)
                    .ToListAsync();
                foreach (var item in checklistItems)
                    item.CreatedByUserId = item.Trip.UserId;

                // 3. Expenses they paid on shared trips: keep, drop the name.
                var userIdText = userId.ToString();
                var expenses = await _dbContext.TripExpenses
                    .Where(e => e.PaidByMemberId == userIdText)
                    .ToListAsync();
                foreach (var expense in expenses)
                    expense.PaidByName = FormerTraveller;

                // 4. Their likes and comments on journals (no cascade from users).
                _dbContext.JournalLikes.RemoveRange(
                    _dbContext.JournalLikes.Where(l => l.UserId == userId));
                _dbContext.JournalComments.RemoveRange(
                    _dbContext.JournalComments.Where(c => c.UserId == userId));

                // 5. The user. Favourites, journals and memberships cascade;
                //    invitations they used and checklist assignments set null.
                _dbContext.Users.Remove(user);
                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
            }

            // 6. Outside the database: the Firebase login, then journal photos.
            var firebaseDeleted = true;
            try
            {
                await FirebaseAuth.DefaultInstance.DeleteUserAsync(firebaseUid);
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
            {
                // Already gone.
            }
            catch (Exception ex)
            {
                firebaseDeleted = false;
                _logger.LogError(ex, "Account {UserId} deleted, but removing Firebase user {FirebaseUid} failed.", userId, firebaseUid);
            }

            try
            {
                await _cloudinaryService.DeleteFolderAsync($"users/{userId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete Cloudinary profile photo for user {UserId}.", userId);
            }

            foreach (var journalId in journalIds)
            {
                try
                {
                    await _cloudinaryService.DeleteFolderAsync($"journals/{journalId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete Cloudinary photos for journal {JournalId}.", journalId);
                }
            }

            _logger.LogInformation(
                "Account {UserId} deleted: {Transferred} trips handed on, {Deleted} trips deleted.",
                userId, tripsTransferred, tripsDeleted);

            return Ok(new
            {
                message = "Your account has been deleted.",
                tripsTransferred,
                tripsDeleted,
                firebaseDeleted
            });
        }

        private async Task<Models.User?> GetCurrentUser()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            return await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
        }
    }

    public class UpdateProfileDto
    {
        public string? Name { get; set; }
    }
}
