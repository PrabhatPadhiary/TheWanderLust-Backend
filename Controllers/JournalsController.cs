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

        public JournalsController(AppDbContext dbContext, CloudinaryService cloudinaryService)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
        }

        /// <summary>
        /// Get all public published journals (feed). No auth required.
        /// </summary>
        [HttpGet("feed")]
        public async Task<IActionResult> GetFeed()
        {
            var journals = await _dbContext.Journals
                .Include(j => j.User)
                .Include(j => j.Places)
                .Where(j => j.Status == "published" && j.Visibility == "public")
                .OrderByDescending(j => j.PublishedAt)
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
                    j.LikesCount,
                    j.CommentsCount,
                    j.PublishedAt,
                    j.CreatedAt,
                    Author = new { j.User.Id, j.User.Name },
                    Places = j.Places.Select(p => new
                    {
                        p.Id,
                        p.PlaceName,
                        p.Category,
                        p.GooglePlaceId
                    }),
                    Photos = _dbContext.JournalPhotos
                        .Where(ph => ph.JournalId == j.Id)
                        .OrderBy(ph => ph.Order)
                        .Select(ph => new
                        {
                            ph.Id,
                            ph.Url,
                            ph.Order
                        }).ToList()
                })
                .ToListAsync();

            return Ok(journals);
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
                .Where(j => j.UserId == userId.Value)
                .OrderByDescending(j => j.UpdatedAt)
                .Select(j => new
                {
                    j.Id,
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
                        p.GooglePlaceId
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
                .FirstOrDefaultAsync(j => j.Id == id);

            if (journal == null)
                return NotFound("Journal not found.");

            // If it's not public+published, only the author can view
            if (journal.Status != "published" || journal.Visibility != "public")
            {
                var userId = await GetCurrentUserId();
                if (userId == null || journal.UserId != userId.Value)
                    return NotFound("Journal not found.");
            }

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
                journal.Status,
                journal.LikesCount,
                journal.CommentsCount,
                journal.CreatedAt,
                journal.UpdatedAt,
                journal.PublishedAt,
                Author = new { journal.User.Id, journal.User.Name },
                Places = journal.Places.Select(p => new
                {
                    p.Id,
                    p.PlaceName,
                    p.Category,
                    p.GooglePlaceId
                })
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
                        GooglePlaceId = p.GooglePlaceId
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
                        GooglePlaceId = p.GooglePlaceId
                    });
                }
            }

            journal.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Reload places for response
            var updatedPlaces = await _dbContext.JournalPlaces
                .Where(p => p.JournalId == journal.Id)
                .Select(p => new { p.Id, p.PlaceName, p.Category, p.GooglePlaceId })
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

            return Ok(new { message = "Photo deleted." });
        }

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
