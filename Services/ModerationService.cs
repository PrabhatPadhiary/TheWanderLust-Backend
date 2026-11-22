using Microsoft.EntityFrameworkCore;
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

        public ModerationService(AppDbContext dbContext, NotificationSender sender, CloudinaryService cloudinary,
            ILogger<ModerationService> logger)
        {
            _dbContext = dbContext;
            _sender = sender;
            _cloudinary = cloudinary;
            _logger = logger;
        }

        public static bool IsModerator(User? user) =>
            user != null && string.Equals(user.Role, ModeratorRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>A new report: tell the moderators (never fails the request).</summary>
        /// <param name="what">"story" or "comment".</param>
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

        private static string Shorten(string s, int max)
        {
            var text = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
        }
    }
}
