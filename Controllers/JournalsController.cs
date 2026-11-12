using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JournalsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<JournalsController> _logger;

        public JournalsController(AppDbContext dbContext, CloudinaryService cloudinaryService, ILogger<JournalsController> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        /// <summary>
        /// Public published journals, newest first (the latest FeedLimit). No auth
        /// required; signed in, each says whether you liked it, and stories from
        /// people you blocked or ones you hid / reported are left out.
        /// One database round trip for the stories (plus finding who you are).
        /// </summary>
        [HttpGet("feed")]
        public async Task<IActionResult> GetFeed()
        {
            var userId = await GetCurrentUserId();
            var uid = userId ?? Guid.Empty;
            var signedIn = userId.HasValue;

            var journals = await _dbContext.Journals
                .AsNoTracking()
                .Where(j => j.Status == "published" && j.Visibility == "public")
                .Where(j => !signedIn || (
                    !_dbContext.UserBlocks.Any(b => b.BlockerId == uid && b.BlockedId == j.UserId)
                    && !_dbContext.HiddenJournals.Any(h => h.UserId == uid && h.JournalId == j.Id)))
                .OrderByDescending(j => j.PublishedAt)
                .Take(FeedLimit)
                .Select(j => new
                {
                    j.Id,
                    j.Title,
                    j.Body,
                    j.Destination,
                    j.StartDate,
                    j.EndDate,
                    j.TravelersCount,
                    j.Budget,
                    j.Currency,
                    j.ProTips,
                    j.Vibes,
                    j.LikesCount,
                    j.CommentsCount,
                    j.PublishedAt,
                    j.CreatedAt,
                    Author = new { j.User.Id, j.User.Name, j.User.PhotoUrl },
                    Places = j.Places.Select(p => new
                    {
                        p.Id,
                        p.PlaceName,
                        p.Category,
                        p.GooglePlaceId,
                        p.Latitude,
                        p.Longitude
                    }),
                    Photos = j.Photos.OrderBy(ph => ph.Order).Select(ph => new
                    {
                        ph.Id,
                        ph.Url,
                        ph.Order
                    }),
                    IsLiked = signedIn && _dbContext.JournalLikes.Any(l => l.JournalId == j.Id && l.UserId == uid)
                })
                .ToListAsync();

            return Ok(journals);
        }

        // The feed returns at most this many of the newest stories.
        private const int FeedLimit = 60;

        /// <summary>
        /// Public, published journals that tag a place (by Google place id),
        /// newest first: "Stories from Wayraa travellers" on a place's page.
        /// Same shape as the feed. No auth required (isLiked when signed in).
        /// </summary>
        [HttpGet("by-place/{googlePlaceId}")]
        public async Task<IActionResult> GetByPlace(string googlePlaceId, [FromQuery] int? limit)
        {
            if (string.IsNullOrWhiteSpace(googlePlaceId))
                return BadRequest("Place id is required.");

            var take = Math.Clamp(limit ?? 10, 1, 30);
            var userId = await GetCurrentUserId();
            HashSet<Guid> likedIds = userId.HasValue
                ? new HashSet<Guid>(await _dbContext.JournalLikes
                    .Where(l => l.UserId == userId.Value)
                    .Select(l => l.JournalId)
                    .ToListAsync())
                : new HashSet<Guid>();

            var (blocked, hidden) = await ExclusionsFor(userId);

            var journals = await _dbContext.Journals
                .Where(j => j.Status == "published" && j.Visibility == "public"
                    && j.Places.Any(p => p.GooglePlaceId == googlePlaceId))
                .Where(j => !blocked.Contains(j.UserId) && !hidden.Contains(j.Id))
                .OrderByDescending(j => j.PublishedAt)
                .Take(take)
                .Select(j => new
                {
                    j.Id,
                    j.Title,
                    j.Body,
                    j.Destination,
                    j.StartDate,
                    j.EndDate,
                    j.Vibes,
                    j.LikesCount,
                    j.CommentsCount,
                    j.PublishedAt,
                    j.CreatedAt,
                    Author = new { j.User.Id, j.User.Name, j.User.PhotoUrl },
                    Photos = j.Photos.OrderBy(ph => ph.Order).Select(ph => new { ph.Id, ph.Url, ph.Order })
                })
                .ToListAsync();

            return Ok(journals.Select(j => new
            {
                j.Id,
                j.Title,
                j.Body,
                j.Destination,
                j.StartDate,
                j.EndDate,
                j.Vibes,
                j.LikesCount,
                j.CommentsCount,
                j.PublishedAt,
                j.CreatedAt,
                j.Author,
                j.Photos,
                IsLiked = likedIds.Contains(j.Id)
            }));
        }

        /// <summary>
        /// Get my journals (drafts + published). Auth required.
        /// </summary>
        [HttpGet("mine")]
        [Authorize]
        public async Task<IActionResult> GetMine()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journals = await _dbContext.Journals
                .Include(j => j.Places)
                .Include(j => j.Photos)
                .Include(j => j.User)
                .Where(j => j.UserId == userId.Value)
                .OrderByDescending(j => j.UpdatedAt)
                .Select(j => new
                {
                    j.Id,
                    // Same shape as the feed, so the app can show the same cards.
                    Author = new { j.User.Id, j.User.Name, j.User.PhotoUrl },
                    Photos = j.Photos.OrderBy(ph => ph.Order).Select(ph => new { ph.Id, ph.Url, ph.Order }),
                    j.TripId,
                    j.Title,
                    j.Body,
                    j.Destination,
                    j.StartDate,
                    j.EndDate,
                    j.TravelersCount,
                    j.Budget,
                    j.Currency,
                    j.Visibility,
                    j.ProTips,
                    j.Vibes,
                    j.Status,
                    j.LikesCount,
                    j.CommentsCount,
                    j.CreatedAt,
                    j.UpdatedAt,
                    j.PublishedAt,
                    Places = j.Places.Select(p => new
                    {
                        p.Id,
                        p.PlaceName,
                        p.Category,
                        p.GooglePlaceId,
                        p.Latitude,
                        p.Longitude
                    })
                })
                .ToListAsync();

            return Ok(journals);
        }

        /// <summary>
        /// Get a single journal by id. Public published journals are accessible to all.
        /// Private or draft journals are only accessible to the author.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var journal = await _dbContext.Journals
                .Include(j => j.User)
                .Include(j => j.Places)
                .Include(j => j.Photos)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (journal == null)
                return NotFound("Journal not found.");

            // If it's not public+published, only the author can view
            if (journal.Status != "published" || journal.Visibility != "public")
            {
                var requesterId = await GetCurrentUserId();
                if (requesterId == null || journal.UserId != requesterId.Value)
                    return NotFound("Journal not found.");
            }

            // Check if current user has liked this journal
            var userId = await GetCurrentUserId();
            var isLikedByMe = userId.HasValue && await _dbContext.JournalLikes
                .AnyAsync(l => l.JournalId == id && l.UserId == userId.Value);

            // Someone you blocked: their story isn't shown (as if it's gone);
            // nor are their comments on anyone's story.
            var (blockedUsers, _) = await ExclusionsFor(userId);
            if (blockedUsers.Contains(journal.UserId))
                return NotFound("Journal not found.");

            // Fetch comments with author info
            var comments = await _dbContext.JournalComments
                .Include(c => c.User)
                .Where(c => c.JournalId == id && !blockedUsers.Contains(c.UserId))
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Body,
                    c.CreatedAt,
                    c.UpdatedAt,
                    Author = new { c.User.Id, c.User.Name, c.User.PhotoUrl }
                })
                .ToListAsync();

            return Ok(new
            {
                journal.Id,
                journal.UserId,
                journal.TripId,
                journal.Title,
                journal.Body,
                journal.Destination,
                journal.StartDate,
                journal.EndDate,
                journal.TravelersCount,
                journal.Budget,
                journal.Currency,
                journal.Visibility,
                journal.ProTips,
                Vibes = journal.Vibes != null
                    ? journal.Vibes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>(),
                journal.Status,
                journal.LikesCount,
                journal.CommentsCount,
                journal.CreatedAt,
                journal.UpdatedAt,
                journal.PublishedAt,
                IsLikedByMe = isLikedByMe,
                Author = new { journal.User.Id, journal.User.Name, journal.User.PhotoUrl },
                Photos = journal.Photos
                    .OrderBy(p => p.Order)
                    .Select(p => new { p.Id, p.Url, p.Caption, p.Order }),
                Places = journal.Places.Select(p => new
                {
                    p.Id,
                    p.PlaceName,
                    p.Category,
                    p.GooglePlaceId,
                    p.Latitude,
                    p.Longitude
                }),
                Comments = comments
            });
        }

        /// <summary>
        /// Create a new journal. Auth required.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateJournalDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Title))
                return BadRequest("Title is required.");

            if (string.IsNullOrWhiteSpace(dto.Body))
                return BadRequest("Body is required.");

            if (string.IsNullOrWhiteSpace(dto.Destination))
                return BadRequest("Destination is required.");

            var validVisibility = new[] { "public", "private" };
            var visibility = string.IsNullOrWhiteSpace(dto.Visibility) ? "private" : dto.Visibility.ToLower();
            if (!validVisibility.Contains(visibility))
                return BadRequest("Visibility must be 'public' or 'private'.");

            var validStatus = new[] { "draft", "published" };
            var status = string.IsNullOrWhiteSpace(dto.Status) ? "draft" : dto.Status.ToLower();
            if (!validStatus.Contains(status))
                return BadRequest("Status must be 'draft' or 'published'.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = new Journal
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                TripId = dto.TripId,
                Title = dto.Title,
                Body = dto.Body,
                Destination = dto.Destination,
                StartDate = dto.StartDate.HasValue ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null,
                EndDate = dto.EndDate.HasValue ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null,
                TravelersCount = dto.TravelersCount,
                Budget = dto.Budget,
                Currency = dto.Currency,
                Visibility = visibility,
                ProTips = dto.ProTips,
                Vibes = dto.Vibes != null && dto.Vibes.Count > 0
                    ? string.Join(",", dto.Vibes.Select(v => v.Trim().ToLower()))
                    : null,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PublishedAt = status == "published" ? DateTime.UtcNow : null
            };

            _dbContext.Journals.Add(journal);

            // Add places if provided
            if (dto.Places != null && dto.Places.Count > 0)
            {
                foreach (var p in dto.Places)
                {
                    if (string.IsNullOrWhiteSpace(p.PlaceName))
                        continue;

                    _dbContext.JournalPlaces.Add(new JournalPlace
                    {
                        Id = Guid.NewGuid(),
                        JournalId = journal.Id,
                        PlaceName = p.PlaceName,
                        Category = string.IsNullOrWhiteSpace(p.Category) ? "other" : p.Category.ToLower(),
                        GooglePlaceId = p.GooglePlaceId,
                        Latitude = p.Latitude,
                        Longitude = p.Longitude
                    });
                }
            }

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                journal.Id,
                journal.Title,
                journal.Status,
                journal.Visibility,
                journal.CreatedAt
            });
        }

        /// <summary>
        /// Update a journal. Only the author can update.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateJournalDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = await _dbContext.Journals
                .Include(j => j.Places)
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId.Value);

            if (journal == null)
                return NotFound("Journal not found.");

            if (!string.IsNullOrWhiteSpace(dto.Title))
                journal.Title = dto.Title;

            if (dto.Body != null)
                journal.Body = dto.Body;

            if (dto.Destination != null)
                journal.Destination = dto.Destination;

            if (dto.TripId.HasValue)
                journal.TripId = dto.TripId.Value == Guid.Empty ? null : dto.TripId;

            if (dto.StartDate.HasValue)
                journal.StartDate = DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc);

            if (dto.EndDate.HasValue)
                journal.EndDate = DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc);

            if (dto.TravelersCount.HasValue)
                journal.TravelersCount = dto.TravelersCount;

            if (dto.Budget.HasValue)
                journal.Budget = dto.Budget;

            if (dto.Currency != null)
                journal.Currency = dto.Currency;

            if (!string.IsNullOrWhiteSpace(dto.Visibility))
            {
                var vis = dto.Visibility.ToLower();
                if (vis != "public" && vis != "private")
                    return BadRequest("Visibility must be 'public' or 'private'.");
                journal.Visibility = vis;
            }

            if (dto.ProTips != null)
                journal.ProTips = dto.ProTips;

            if (dto.Vibes != null)
                journal.Vibes = dto.Vibes.Count > 0
                    ? string.Join(",", dto.Vibes.Select(v => v.Trim().ToLower()))
                    : null;

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var newStatus = dto.Status.ToLower();
                if (newStatus != "draft" && newStatus != "published")
                    return BadRequest("Status must be 'draft' or 'published'.");

                // Set PublishedAt when first published
                if (newStatus == "published" && journal.Status != "published")
                    journal.PublishedAt = DateTime.UtcNow;

                journal.Status = newStatus;
            }

            // Replace places if provided
            if (dto.Places != null)
            {
                _dbContext.JournalPlaces.RemoveRange(journal.Places);

                foreach (var p in dto.Places)
                {
                    if (string.IsNullOrWhiteSpace(p.PlaceName))
                        continue;

                    _dbContext.JournalPlaces.Add(new JournalPlace
                    {
                        Id = Guid.NewGuid(),
                        JournalId = journal.Id,
                        PlaceName = p.PlaceName,
                        Category = string.IsNullOrWhiteSpace(p.Category) ? "other" : p.Category.ToLower(),
                        GooglePlaceId = p.GooglePlaceId,
                        Latitude = p.Latitude,
                        Longitude = p.Longitude
                    });
                }
            }

            journal.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Reload places for response
            var updatedPlaces = await _dbContext.JournalPlaces
                .Where(p => p.JournalId == journal.Id)
                .Select(p => new { p.Id, p.PlaceName, p.Category, p.GooglePlaceId, p.Latitude, p.Longitude })
                .ToListAsync();

            return Ok(new
            {
                journal.Id,
                journal.TripId,
                journal.Title,
                journal.Body,
                journal.Destination,
                journal.StartDate,
                journal.EndDate,
                journal.TravelersCount,
                journal.Budget,
                journal.Currency,
                journal.Visibility,
                journal.ProTips,
                journal.Vibes,
                journal.Status,
                journal.LikesCount,
                journal.CommentsCount,
                journal.CreatedAt,
                journal.UpdatedAt,
                journal.PublishedAt,
                Places = updatedPlaces
            });
        }

        /// <summary>
        /// Delete a journal. Only the author can delete.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = await _dbContext.Journals
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId.Value);

            if (journal == null)
                return NotFound("Journal not found.");

            _dbContext.Journals.Remove(journal);
            await _dbContext.SaveChangesAsync();

            // Its photos too (uploaded under journals/{id}/). The story is gone
            // either way; a failure here only leaves files behind, so log it.
            try
            {
                await _cloudinaryService.DeleteFolderAsync($"journals/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete Cloudinary photos for journal {JournalId}.", id);
            }

            return Ok(new { message = "Journal deleted." });
        }

        /// <summary>
        /// Upload photos for a journal. Only the author can upload. Max 10 files, 5MB each.
        /// Photos are stored in Cloudinary under journals/{journalId}/.
        /// </summary>
        [HttpPost("{id}/photos")]
        [Authorize]
        public async Task<IActionResult> UploadPhotos(Guid id, [FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("At least one file is required.");

            if (files.Count > 10)
                return BadRequest("Maximum 10 files allowed per upload.");

            const long maxFileSize = 5 * 1024 * 1024; // 5MB
            foreach (var file in files)
            {
                if (file.Length > maxFileSize)
                    return BadRequest($"File '{file.FileName}' exceeds the 5MB limit.");

                if (!file.ContentType.StartsWith("image/"))
                    return BadRequest($"File '{file.FileName}' is not an image.");
            }

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = await _dbContext.Journals
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId.Value);

            if (journal == null)
                return NotFound("Journal not found.");

            var currentMaxOrder = await _dbContext.JournalPhotos
                .Where(p => p.JournalId == id)
                .MaxAsync(p => (int?)p.Order) ?? 0;

            var uploadedPhotos = new List<object>();

            foreach (var file in files)
            {
                var url = await _cloudinaryService.UploadImageAsync(file, $"journals/{id}");

                currentMaxOrder++;

                var photo = new JournalPhoto
                {
                    Id = Guid.NewGuid(),
                    JournalId = id,
                    Url = url,
                    Order = currentMaxOrder,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.JournalPhotos.Add(photo);
                uploadedPhotos.Add(new
                {
                    photo.Id,
                    photo.Url,
                    photo.Caption,
                    photo.Order,
                    photo.CreatedAt
                });
            }

            await _dbContext.SaveChangesAsync();

            return Ok(uploadedPhotos);
        }

        /// <summary>
        /// Get all photos for a journal.
        /// </summary>
        [HttpGet("{id}/photos")]
        public async Task<IActionResult> GetPhotos(Guid id)
        {
            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null)
                return NotFound("Journal not found.");

            // If not public+published, only the author can view photos
            if (journal.Status != "published" || journal.Visibility != "public")
            {
                var userId = await GetCurrentUserId();
                if (userId == null || journal.UserId != userId.Value)
                    return NotFound("Journal not found.");
            }

            var photos = await _dbContext.JournalPhotos
                .Where(p => p.JournalId == id)
                .OrderBy(p => p.Order)
                .Select(p => new
                {
                    p.Id,
                    p.Url,
                    p.Caption,
                    p.Order,
                    p.CreatedAt
                })
                .ToListAsync();

            return Ok(photos);
        }

        /// <summary>
        /// Delete a photo from a journal. Only the author can delete.
        /// </summary>
        [HttpDelete("{journalId}/photos/{photoId}")]
        [Authorize]
        public async Task<IActionResult> DeletePhoto(Guid journalId, Guid photoId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = await _dbContext.Journals
                .FirstOrDefaultAsync(j => j.Id == journalId && j.UserId == userId.Value);

            if (journal == null)
                return NotFound("Journal not found.");

            var photo = await _dbContext.JournalPhotos
                .FirstOrDefaultAsync(p => p.Id == photoId && p.JournalId == journalId);

            if (photo == null)
                return NotFound("Photo not found.");

            _dbContext.JournalPhotos.Remove(photo);
            await _dbContext.SaveChangesAsync();

            try
            {
                await _cloudinaryService.DeleteImageByUrlAsync(photo.Url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete Cloudinary photo {PhotoId} of journal {JournalId}.", photoId, journalId);
            }

            return Ok(new { message = "Photo deleted." });
        }

        /// <summary>
        /// Set the order of a journal's photos (the first is the cover). Only the
        /// author can reorder. Body: the photo ids in the new order; photos not
        /// listed keep their place after them.
        /// </summary>
        [HttpPut("{id}/photos/order")]
        [Authorize]
        public async Task<IActionResult> ReorderPhotos(Guid id, [FromBody] List<Guid> photoIds)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var journal = await _dbContext.Journals
                .Include(j => j.Photos)
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId.Value);
            if (journal == null)
                return NotFound("Journal not found.");

            var order = (photoIds ?? new List<Guid>()).Distinct().ToList();
            var position = 0;
            foreach (var photoId in order)
            {
                var photo = journal.Photos.FirstOrDefault(p => p.Id == photoId);
                if (photo != null) photo.Order = position++;
            }
            foreach (var photo in journal.Photos.Where(p => !order.Contains(p.Id)).OrderBy(p => p.Order))
                photo.Order = position++;

            journal.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return Ok(journal.Photos.OrderBy(p => p.Order).Select(p => new { p.Id, p.Url, p.Order }));
        }

        // ─── Community safety: report, "not interested" ─────────────────────────

        private static readonly HashSet<string> ReportReasons = new(StringComparer.OrdinalIgnoreCase)
        {
            "spam", "inappropriate", "harassment", "false_info", "other",
        };

        public class ReportJournalDto
        {
            public string Reason { get; set; } = "other";
            public string? Details { get; set; }
        }

        /// <summary>
        /// Report a story. One report per person per story (reporting again
        /// updates it). The story is also hidden from the reporter's feed.
        /// </summary>
        [HttpPost("{id}/report")]
        [Authorize]
        public async Task<IActionResult> Report(Guid id, [FromBody] ReportJournalDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();
            var reason = (dto?.Reason ?? "").Trim().ToLowerInvariant();
            if (!ReportReasons.Contains(reason))
                return BadRequest("Pick a reason: spam, inappropriate, harassment, false_info or other.");

            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null)
                return NotFound("Journal not found.");
            if (journal.UserId == userId.Value)
                return BadRequest("You can't report your own story.");

            var details = string.IsNullOrWhiteSpace(dto?.Details) ? null : dto.Details.Trim();
            if (details?.Length > 500) details = details[..500];

            var report = await _dbContext.JournalReports
                .FirstOrDefaultAsync(r => r.JournalId == id && r.ReporterId == userId.Value);
            if (report == null)
            {
                _dbContext.JournalReports.Add(new JournalReport
                {
                    Id = Guid.NewGuid(),
                    JournalId = id,
                    ReporterId = userId.Value,
                    Reason = reason,
                    Details = details,
                });
            }
            else
            {
                report.Reason = reason;
                report.Details = details;
                report.Status = "open";
                report.CreatedAt = DateTime.UtcNow;
            }
            await HideFor(userId.Value, id);
            await _dbContext.SaveChangesAsync();

            var open = await _dbContext.JournalReports.CountAsync(r => r.JournalId == id && r.Status == "open");
            _logger.LogWarning("Story {JournalId} reported ({Reason}); {Open} open report(s) on it.", id, reason, open);

            return Ok(new { message = "Thanks, we'll take a look." });
        }

        /// <summary>"Not interested": hide a story from your own feed.</summary>
        [HttpPost("{id}/hide")]
        [Authorize]
        public async Task<IActionResult> Hide(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();
            if (!await _dbContext.Journals.AnyAsync(j => j.Id == id))
                return NotFound("Journal not found.");
            await HideFor(userId.Value, id);
            await _dbContext.SaveChangesAsync();
            return Ok(new { hidden = true });
        }

        /// <summary>Undo "Not interested".</summary>
        [HttpDelete("{id}/hide")]
        [Authorize]
        public async Task<IActionResult> Unhide(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();
            _dbContext.HiddenJournals.RemoveRange(
                _dbContext.HiddenJournals.Where(h => h.UserId == userId.Value && h.JournalId == id));
            await _dbContext.SaveChangesAsync();
            return Ok(new { hidden = false });
        }

        private async Task HideFor(Guid userId, Guid journalId)
        {
            if (!await _dbContext.HiddenJournals.AnyAsync(h => h.UserId == userId && h.JournalId == journalId))
                _dbContext.HiddenJournals.Add(new HiddenJournal { Id = Guid.NewGuid(), UserId = userId, JournalId = journalId });
        }

        /// <summary>People this user blocked, and stories they hid or reported (empty when signed out).</summary>
        private async Task<(List<Guid> Blocked, List<Guid> Hidden)> ExclusionsFor(Guid? userId)
        {
            if (userId == null)
                return (new List<Guid>(), new List<Guid>());
            var blocked = await _dbContext.UserBlocks
                .Where(b => b.BlockerId == userId.Value)
                .Select(b => b.BlockedId)
                .ToListAsync();
            var hidden = await _dbContext.HiddenJournals
                .Where(h => h.UserId == userId.Value)
                .Select(h => h.JournalId)
                .ToListAsync();
            return (blocked, hidden);
        }

        private async Task<Guid?> GetCurrentUserId()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return user?.Id;
        }

        // ─── Comments ────────────────────────────────────────────────────────────

        /// <summary>
        /// Get all comments for a journal. Public journals are accessible to all.
        /// </summary>
        [HttpGet("{id}/comments")]
        public async Task<IActionResult> GetComments(Guid id)
        {
            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null)
                return NotFound("Journal not found.");

            if (journal.Status != "published" || journal.Visibility != "public")
            {
                var requesterId = await GetCurrentUserId();
                if (requesterId == null || journal.UserId != requesterId.Value)
                    return NotFound("Journal not found.");
            }

            var (blockedUsers, _) = await ExclusionsFor(await GetCurrentUserId());
            var comments = await _dbContext.JournalComments
                .Include(c => c.User)
                .Where(c => c.JournalId == id && !blockedUsers.Contains(c.UserId))
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Body,
                    c.CreatedAt,
                    c.UpdatedAt,
                    Author = new { c.User.Id, c.User.Name, c.User.PhotoUrl }
                })
                .ToListAsync();

            return Ok(comments);
        }

        /// <summary>
        /// Add a comment to a journal. Auth required.
        /// </summary>
        [HttpPost("{id}/comments")]
        [Authorize]
        public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Body))
                return BadRequest("Comment body is required.");

            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null || journal.Status != "published" || journal.Visibility != "public")
                return NotFound("Journal not found.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var comment = new JournalComment
            {
                Id = Guid.NewGuid(),
                JournalId = id,
                UserId = userId.Value,
                Body = dto.Body.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.JournalComments.Add(comment);
            journal.CommentsCount++;
            await _dbContext.SaveChangesAsync();

            var user = await _dbContext.Users.FindAsync(userId.Value);

            return Ok(new
            {
                comment.Id,
                comment.Body,
                comment.CreatedAt,
                comment.UpdatedAt,
                Author = new { user.Id, user.Name, user.PhotoUrl }
            });
        }

        /// <summary>
        /// Edit your own comment. Auth required.
        /// </summary>
        [HttpPut("{id}/comments/{commentId}")]
        [Authorize]
        public async Task<IActionResult> UpdateComment(Guid id, Guid commentId, [FromBody] AddCommentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Body))
                return BadRequest("Comment body is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var comment = await _dbContext.JournalComments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.JournalId == id && c.UserId == userId.Value);

            if (comment == null)
                return NotFound("Comment not found.");

            comment.Body = dto.Body.Trim();
            comment.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                comment.Id,
                comment.Body,
                comment.CreatedAt,
                comment.UpdatedAt
            });
        }

        /// <summary>
        /// Delete your own comment. Auth required.
        /// </summary>
        [HttpDelete("{id}/comments/{commentId}")]
        [Authorize]
        public async Task<IActionResult> DeleteComment(Guid id, Guid commentId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var comment = await _dbContext.JournalComments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.JournalId == id && c.UserId == userId.Value);

            if (comment == null)
                return NotFound("Comment not found.");

            var journal = await _dbContext.Journals.FindAsync(id);
            if (journal != null && journal.CommentsCount > 0)
                journal.CommentsCount--;

            _dbContext.JournalComments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Comment deleted." });
        }

        // ─── Likes ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Toggle like on a journal. Returns liked: true/false. Auth required.
        /// </summary>
        [HttpPost("{id}/like")]
        [Authorize]
        public async Task<IActionResult> ToggleLike(Guid id)
        {
            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null || journal.Status != "published" || journal.Visibility != "public")
                return NotFound("Journal not found.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var existing = await _dbContext.JournalLikes
                .FirstOrDefaultAsync(l => l.JournalId == id && l.UserId == userId.Value);

            if (existing != null)
            {
                // Unlike
                _dbContext.JournalLikes.Remove(existing);
                if (journal.LikesCount > 0)
                    journal.LikesCount--;

                await _dbContext.SaveChangesAsync();
                return Ok(new { liked = false, likesCount = journal.LikesCount });
            }
            else
            {
                // Like
                _dbContext.JournalLikes.Add(new JournalLike
                {
                    Id = Guid.NewGuid(),
                    JournalId = id,
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow
                });
                journal.LikesCount++;

                await _dbContext.SaveChangesAsync();
                return Ok(new { liked = true, likesCount = journal.LikesCount });
            }
        }

        /// <summary>
        /// Check if the current user has liked a journal. Auth required.
        /// </summary>
        [HttpGet("{id}/like")]
        [Authorize]
        public async Task<IActionResult> GetLikeStatus(Guid id)
        {
            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == id);
            if (journal == null)
                return NotFound("Journal not found.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var liked = await _dbContext.JournalLikes
                .AnyAsync(l => l.JournalId == id && l.UserId == userId.Value);

            return Ok(new { liked, likesCount = journal.LikesCount });
        }
    }
}
