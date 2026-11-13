using System.Security.Claims;
using System.Text.Encodings.Web;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TheWanderLustWebAPI.Auth
{
    public class FirebaseAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public FirebaseAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string token;
            var authHeader = Request.Headers["Authorization"].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader["Bearer ".Length..].Trim();
            }
            else if (Request.Path.StartsWithSegments(TheWanderLustWebAPI.Live.TripHub.Path)
                && Request.Query.TryGetValue("access_token", out var queryToken)
                && !string.IsNullOrWhiteSpace(queryToken))
            {
                // SignalR's WebSocket can't send headers, so the live-updates hub
                // (and only it) takes the token from the query string.
                token = queryToken.ToString();
            }
            else
            {
                return AuthenticateResult.NoResult();
            }

            try
            {
                var firebaseToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, firebaseToken.Uid),
                    new Claim("firebase_uid", firebaseToken.Uid)
                };

                if (firebaseToken.Claims.TryGetValue("email", out var email))
                    claims.Add(new Claim(ClaimTypes.Email, email.ToString()));

                if (firebaseToken.Claims.TryGetValue("name", out var name))
                    claims.Add(new Claim(ClaimTypes.Name, name.ToString()));

                var identity = new ClaimsIdentity(claims, "Firebase");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Firebase");

                return AuthenticateResult.Success(ticket);
            }
            catch (FirebaseAuthException)
            {
                return AuthenticateResult.Fail("Invalid Firebase token.");
            }
            catch (Exception ex)
            {
                return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
            }
        }
    }
}
