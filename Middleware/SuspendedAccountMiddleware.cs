using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Middleware
{
    /// <summary>
    /// Suspended accounts (Users.IsActive = false, set by a moderator) get 403
    /// on every signed-in request, except deleting their own account (they can
    /// always remove their data). Signed-out browsing is unaffected.
    ///
    /// Whether an account is active is cached for a minute; suspending or
    /// restoring someone clears it (<see cref="Forget"/>), so it applies at once.
    /// </summary>
    public class SuspendedAccountMiddleware
    {
        public const string Message = "This account has been suspended for breaking Wayraa's community rules. Contact support@wayraa.com if you think this is a mistake.";
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

        private readonly RequestDelegate _next;

        public SuspendedAccountMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext dbContext, IMemoryCache cache)
        {
            var uid = context.User.FindFirst("firebase_uid")?.Value;
            if (!string.IsNullOrEmpty(uid) && !IsAccountDeletion(context.Request))
            {
                var active = await cache.GetOrCreateAsync(CacheKey(uid), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheFor;
                    entry.Size = 1;
                    // No row yet (first sign-in): treat as active.
                    return dbContext.Users.Where(u => u.FirebaseId == uid)
                        .Select(u => (bool?)u.IsActive).FirstOrDefaultAsync();
                });
                if (active == false)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(Message);
                    return;
                }
            }
            await _next(context);
        }

        public static void Forget(IMemoryCache cache, string firebaseUid) => cache.Remove(CacheKey(firebaseUid));

        private static string CacheKey(string uid) => "account-active:" + uid;

        private static bool IsAccountDeletion(HttpRequest request) =>
            HttpMethods.IsDelete(request.Method)
            && string.Equals(request.Path.Value?.TrimEnd('/'), "/api/users/me", StringComparison.OrdinalIgnoreCase);
    }
}
