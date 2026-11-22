using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Push + bell for activity on stories:
    ///  - a comment on your story, straight away ("Satish commented: “…”");
    ///  - a reply to your comment, straight away ("Satish replied to you: “…”");
    ///  - likes on your story, grouped (queued as <see cref="PendingNotification"/>
    ///    and sent by NotificationDigestWorker: "Satish and 4 others liked your story").
    ///
    /// Nobody hears about their own activity, or about someone they blocked.
    /// Never fails the request.
    /// </summary>
    public class StoryNotifier
    {
        private const int SnippetLength = 100;

        private readonly AppDbContext _dbContext;
        private readonly NotificationSender _sender;
        private readonly NotificationQueueSignal _signal;
        private readonly ILogger<StoryNotifier> _logger;

        public StoryNotifier(AppDbContext dbContext, NotificationSender sender, NotificationQueueSignal signal,
            ILogger<StoryNotifier> logger)
        {
            _dbContext = dbContext;
            _sender = sender;
            _signal = signal;
            _logger = logger;
        }

        /// <summary>A new comment or reply: tell the story's author, and (for a reply) whoever it answers.</summary>
        /// <param name="parentAuthorId">For a reply: who wrote the comment it answers.</param>
        public async Task CommentAddedAsync(Journal journal, JournalComment comment, string? actorName, Guid? parentAuthorId)
        {
            try
            {
                var actorId = comment.UserId;
                var actor = FirstName(actorName);
                var snippet = Snippet(comment.Body);
                var title = string.IsNullOrWhiteSpace(journal.Title) ? "Your story" : journal.Title.Trim();

                var candidates = new List<Guid> { journal.UserId };
                if (parentAuthorId != null) candidates.Add(parentAuthorId.Value);
                var blockedMe = await _dbContext.UserBlocks
                    .Where(b => candidates.Contains(b.BlockerId) && b.BlockedId == actorId)
                    .Select(b => b.BlockerId)
                    .ToListAsync();
                bool Tell(Guid id) => id != actorId && !blockedMe.Contains(id);

                var replyTo = parentAuthorId != null && Tell(parentAuthorId.Value) ? parentAuthorId : null;
                if (replyTo != null)
                {
                    await _sender.SendAsync(new[] { replyTo.Value }, null, actorId, NotificationKinds.Reply,
                        title, $"{actor} replied to your comment: “{snippet}”", journalId: journal.Id);
                }

                // The story's author, unless they just got the reply above.
                if (Tell(journal.UserId) && replyTo != journal.UserId)
                {
                    var body = parentAuthorId == null
                        ? $"{actor} commented on your story: “{snippet}”"
                        : $"{actor} replied to a comment on your story: “{snippet}”";
                    await _sender.SendAsync(new[] { journal.UserId }, null, actorId, NotificationKinds.Comment,
                        title, body, journalId: journal.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push: couldn't send comment notifications for story {JournalId}", journal.Id);
            }
        }

        /// <summary>Someone liked a story: queue it (likes go out grouped, see NotificationDigestWorker).</summary>
        public async Task LikedAsync(Journal journal, Guid actorId)
        {
            if (journal.UserId == actorId) return;
            try
            {
                var blocked = await _dbContext.UserBlocks
                    .AnyAsync(b => b.BlockerId == journal.UserId && b.BlockedId == actorId);
                var queued = await _dbContext.PendingNotifications
                    .AnyAsync(p => p.JournalId == journal.Id && p.ActorId == actorId && p.Kind == NotificationKinds.Likes);
                if (blocked || queued) return;

                _dbContext.PendingNotifications.Add(new PendingNotification
                {
                    Id = Guid.NewGuid(),
                    JournalId = journal.Id,
                    ActorId = actorId,
                    Kind = NotificationKinds.Likes,
                    Change = "added",
                    CreatedAt = DateTime.UtcNow,
                });
                await _dbContext.SaveChangesAsync();
                _signal.Wake();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push: couldn't queue a like on story {JournalId}", journal.Id);
            }
        }

        /// <summary>Unliked before it went out: don't send it.</summary>
        public async Task UnlikedAsync(Guid journalId, Guid actorId)
        {
            try
            {
                await _dbContext.PendingNotifications
                    .Where(p => p.JournalId == journalId && p.ActorId == actorId && p.Kind == NotificationKinds.Likes)
                    .ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push: couldn't drop a queued like on story {JournalId}", journalId);
            }
        }

        /// <summary>"Satish liked your story", "Satish and Priya …", "Satish, Priya and 3 others …".</summary>
        /// <param name="names">First names, newest first, one per person.</param>
        internal static string ComposeLikes(List<string> names)
        {
            return names.Count switch
            {
                0 => "Someone liked your story",
                1 => $"{names[0]} liked your story",
                2 => $"{names[0]} and {names[1]} liked your story",
                3 => $"{names[0]}, {names[1]} and 1 other liked your story",
                _ => $"{names[0]}, {names[1]} and {names.Count - 2} others liked your story",
            };
        }

        private static string Snippet(string body)
        {
            var text = string.Join(' ', (body ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= SnippetLength ? text : text[..(SnippetLength - 1)].TrimEnd() + "…";
        }

        internal static string FirstName(string? name)
        {
            var first = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrEmpty(first) ? "Someone" : first;
        }
    }
}
