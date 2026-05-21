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
    [Authorize]
    public class FavouritesController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public FavouritesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

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
                PhotoUrl = dto.PhotoUrl,
                Category = dto.Category,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Favourites.Add(favourite);
            await _dbContext.SaveChangesAsync();

            return Ok(new { favourite.Id, favourite.PlaceId, favourite.PlaceName });
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
