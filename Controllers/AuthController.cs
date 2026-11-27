using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AppDbContext dbContext, CloudinaryService cloudinaryService, ILogger<AuthController> logger)
        {
            _dbContext = dbContext;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] FirebaseLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Token))
                return BadRequest("Token is required.");

            try
            {
                // Verify the Firebase token
                var firebaseToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.Token);
                var firebaseId = firebaseToken.Uid;

                var email = firebaseToken.Claims.TryGetValue("email", out var emailClaim)
                    ? emailClaim.ToString()
                    : null;

                var name = firebaseToken.Claims.TryGetValue("name", out var nameClaim)
                    ? nameClaim.ToString()
                    : null;

                // The Google account's photo (Google sign-in only).
                var picture = firebaseToken.Claims.TryGetValue("picture", out var pictureClaim)
                    ? pictureClaim?.ToString()
                    : null;

                // Check if user exists
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseId);

                if (user == null)
                {
                    // Create new user
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        FirebaseId = firebaseId,
                        Name = name ?? "User",
                        Email = email ?? "",
                        Role = "User",
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };

                    _dbContext.Users.Add(user);
                    await _dbContext.SaveChangesAsync();
                }

                if (!user.IsActive)
                    return StatusCode(StatusCodes.Status403Forbidden, TheWanderLustWebAPI.Middleware.SuspendedAccountMiddleware.Message);

                await SyncGooglePhotoAsync(user, picture);

                return Ok(new
                {
                    user.Id,
                    user.FirebaseId,
                    user.Name,
                    user.Email,
                    user.Role,
                    user.CreatedAt,
                    user.IsActive
                });
            }
            catch (FirebaseAuthException)
            {
                return Unauthorized("Invalid Firebase token.");
            }
        }

        /// <summary>
        /// Keep the profile photo in step with the Google account:
        ///  - no photo yet (new or existing user): copy the Google one;
        ///  - photo came from Google and Google's has changed: copy the new one;
        ///  - uploaded in Wayraa, or removed: leave it alone.
        /// The copy goes to Cloudinary like an uploaded photo, so it never
        /// breaks when the Google one changes. Never blocks signing in.
        /// </summary>
        private async Task SyncGooglePhotoAsync(User user, string? picture)
        {
            if (string.IsNullOrWhiteSpace(picture) || !picture.StartsWith("https://")) return;
            var firstTime = user.PhotoSource == null && user.PhotoUrl == null;
            var changed = user.PhotoSource == "google" && user.GooglePhotoSourceUrl != picture;
            if (!firstTime && !changed) return;

            try
            {
                var folder = $"users/{user.Id}";
                try { await _cloudinaryService.DeleteFolderAsync(folder); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not clear old profile photo for user {UserId}.", user.Id); }

                var url = await _cloudinaryService.UploadImageFromUrlAsync(LargeGooglePhoto(picture), folder);
                user.PhotoUrl = url;
                user.PhotoSource = "google";
                user.GooglePhotoSourceUrl = picture;
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Tried again at the next sign-in.
                _logger.LogWarning(ex, "Could not copy the Google photo for user {UserId}.", user.Id);
            }
        }

        /// <summary>
        /// Google's photo links end in a size, "=s96-c" (96 px): ask for 600 px
        /// so the photo is sharp on profile pages.
        /// </summary>
        private static string LargeGooglePhoto(string url)
        {
            var m = System.Text.RegularExpressions.Regex.Match(url, @"=s\d+(-c)?$");
            return m.Success ? url[..m.Index] + "=s600-c" : url;
        }
    }

    public class FirebaseLoginRequest
    {
        public string Token { get; set; }
    }
}
