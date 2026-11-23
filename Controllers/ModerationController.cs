using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// Reviewing reports, for moderators only (Role = "Admin"): see what's
    /// reported, then remove it or dismiss the reports.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ModerationController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly ModerationService _moderation;

        public ModerationController(AppDbContext dbContext, ModerationService moderation)
        {
            _dbContext = dbContext;
            _moderation = moderation;
        }

        /// <summary>
        /// Open reports, one row per reported story or comment, most recently
        /// reported first.
        /// </summary>
        [HttpGet("reports")]
        public async Task<IActionResult> GetReports()
        {
            if (!await IsModerator()) return Forbid();

            var storyReports = await _dbContext.JournalReports
                .Where(r => r.Status == "open")
                .Select(r => new
                {
                    r.JournalId,
                    r.Reason,
                    r.Details,
                    r.CreatedAt,
                    Title = r.Journal.Title,
                    Body = r.Journal.Body,
                    AuthorId = r.Journal.UserId,
                    AuthorName = r.Journal.User.Name,
                })
                .ToListAsync();

            var commentReports = await _dbContext.CommentReports
                .Where(r => r.Status == "open")
                .Select(r => new
                {
                    r.CommentId,
                    r.Comment.JournalId,
                    r.Reason,
                    r.Details,
                    r.CreatedAt,
                    Title = r.Comment.Journal.Title,
                    Body = r.Comment.Body,
                    AuthorId = r.Comment.UserId,
                    AuthorName = r.Comment.User.Name,
                })
                .ToListAsync();

            var stories = storyReports.GroupBy(r => r.JournalId).Select(g => new ReportItem(
                "story", g.Key, g.Key, g.First().Title, Snip(g.First().Body),
                g.First().AuthorId, g.First().AuthorName, g.Count(),
                g.Select(r => r.Reason).Distinct().ToList(),
                g.Where(r => !string.IsNullOrWhiteSpace(r.Details)).Select(r => r.Details!).ToList(),
                g.Max(r => r.CreatedAt)));

            var comments = commentReports.GroupBy(r => r.CommentId).Select(g => new ReportItem(
                "comment", g.Key, g.First().JournalId, g.First().Title, Snip(g.First().Body),
                g.First().AuthorId, g.First().AuthorName, g.Count(),
                g.Select(r => r.Reason).Distinct().ToList(),
                g.Where(r => !string.IsNullOrWhiteSpace(r.Details)).Select(r => r.Details!).ToList(),
                g.Max(r => r.CreatedAt)));

            return Ok(stories.Concat(comments).OrderByDescending(i => i.LastReportedAt));
        }

        /// <summary>Remove the reported story or comment (its reports go with it).</summary>
        [HttpPost("reports/{kind}/{targetId:guid}/remove")]
        public async Task<IActionResult> Remove(string kind, Guid targetId)
        {
            if (!await IsModerator()) return Forbid();
            var removed = kind switch
            {
                "story" => await _moderation.RemoveStoryAsync(targetId),
                "comment" => await _moderation.RemoveCommentAsync(targetId),
                _ => (bool?)null,
            };
            if (removed == null) return BadRequest("Kind must be story or comment.");
            return Ok(new { removed = removed.Value, message = removed.Value ? "Removed." : "It was already deleted." });
        }

        /// <summary>Nothing wrong with it: close its reports.</summary>
        [HttpPost("reports/{kind}/{targetId:guid}/dismiss")]
        public async Task<IActionResult> Dismiss(string kind, Guid targetId)
        {
            if (!await IsModerator()) return Forbid();
            int closed;
            if (kind == "story")
                closed = await _dbContext.JournalReports
                    .Where(r => r.JournalId == targetId && r.Status == "open")
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "resolved"));
            else if (kind == "comment")
                closed = await _dbContext.CommentReports
                    .Where(r => r.CommentId == targetId && r.Status == "open")
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "resolved"));
            else
                return BadRequest("Kind must be story or comment.");
            return Ok(new { closed });
        }

        /// <summary>Suspend an account (e.g. with "Remove and suspend author").</summary>
        [HttpPost("users/{userId:guid}/suspend")]
        public async Task<IActionResult> Suspend(Guid userId)
        {
            if (!await IsModerator()) return Forbid();
            var problem = await _moderation.SetActiveAsync(userId, false);
            return problem == null ? Ok(new { suspended = true }) : BadRequest(problem);
        }

        /// <summary>Restore a suspended account.</summary>
        [HttpPost("users/{userId:guid}/restore")]
        public async Task<IActionResult> Restore(Guid userId)
        {
            if (!await IsModerator()) return Forbid();
            var problem = await _moderation.SetActiveAsync(userId, true);
            return problem == null ? Ok(new { suspended = false }) : BadRequest(problem);
        }

        private async Task<bool> IsModerator()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid)) return false;
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return ModerationService.IsModerator(user);
        }

        private static string Snip(string? body)
        {
            var text = string.Join(' ', (body ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= 300 ? text : text[..299].TrimEnd() + "…";
        }

        public record ReportItem(string Kind, Guid TargetId, Guid JournalId, string? JournalTitle, string Content,
            Guid AuthorId, string? AuthorName, int Reports, List<string> Reasons, List<string> Details,
            DateTime LastReportedAt);
    }
}
