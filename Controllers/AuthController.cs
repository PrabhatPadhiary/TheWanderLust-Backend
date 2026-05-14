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

        public AuthController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
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
    }

    public class FirebaseLoginRequest
    {
        public string Token { get; set; }
    }
}
