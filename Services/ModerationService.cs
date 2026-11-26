using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheWanderLustWebAPI.Middleware;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Moderation: who the moderators are, telling them about new reports, and
    /// removing reported content.
    ///
    /// Moderators are users whose Role is "Admin" (set it on their row in the
    /// Users table). Each new report sends them a push and a bell entry
    /// ("report"), which opens Reports in the app.
    /// </summary>
    public class ModerationService
    {
        public const string ModeratorRole = "Admin";

        private readonly AppDbContext _dbContext;
        private readonly NotificationSender _sender;
        private readonly CloudinaryService _cloudinary;
        private readonly ILogger<ModerationService> _logger;
        private readonly IMemoryCache _cache;

        public ModerationService(AppDbContext dbContext, NotificationSender sender, CloudinaryService cloudinary,
            ILogger<ModerationService> logger, IMemoryCache cache)
        {
            _cache = cache;
            _dbContext = dbContext;
            _sender = sender;
            _cloudinary = cloudinary;
            _logger = logger;
        }

        public static bool IsModerator(User? user) =>
            user != null && string.Equals(user.Role, ModeratorRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>A new report: tell the moderators (never fails the request).</summary>
        /// <param name="what">"story", "comment" or "profile".</param>
        public async Task ReportedAsync(string what, string reason, Guid reporterId, string? snippet)
        {
            try
            {
                var moderators = await _dbContext.Users
                    .Where(u => u.Role.ToLower() == ModeratorRole.ToLower() && u.Id != reporterId)
                    .Select(u => u.Id)
                    .ToListAsync();
                if (moderators.Count == 0)
                {
                    _logger.LogWarning("A {What} was reported, but there are no moderators (Role = \"Admin\") to tell.", what);
                    return;
                }
                var label = reason.Replace('_', ' ');
                var body = string.IsNullOrWhiteSpace(snippet)
                    ? $"A {what} was reported ({label})."
                    : $"A {what} was reported ({label}): “{Shorten(snippet, 90)}”";
                await _sender.SendAsync(moderators, null, reporterId, "report", "New report", body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Moderation: couldn't tell moderators about a report");
            }
        }

        /// <summary>Delete a comment (its replies go too) and fix the story's comment count.</summary>
        public async Task<bool> RemoveCommentAsync(Guid commentId)
        {
            var comment = await _dbContext.JournalComments.FirstOrDefaultAsync(c => c.Id == commentId);
            if (comment == null) return false;
            var removed = 1 + await _dbContext.JournalComments.CountAsync(c => c.ParentCommentId == commentId);
            var journal = await _dbContext.Journals.FindAsync(comment.JournalId);
            if (journal != null)
                journal.CommentsCount = Math.Max(0, journal.CommentsCount - removed);
            _dbContext.JournalComments.Remove(comment);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        /// <summary>A reported profile: clear their bio and close its reports.</summary>
        public async Task<bool> ClearBioAsync(Guid userId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return false;
            user.Bio = null;
            await _dbContext.UserReports
                .Where(r => r.ReportedUserId == userId && r.Status == "open")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "resolved"));
            await _dbContext.SaveChangesAsync();
            return true;
        }

        /// <summary>Delete a story and its photos.</summary>
        public async Task<bool> RemoveStoryAsync(Guid journalId)
        {
            var journal = await _dbContext.Journals.FirstOrDefaultAsync(j => j.Id == journalId);
            if (journal == null) return false;
            _dbContext.Journals.Remove(journal);
            await _dbContext.SaveChangesAsync();
            try
            {
                await _cloudinary.DeleteFolderAsync($"journals/{journalId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete Cloudinary photos for removed journal {JournalId}.", journalId);
            }
            return true;
        }

        /// <summary>
        /// Suspend (active false) or restore an account. Suspended: every
        /// signed-in request is refused, their stories and comments are hidden
        /// from everyone, and their phones stop getting notifications.
        /// Moderators can't be suspended this way.
        /// </summary>
        /// <returns>null when done, else why not.</returns>
        public async Task<string?> SetActiveAsync(Guid userId, bool active)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return "That account no longer exists.";
            if (!active && IsModerator(user)) return "Moderators can't be suspended.";
            user.IsActive = active;
            if (!active)
                await _dbContext.PushTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _dbContext.SaveChangesAsync();
            SuspendedAccountMiddleware.Forget(_cache, user.FirebaseId);
            _logger.LogWarning("Moderation: account {UserId} {Action}.", userId, active ? "restored" : "suspended");
            return null;
        }

        private static string Shorten(string s, int max)
        {
            var text = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
        }
    }
}
