using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FavouritesController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IGooglePlacesService _places;

        // Older favourites have no city yet; each list request fills in up to
        // this many (one Google lookup each, once per favourite, ever).
        private const int CityBackfillPerRequest = 30;

        public FavouritesController(AppDbContext dbContext, IGooglePlacesService places)
        {
            _dbContext = dbContext;
            _places = places;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            await FillMissingCities(userId.Value, HttpContext.RequestAborted);

            var favourites = await _dbContext.Favourites
                .Where(f => f.UserId == userId.Value)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new
                {
                    f.Id,
                    f.PlaceId,
                    f.PlaceName,
                    f.Vicinity,
                    f.Rating,
                    f.UserRatingsTotal,
                    f.PhotoUrl,
                    f.Category,
                    f.City,
                    f.CreatedAt
                })
                .ToListAsync();

            return Ok(favourites);
        }

        [HttpPost]
        public async Task<IActionResult> Add([FromBody] AddFavouriteDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.PlaceId))
                return BadRequest("PlaceId is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var exists = await _dbContext.Favourites
                .AnyAsync(f => f.UserId == userId.Value && f.PlaceId == dto.PlaceId);

            if (exists)
                return Conflict("Already in favourites.");

            var favourite = new Favourite
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                PlaceId = dto.PlaceId,
                PlaceName = dto.PlaceName,
                Vicinity = dto.Vicinity,
                Rating = dto.Rating,
                UserRatingsTotal = dto.UserRatingsTotal,
                PhotoUrl = PlacePhotoLinks.StripKey(dto.PhotoUrl),
                Category = dto.Category,
                // Null if Google couldn't be reached; the next list request retries.
                City = await _places.GetPlaceCity(dto.PlaceId, HttpContext.RequestAborted),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Favourites.Add(favourite);
            await _dbContext.SaveChangesAsync();

            return Ok(new { favourite.Id, favourite.PlaceId, favourite.PlaceName, favourite.City });
        }

        [HttpDelete("{placeId}")]
        public async Task<IActionResult> Remove(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("PlaceId is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var favourite = await _dbContext.Favourites
                .FirstOrDefaultAsync(f => f.UserId == userId.Value && f.PlaceId == placeId);

            if (favourite == null)
                return NotFound("Favourite not found.");

            _dbContext.Favourites.Remove(favourite);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Removed from favourites." });
        }

        [HttpPost("sync")]
        public async Task<IActionResult> Sync([FromBody] SyncFavouritesDto dto)
        {
            if (dto?.Favourites == null || dto.Favourites.Count == 0)
                return BadRequest("No favourites to sync.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var existingPlaceIds = await _dbContext.Favourites
                .Where(f => f.UserId == userId.Value)
                .Select(f => f.PlaceId)
                .ToListAsync();

            var newFavourites = dto.Favourites
                .Where(f => !string.IsNullOrWhiteSpace(f.PlaceId) && !existingPlaceIds.Contains(f.PlaceId))
                .Select(f => new Favourite
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    PlaceId = f.PlaceId,
                    PlaceName = f.PlaceName,
                    Vicinity = f.Vicinity,
                    Rating = f.Rating,
                    UserRatingsTotal = f.UserRatingsTotal,
                    PhotoUrl = f.PhotoUrl,
                    Category = f.Category,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList();

            if (newFavourites.Count > 0)
            {
                _dbContext.Favourites.AddRange(newFavourites);
                await _dbContext.SaveChangesAsync();
            }

            return Ok(new { synced = newFavourites.Count });
        }

        /// <summary>
        /// Look up the city of this user's favourites that don't have one yet
        /// (saved before cities were stored, or when Google was unreachable).
        /// </summary>
        private async Task FillMissingCities(Guid userId, CancellationToken ct)
        {
            var missing = await _dbContext.Favourites
                .Where(f => f.UserId == userId && f.City == null)
                .OrderByDescending(f => f.CreatedAt)
                .Take(CityBackfillPerRequest)
                .ToListAsync(ct);
            if (missing.Count == 0) return;

            // A few lookups at a time; the DbContext is only touched afterwards.
            using var gate = new SemaphoreSlim(4);
            var cities = await Task.WhenAll(missing.Select(async f =>
            {
                await gate.WaitAsync(ct);
                try { return await _places.GetPlaceCity(f.PlaceId, ct); }
                finally { gate.Release(); }
            }));

            var changed = false;
            for (var i = 0; i < missing.Count; i++)
            {
                if (cities[i] == null) continue; // lookup failed: try next time
                missing[i].City = cities[i];
                changed = true;
            }
            if (changed) await _dbContext.SaveChangesAsync(ct);
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
