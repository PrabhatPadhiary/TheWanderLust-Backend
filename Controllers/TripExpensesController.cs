using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/trips/{tripId}/expenses")]
    [Authorize]
    public class TripExpensesController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripExpensesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // GET api/trips/{tripId}/expenses
        [HttpGet]
        public async Task<IActionResult> GetAll(Guid tripId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");

            // All roles (owner, member, viewer) can view expenses
            var expenses = await _dbContext.TripExpenses
                .Where(e => e.TripId == tripId)
                .OrderBy(e => e.Date)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Amount,
                    Category = e.Category.ToString().ToLower(),
                    e.Date,
                    e.PaidByMemberId,
                    e.PaidByName,
                    e.Notes,
                    e.CreatedAt,
                    e.UpdatedAt
                })
                .ToListAsync();

            return Ok(expenses);
        }

        // GET api/trips/{tripId}/expenses/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid tripId, Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");

            // All roles can view a single expense
            var expense = await _dbContext.TripExpenses
                .Where(e => e.TripId == tripId && e.Id == id)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Amount,
                    Category = e.Category.ToString().ToLower(),
                    e.Date,
                    e.PaidByMemberId,
                    e.PaidByName,
                    e.Notes,
                    e.CreatedAt,
                    e.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (expense == null)
                return NotFound("Expense not found.");

            return Ok(expense);
        }

        // POST api/trips/{tripId}/expenses
        [HttpPost]
        public async Task<IActionResult> Create(Guid tripId, [FromBody] CreateTripExpenseDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Title))
                return BadRequest("Title is required.");

            if (dto.Amount <= 0)
                return BadRequest("Amount must be greater than zero.");

            if (string.IsNullOrWhiteSpace(dto.PaidByMemberId))
                return BadRequest("PaidByMemberId is required.");

            if (string.IsNullOrWhiteSpace(dto.PaidByName))
                return BadRequest("PaidByName is required.");

            if (!Enum.TryParse<ExpenseCategory>(dto.Category, ignoreCase: true, out var category))
                return BadRequest("Invalid category. Must be one of: stay, food, activity, transport, other.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var expense = new TripExpense
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                Title = dto.Title,
                Amount = dto.Amount,
                Category = category,
                Date = dto.Date.ToUniversalTime(),
                PaidByMemberId = dto.PaidByMemberId,
                PaidByName = dto.PaidByName,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.TripExpenses.Add(expense);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                expense.Id,
                expense.Title,
                expense.Amount,
                Category = expense.Category.ToString().ToLower(),
                expense.Date,
                expense.PaidByMemberId,
                expense.PaidByName,
                expense.Notes,
                expense.CreatedAt,
                expense.UpdatedAt
            });
        }

        // PUT api/trips/{tripId}/expenses/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid tripId, Guid id, [FromBody] UpdateTripExpenseDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var expense = await _dbContext.TripExpenses
                .FirstOrDefaultAsync(e => e.TripId == tripId && e.Id == id);

            if (expense == null)
                return NotFound("Expense not found.");

            // Members can only edit their own expenses
            if (role == "member" && expense.PaidByMemberId != userId.Value.ToString())
                return Forbid();

            if (!string.IsNullOrWhiteSpace(dto.Title))
                expense.Title = dto.Title;

            if (dto.Amount.HasValue)
            {
                if (dto.Amount.Value <= 0)
                    return BadRequest("Amount must be greater than zero.");
                expense.Amount = dto.Amount.Value;
            }

            if (!string.IsNullOrWhiteSpace(dto.Category))
            {
                if (!Enum.TryParse<ExpenseCategory>(dto.Category, ignoreCase: true, out var category))
                    return BadRequest("Invalid category. Must be one of: stay, food, activity, transport, other.");
                expense.Category = category;
            }

            if (dto.Date.HasValue)
                expense.Date = dto.Date.Value.ToUniversalTime();

            if (!string.IsNullOrWhiteSpace(dto.PaidByMemberId))
                expense.PaidByMemberId = dto.PaidByMemberId;

            if (!string.IsNullOrWhiteSpace(dto.PaidByName))
                expense.PaidByName = dto.PaidByName;

            if (dto.Notes != null)
                expense.Notes = dto.Notes;

            expense.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                expense.Id,
                expense.Title,
                expense.Amount,
                Category = expense.Category.ToString().ToLower(),
                expense.Date,
                expense.PaidByMemberId,
                expense.PaidByName,
                expense.Notes,
                expense.CreatedAt,
                expense.UpdatedAt
            });
        }

        // DELETE api/trips/{tripId}/expenses/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid tripId, Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(tripId, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var expense = await _dbContext.TripExpenses
                .FirstOrDefaultAsync(e => e.TripId == tripId && e.Id == id);

            if (expense == null)
                return NotFound("Expense not found.");

            // Members can only delete their own expenses
            if (role == "member" && expense.PaidByMemberId != userId.Value.ToString())
                return Forbid();

            _dbContext.TripExpenses.Remove(expense);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Expense deleted." });
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
