using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// Polls: "Dinner on Day 2: where?" for everyone on the trip.
    ///
    /// Owners and members start polls and vote (one vote each, changeable while
    /// it's open); viewers see them. The creator or the owner can end a poll
    /// early or delete it. A new poll and a closed poll are pushed to the others;
    /// single votes only update live (TripLiveUpdatesFilter).
    /// </summary>
    [ApiController]
    [Route("api/trips/{tripId}/polls")]
    [Authorize]
    public class TripPollsController : ControllerBase
    {
        private static readonly HashSet<string> Slots = new() { "morning", "afternoon", "evening", "anytime" };

        private readonly AppDbContext _dbContext;
        private readonly NotificationSender _sender;
        private readonly ILogger<TripPollsController> _logger;

        public TripPollsController(AppDbContext dbContext, NotificationSender sender, ILogger<TripPollsController> logger)
        {
            _dbContext = dbContext;
            _sender = sender;
            _logger = logger;
        }

        public record PollOptionDto(string? Text, Guid? TripPlaceId);
        public record CreatePollDto(string Question, List<PollOptionDto> Options, DateTime? ScheduledDate, string? Slot, DateTime? ClosesAt);
        public record VoteDto(Guid OptionId);
        public record LinkItineraryDto(Guid ItineraryItemId);

        [HttpGet]
        public async Task<IActionResult> GetAll(Guid tripId)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");

            var polls = await LoadPolls(_dbContext.TripPolls.Where(p => p.TripId == tripId));
            var now = DateTime.UtcNow;
            return Ok(polls
                .OrderBy(p => p.IsClosed(now))
                .ThenByDescending(p => p.CreatedAt)
                .Select(p => Shape(p, user.Id, role, now)));
        }

        [HttpPost]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] CreatePollDto dto)
        {
            var question = dto?.Question?.Trim();
            if (string.IsNullOrEmpty(question))
                return BadRequest("Ask a question.");
            if (question.Length > TripPoll.QuestionMaxLength)
                return BadRequest($"Keep the question under {TripPoll.QuestionMaxLength} characters.");

            var slot = string.IsNullOrWhiteSpace(dto!.Slot) ? null : dto.Slot.Trim().ToLowerInvariant();
            if (slot != null && !Slots.Contains(slot))
                return BadRequest("Slot must be morning, afternoon, evening or anytime.");

            var now = DateTime.UtcNow;
            DateTime? closesAt = dto.ClosesAt == null ? null : ToUtc(dto.ClosesAt.Value);
            if (closesAt != null && (closesAt <= now.AddMinutes(1) || closesAt > now.AddDays(60)))
                return BadRequest("Pick a closing time in the next 60 days.");

            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            // Options: saved places on this trip, or plain text.
            var raw = dto.Options ?? new List<PollOptionDto>();
            var placeIds = raw.Where(o => o?.TripPlaceId != null).Select(o => o!.TripPlaceId!.Value).Distinct().ToList();
            var places = placeIds.Count == 0
                ? new Dictionary<Guid, TripPlace>()
                : await _dbContext.TripPlaces
                    .Where(p => placeIds.Contains(p.Id) && p.TripDestination.TripId == tripId)
                    .ToDictionaryAsync(p => p.Id);

            var options = new List<TripPollOption>();
            foreach (var o in raw)
            {
                if (o == null) continue;
                TripPlace? place = null;
                if (o.TripPlaceId != null && !places.TryGetValue(o.TripPlaceId.Value, out place))
                    return BadRequest("One of those places isn't on this trip any more.");
                var text = (o.Text?.Trim() is { Length: > 0 } t ? t : place?.PlaceName)?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue; // blank rows are ignored
                if (text.Length > TripPollOption.TextMaxLength)
                    return BadRequest($"Keep each option under {TripPollOption.TextMaxLength} characters.");
                if (options.Any(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest($"\"{text}\" is in there twice.");
                options.Add(new TripPollOption
                {
                    Id = Guid.NewGuid(),
                    Text = text,
                    TripPlaceId = place?.Id,
                    PhotoUrl = place?.PhotoUrl,
                    Category = place?.Category,
                    Order = options.Count,
                });
            }
            if (options.Count < TripPoll.MinOptions)
                return BadRequest($"Add at least {TripPoll.MinOptions} options.");
            if (options.Count > TripPoll.MaxOptions)
                return BadRequest($"A poll can have up to {TripPoll.MaxOptions} options.");

            var poll = new TripPoll
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                CreatedByUserId = user.Id,
                Question = question,
                ScheduledDate = dto.ScheduledDate == null ? null : DateOnlyUtc(dto.ScheduledDate.Value),
                Slot = slot,
                ClosesAt = closesAt,
                CreatedAt = now,
                Options = options,
            };
            _dbContext.TripPolls.Add(poll);
            await _dbContext.SaveChangesAsync();

            await Notify(tripId, user, "asked", question);

            poll.CreatedByUser = user;
            return Ok(Shape(poll, user.Id, role, now));
        }

        /// <summary>Vote, or change your vote.</summary>
        [HttpPut("{id}/vote")]
        public async Task<IActionResult> Vote(Guid tripId, Guid id, [FromBody] VoteDto dto)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var poll = await _dbContext.TripPolls
                .Include(p => p.Options)
                .FirstOrDefaultAsync(p => p.Id == id && p.TripId == tripId);
            if (poll == null)
                return NotFound("Poll not found.");
            var now = DateTime.UtcNow;
            if (poll.IsClosed(now))
                return BadRequest("This poll has closed.");
            if (poll.Options.All(o => o.Id != dto.OptionId))
                return BadRequest("That option isn't in this poll.");

            var vote = await _dbContext.TripPollVotes.FirstOrDefaultAsync(v => v.PollId == id && v.UserId == user.Id);
            if (vote == null)
            {
                _dbContext.TripPollVotes.Add(new TripPollVote
                {
                    Id = Guid.NewGuid(),
                    PollId = id,
                    OptionId = dto.OptionId,
                    UserId = user.Id,
                    CreatedAt = now,
                });
            }
            else
            {
                vote.OptionId = dto.OptionId;
                vote.CreatedAt = now;
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two taps at once: the other one won. Point it at this option.
                _dbContext.ChangeTracker.Clear();
                await _dbContext.TripPollVotes
                    .Where(v => v.PollId == id && v.UserId == user.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(v => v.OptionId, dto.OptionId));
            }

            return Ok(await ShapeOne(id, user.Id, role));
        }

        /// <summary>Take your vote back.</summary>
        [HttpDelete("{id}/vote")]
        public async Task<IActionResult> Unvote(Guid tripId, Guid id)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var poll = await _dbContext.TripPolls.FirstOrDefaultAsync(p => p.Id == id && p.TripId == tripId);
            if (poll == null)
                return NotFound("Poll not found.");
            if (poll.IsClosed(DateTime.UtcNow))
                return BadRequest("This poll has closed.");

            await _dbContext.TripPollVotes
                .Where(v => v.PollId == id && v.UserId == user.Id)
                .ExecuteDeleteAsync();
            return Ok(await ShapeOne(id, user.Id, role));
        }

        /// <summary>End voting now (creator or owner).</summary>
        [HttpPost("{id}/close")]
        public async Task<IActionResult> Close(Guid tripId, Guid id)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");

            var poll = await _dbContext.TripPolls.FirstOrDefaultAsync(p => p.Id == id && p.TripId == tripId);
            if (poll == null)
                return NotFound("Poll not found.");
            if (role != "owner" && poll.CreatedByUserId != user.Id)
                return Forbid();

            var now = DateTime.UtcNow;
            if (!poll.IsClosed(now))
            {
                poll.ClosedAt = now;
                await _dbContext.SaveChangesAsync();

                var shaped = await LoadPolls(_dbContext.TripPolls.Where(p => p.Id == id));
                var winners = Winners(shaped[0]);
                if (shaped[0].Votes.Count > 0)
                {
                    var result = winners.Count == 1
                        ? $"{winners[0].Text} won"
                        : "it's a tie";
                    await Notify(tripId, user, "closed", $"{poll.Question}: {result}");
                }
            }
            return Ok(await ShapeOne(id, user.Id, role));
        }

        /// <summary>Remember the itinerary item made from the winner.</summary>
        [HttpPut("{id}/itinerary")]
        public async Task<IActionResult> LinkItinerary(Guid tripId, Guid id, [FromBody] LinkItineraryDto dto)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var poll = await _dbContext.TripPolls.FirstOrDefaultAsync(p => p.Id == id && p.TripId == tripId);
            if (poll == null)
                return NotFound("Poll not found.");
            if (!await _dbContext.ItineraryItems.AnyAsync(i => i.Id == dto.ItineraryItemId && i.TripId == tripId))
                return BadRequest("That itinerary item isn't on this trip.");

            poll.ItineraryItemId = dto.ItineraryItemId;
            await _dbContext.SaveChangesAsync();
            return Ok(await ShapeOne(id, user.Id, role));
        }

        /// <summary>Delete a poll and its votes (creator or owner).</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid tripId, Guid id)
        {
            var user = await GetCurrentUser();
            if (user == null)
                return Unauthorized();
            var role = await GetRole(tripId, user.Id);
            if (role == null)
                return NotFound("Trip not found.");

            var poll = await _dbContext.TripPolls.FirstOrDefaultAsync(p => p.Id == id && p.TripId == tripId);
            if (poll == null)
                return NotFound("Poll not found.");
            if (role != "owner" && poll.CreatedByUserId != user.Id)
                return Forbid();

            _dbContext.TripPolls.Remove(poll);
            await _dbContext.SaveChangesAsync();
            return Ok(new { message = "Poll deleted." });
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private Task<List<TripPoll>> LoadPolls(IQueryable<TripPoll> query) =>
            query
                .Include(p => p.CreatedByUser)
                .Include(p => p.Options)
                .Include(p => p.Votes).ThenInclude(v => v.User)
                .AsSplitQuery()
                .ToListAsync();

        private async Task<object?> ShapeOne(Guid id, Guid userId, string role)
        {
            var polls = await LoadPolls(_dbContext.TripPolls.Where(p => p.Id == id));
            return polls.Count == 0 ? null : Shape(polls[0], userId, role, DateTime.UtcNow);
        }

        /// <summary>The options with the most votes (more than one on a tie; none without votes).</summary>
        private static List<TripPollOption> Winners(TripPoll poll)
        {
            if (poll.Votes.Count == 0) return new();
            var counts = poll.Options.ToDictionary(o => o.Id, o => poll.Votes.Count(v => v.OptionId == o.Id));
            var top = counts.Values.Max();
            return poll.Options.Where(o => counts[o.Id] == top).OrderBy(o => o.Order).ToList();
        }

        private static object Shape(TripPoll p, Guid userId, string role, DateTime now)
        {
            var closed = p.IsClosed(now);
            var leaders = Winners(p).Select(o => o.Id).ToList();
            return new
            {
                p.Id,
                p.Question,
                p.ScheduledDate,
                p.Slot,
                p.ClosesAt,
                p.ClosedAt,
                IsClosed = closed,
                p.ItineraryItemId,
                p.CreatedAt,
                CreatedBy = p.CreatedByUser == null ? null : new { p.CreatedByUser.Id, p.CreatedByUser.Name, p.CreatedByUser.PhotoUrl },
                TotalVotes = p.Votes.Count,
                MyOptionId = p.Votes.FirstOrDefault(v => v.UserId == userId)?.OptionId,
                // Leading while open; the winner(s) once closed.
                LeadingOptionIds = leaders,
                CanVote = !closed && (role == "owner" || role == "member"),
                CanManage = role == "owner" || p.CreatedByUserId == userId,
                Options = p.Options.OrderBy(o => o.Order).Select(o => new
                {
                    o.Id,
                    o.Text,
                    o.TripPlaceId,
                    o.PhotoUrl,
                    o.Category,
                    Votes = p.Votes.Count(v => v.OptionId == o.Id),
                    Voters = p.Votes
                        .Where(v => v.OptionId == o.Id)
                        .OrderBy(v => v.CreatedAt)
                        .Select(v => new { v.User.Id, v.User.Name, v.User.PhotoUrl }),
                }),
            };
        }

        /// <summary>Push to everyone else on the trip, now. Never fails the request.</summary>
        private async Task Notify(Guid tripId, User actor, string what, string text)
        {
            try
            {
                var trip = await _dbContext.Trips
                    .Where(t => t.Id == tripId)
                    .Select(t => new { t.Name, t.UserId, MemberIds = t.Members.Select(m => m.UserId).ToList() })
                    .FirstAsync();
                var firstName = (actor.Name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Someone";
                var body = what == "asked"
                    ? $"{firstName} started a poll: {text}"
                    : $"Poll closed. {text}";
                await _sender.SendAsync(
                    trip.MemberIds.Append(trip.UserId).Where(id => id != actor.Id),
                    tripId, actor.Id, "poll",
                    title: trip.Name,
                    body: body,
                    pushTitle: $"📊 {trip.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Polls: couldn't notify trip {TripId}", tripId);
            }
        }

        private static DateTime ToUtc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };

        private static DateTime DateOnlyUtc(DateTime d)
        {
            var u = ToUtc(d);
            return new DateTime(u.Year, u.Month, u.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        private async Task<User?> GetCurrentUser()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
        }

        /// <summary>owner | member | viewer, or null when not on the trip.</summary>
        private async Task<string?> GetRole(Guid tripId, Guid userId)
        {
            var role = await _dbContext.TripMembers
                .Where(m => m.TripId == tripId && m.UserId == userId)
                .Select(m => m.Role)
                .FirstOrDefaultAsync();
            if (role != null) return role;
            // Older trips: the creator may have no member row.
            var isCreator = await _dbContext.Trips.AnyAsync(t => t.Id == tripId && t.UserId == userId);
            return isCreator ? "owner" : null;
        }
    }
}
