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
    public class TripsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trips = await _dbContext.Trips
                .Include(t => t.Destinations)
                    .ThenInclude(d => d.Places)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Where(t => t.UserId == userId.Value
                    || t.Members.Any(m => m.UserId == userId.Value))
                .OrderByDescending(t => t.StartDate)
                .ToListAsync();

            var result = trips.Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.StartDate,
                t.EndDate,
                t.CoverPhotoUrl,
                t.TravelersCount,
                t.PrimaryDestination,
                t.TotalBudget,
                t.Currency,
                t.Status,
                t.CreatedAt,
                // Google place id of the trip's first destination, so clients can
                // load its photo directly from /Destinations/place-photo.
                // The primary stop (else the first stop, on older trips).
                PrimaryPlaceId = t.PrimaryDestinationId != null
                    ? t.Destinations.Where(d => d.Id == t.PrimaryDestinationId).Select(d => d.GooglePlaceId).FirstOrDefault()
                    : t.Destinations.OrderBy(d => d.Order).ThenBy(d => d.CreatedAt).Select(d => d.GooglePlaceId).FirstOrDefault(),
                PlaceIds = t.Destinations
                    .SelectMany(d => d.Places)
                    .Select(p => p.PlaceId)
                    .ToList(),
                // Small member summary for list cards (avatars): the total, plus
                // the first few names with the owner first. Full member details
                // stay on GET /api/Trips/{id}.
                MemberCount = t.Members.Count,
                // The signed-in user's role on this trip (owner | member | viewer),
                // so clients can hide edit actions the server would reject.
                MyRole = t.Members.Where(m => m.UserId == userId.Value)
                    .Select(m => m.Role).FirstOrDefault()
                    ?? (t.UserId == userId.Value ? "owner" : null),
                Members = t.Members
                    .OrderByDescending(m => m.Role == "owner")
                    .ThenBy(m => m.JoinedAt)
                    .Take(3)
                    .Select(m => new { m.UserId, Name = m.User?.Name, PhotoUrl = m.User?.PhotoUrl })
                    .ToList()
            });

            return Ok(result);
        }

        [HttpGet("by-destination/{googlePlaceId}")]
        public async Task<IActionResult> GetByDestination(string googlePlaceId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trips = await _dbContext.Trips
                .Include(t => t.Destinations)
                .Where(t => t.UserId == userId.Value
                    && t.Destinations.Any(d => d.GooglePlaceId == googlePlaceId))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var trip = trips.FirstOrDefault(t => t.Status != "completed");

            if (trip == null)
                return NotFound();

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.StartDate,
                trip.EndDate,
                trip.Status,
                trip.PrimaryDestination,
                trip.TravelersCount
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                    .ThenInclude(d => d.Places)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(t => t.Id == id
                    && (t.UserId == userId.Value || t.Members.Any(m => m.UserId == userId.Value)));

            if (trip == null)
                return NotFound("Trip not found.");

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
                PrimaryDestinationId = EffectivePrimaryId(trip),
                MyRole = trip.Members.Where(m => m.UserId == userId.Value)
                    .Select(m => m.Role).FirstOrDefault()
                    ?? (trip.UserId == userId.Value ? "owner" : null),
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
                    d.EndDate,
                    Places = d.Places.Select(p => new
                    {
                        p.Id,
                        p.PlaceId,
                        p.PlaceName,
                        p.Vicinity,
                        p.Rating,
                        p.UserRatingsTotal,
                        p.PhotoUrl,
                        p.Category,
                        p.Latitude,
                        p.Longitude,
                        p.Notes,
                        p.CreatedAt
                    })
                }),
                Members = trip.Members.Select(m => new
                {
                    m.UserId,
                    Name = m.User.Name,
                    Email = m.User.Email,
                    PhotoUrl = m.User.PhotoUrl,
                    m.Role,
                    m.JoinedAt
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTripDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Name))
                return BadRequest("Trip name is required.");

            if (string.IsNullOrWhiteSpace(dto.Destination?.Name))
                return BadRequest("Destination name is required.");

            if (string.IsNullOrWhiteSpace(dto.Destination?.GooglePlaceId))
                return BadRequest("Destination GooglePlaceId is required.");

            if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate < dto.StartDate)
                return BadRequest("End date must be after start date.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = new Trip
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = dto.Name,
                Description = dto.Description,
                StartDate = ToUtc(dto.StartDate),
                EndDate = ToUtc(dto.EndDate),
                CoverPhotoUrl = PlacePhotoLinks.StripKey(dto.CoverPhotoUrl),
                TravelersCount = dto.TravelersCount,
                PrimaryDestination = dto.PrimaryDestination,
                TotalBudget = dto.TotalBudget,
                Currency = dto.Currency,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Trips.Add(trip);

            // Auto-add creator as trip owner
            var tripMember = new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                UserId = userId.Value,
                Role = "owner",
                JoinedAt = DateTime.UtcNow
            };
            _dbContext.TripMembers.Add(tripMember);

            TripDestination? destination = null;
            if (!string.IsNullOrWhiteSpace(dto.Destination.Name))
            {
                destination = new TripDestination
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    GooglePlaceId = dto.Destination.GooglePlaceId,
                    Name = dto.Destination.Name,
                    Latitude = dto.Destination.Latitude,
                    Longitude = dto.Destination.Longitude,
                    PhotoUrl = PlacePhotoLinks.StripKey(dto.Destination.PhotoUrl),
                    Order = dto.Destination.Order,
                    StartDate = ToUtc(dto.Destination.StartDate),
                    EndDate = ToUtc(dto.Destination.EndDate),
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.TripDestinations.Add(destination);
                trip.PrimaryDestinationId = destination.Id;
            }

            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = trip.Id }, new
            {
                trip.Id,
                trip.Name,
                trip.StartDate,
                trip.EndDate,
                trip.Status,
                Destination = destination == null ? null : new
                {
                    destination.Id,
                    destination.GooglePlaceId,
                    destination.Name,
                    destination.Latitude,
                    destination.Longitude
                }
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTripDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            if (!string.IsNullOrWhiteSpace(dto.Name))
                trip.Name = dto.Name;

            if (dto.Description != null)
                trip.Description = dto.Description;

            if (dto.StartDate.HasValue)
                trip.StartDate = ToUtc(dto.StartDate);

            if (dto.EndDate.HasValue)
                trip.EndDate = ToUtc(dto.EndDate);

            if (trip.EndDate.HasValue && trip.StartDate.HasValue && trip.EndDate < trip.StartDate)
                return BadRequest("End date must be after start date.");

            if (dto.CoverPhotoUrl != null)
                trip.CoverPhotoUrl = PlacePhotoLinks.StripKey(dto.CoverPhotoUrl);

            if (dto.TravelersCount.HasValue)
                trip.TravelersCount = dto.TravelersCount.Value;

            if (dto.PrimaryDestination != null)
                trip.PrimaryDestination = dto.PrimaryDestination;

            if (dto.TotalBudget.HasValue)
                trip.TotalBudget = dto.TotalBudget.Value;

            if (dto.Currency != null)
                trip.Currency = dto.Currency;

            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Status, trip.TravelersCount });
        }

        [HttpPost("{id}/destinations")]
        public async Task<IActionResult> AddDestination(Guid id, [FromBody] CreateTripDestinationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Name))
                return BadRequest("Destination name is required.");

            if (string.IsNullOrWhiteSpace(dto.GooglePlaceId))
                return BadRequest("GooglePlaceId is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var alreadyExists = await _dbContext.TripDestinations
                .AnyAsync(d => d.TripId == id && d.GooglePlaceId == dto.GooglePlaceId);

            if (alreadyExists)
                return Conflict("This destination is already added to the trip.");

            var destination = new TripDestination
            {
                Id = Guid.NewGuid(),
                TripId = id,
                GooglePlaceId = dto.GooglePlaceId,
                Name = dto.Name,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                PhotoUrl = PlacePhotoLinks.StripKey(dto.PhotoUrl),
                Order = dto.Order,
                StartDate = ToUtc(dto.StartDate),
                EndDate = ToUtc(dto.EndDate),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TripDestinations.Add(destination);
            await _dbContext.SaveChangesAsync();

            // Keep the trip's dates in step with its stops.
            var trip = await _dbContext.Trips.Include(t => t.Destinations).FirstOrDefaultAsync(t => t.Id == id);
            if (trip != null)
            {
                var datesChanged = RecalculateTripDates(trip, keepWhenUndated: true);
                var orderChanged = OrderStopsByDate(trip);
                if (datesChanged || orderChanged)
                    await _dbContext.SaveChangesAsync();
            }

            return Ok(new { destination.Id, destination.GooglePlaceId, destination.Name, destination.Latitude, destination.Longitude });
        }

        [HttpDelete("{id}/destinations/{destinationId}")]
        public async Task<IActionResult> DeleteDestination(Guid id, Guid destinationId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .FirstOrDefaultAsync(t => t.Id == id);
            var destination = trip?.Destinations.FirstOrDefault(d => d.Id == destinationId);

            if (destination == null)
                return NotFound("Destination not found.");
            if (destination.Id == EffectivePrimaryId(trip))
                return BadRequest("This is the trip's primary destination. Make another stop primary first.");

            // The stop's itinerary entries go with it (the FK is NO ACTION, so
            // leaving them would block the delete).
            var itineraryItems = await _dbContext.ItineraryItems
                .Where(i => i.DestinationId == destination.Id)
                .ToListAsync();
            _dbContext.ItineraryItems.RemoveRange(itineraryItems);

            _dbContext.TripDestinations.Remove(destination);
            trip.Destinations.Remove(destination);
            // Without this the trip kept the removed stop's dates (e.g. still
            // starting on the 6th after that stop was gone).
            RecalculateTripDates(trip, keepWhenUndated: true);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Destination removed from trip.", trip.StartDate, trip.EndDate });
        }

        [HttpPut("{id}/destinations/dates")]
        public async Task<IActionResult> UpdateDestinationDates(Guid id, [FromBody] List<UpdateDestinationDateDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return BadRequest("At least one destination date update is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            // Validate all destination IDs belong to this trip
            var tripDestinationIds = trip.Destinations.Select(d => d.Id).ToHashSet();
            foreach (var dto in dtos)
            {
                if (!tripDestinationIds.Contains(dto.DestinationId))
                    return BadRequest($"Destination {dto.DestinationId} does not belong to this trip.");
            }

            // Update each destination's dates
            foreach (var dto in dtos)
            {
                var destination = trip.Destinations.First(d => d.Id == dto.DestinationId);
                destination.StartDate = ToUtc(dto.StartDate);
                destination.EndDate = ToUtc(dto.EndDate);
            }

            // Recalculate trip dates from all destinations, and keep the stops
            // in date order (the primary destination stays the same).
            RecalculateTripDates(trip, keepWhenUndated: false);
            OrderStopsByDate(trip);
            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.StartDate,
                trip.EndDate,
                trip.Status,
                Destinations = trip.Destinations.Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.StartDate,
                    d.EndDate
                })
            });
        }

        /// <summary>
        /// Put a trip's stops in a new order: the body lists every stop id, first
        /// to last. The primary destination does not change.
        /// </summary>
        [HttpPut("{id}/destinations/order")]
        public async Task<IActionResult> ReorderDestinations(Guid id, [FromBody] List<Guid> destinationIds)
        {
            if (destinationIds == null || destinationIds.Count == 0)
                return BadRequest("Give the stops in their new order.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (trip == null)
                return NotFound("Trip not found.");

            var current = trip.Destinations.Select(d => d.Id).ToHashSet();
            if (destinationIds.Count != current.Count || !destinationIds.All(current.Contains) || destinationIds.Distinct().Count() != current.Count)
                return BadRequest("The new order must list each of the trip's stops exactly once.");

            trip.PrimaryDestinationId ??= EffectivePrimaryId(trip); // pin it before the order changes
            for (var i = 0; i < destinationIds.Count; i++)
                trip.Destinations.First(d => d.Id == destinationIds[i]).Order = i;
            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return Ok(new
            {
                trip.Id,
                trip.PrimaryDestination,
                Destinations = trip.Destinations.OrderBy(d => d.Order).Select(d => new { d.Id, d.Name, d.Order, d.StartDate, d.EndDate })
            });
        }

        /// <summary>
        /// Make a stop the trip's primary destination: its name on cards, its
        /// cover photo. The route order does not change.
        /// </summary>
        [HttpPut("{id}/primary-destination")]
        public async Task<IActionResult> SetPrimaryDestination(Guid id, [FromBody] SetPrimaryDestinationDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .FirstOrDefaultAsync(t => t.Id == id);
            var stop = trip?.Destinations.FirstOrDefault(d => d.Id == dto?.DestinationId);
            if (stop == null)
                return NotFound("Destination not found.");

            trip.PrimaryDestinationId = stop.Id;
            trip.PrimaryDestination = stop.Name;
            if (!string.IsNullOrEmpty(stop.PhotoUrl))
                trip.CoverPhotoUrl = stop.PhotoUrl;
            trip.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.Id, PrimaryDestinationId = stop.Id, trip.PrimaryDestination });
        }

        /// <summary>The primary stop's id: the chosen one, else the first stop.</summary>
        private static Guid? EffectivePrimaryId(Trip trip) =>
            trip.PrimaryDestinationId
            ?? trip.Destinations.OrderBy(d => d.Order).ThenBy(d => d.CreatedAt).Select(d => (Guid?)d.Id).FirstOrDefault();

        /// <summary>
        /// Keep stops in date order: dated stops by start date, undated ones
        /// after them in their current order. Pins the primary destination first
        /// (older trips take it from the order), so reordering never changes it.
        /// Returns true when the order changed.
        /// </summary>
        private static bool OrderStopsByDate(Trip trip)
        {
            if (trip.Destinations.Count < 2)
                return false;
            trip.PrimaryDestinationId ??= EffectivePrimaryId(trip);

            var sorted = trip.Destinations
                .OrderBy(d => d.StartDate.HasValue ? 0 : 1)
                .ThenBy(d => d.StartDate)
                .ThenBy(d => d.Order)
                .ThenBy(d => d.CreatedAt)
                .ToList();
            var changed = false;
            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Order != i)
                {
                    sorted[i].Order = i;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// A trip's dates follow its stops: earliest stop start to latest stop
        /// end. With no dated stop, keepWhenUndated leaves the trip's own dates
        /// alone (trips created with dates on the trip only); otherwise they are
        /// cleared. Returns true when the dates changed.
        /// </summary>
        private static bool RecalculateTripDates(Trip trip, bool keepWhenUndated)
        {
            var starts = trip.Destinations.Where(d => d.StartDate.HasValue).Select(d => d.StartDate!.Value).ToList();
            var ends = trip.Destinations.Where(d => d.EndDate.HasValue).Select(d => d.EndDate!.Value).ToList();
            if (starts.Count == 0 && ends.Count == 0 && keepWhenUndated)
                return false;

            DateTime? newStart = starts.Count > 0 ? starts.Min() : null;
            DateTime? newEnd = ends.Count > 0 ? ends.Max() : null;
            if (newStart == trip.StartDate && newEnd == trip.EndDate)
                return false;

            trip.StartDate = newStart;
            trip.EndDate = newEnd;
            trip.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        [HttpPut("{id}/budget")]
        public async Task<IActionResult> SetBudget(Guid id, [FromBody] SetTripBudgetDto dto)
        {
            if (dto.TotalBudget <= 0)
                return BadRequest("Budget must be greater than zero.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            trip.TotalBudget = dto.TotalBudget;
            trip.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "₹" : dto.Currency;
            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.TotalBudget, trip.Currency });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            _dbContext.Trips.Remove(trip);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Trip deleted." });
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

        private static DateTime? ToUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
    }

    public class SetPrimaryDestinationDto
    {
        public Guid DestinationId { get; set; }
    }
}
