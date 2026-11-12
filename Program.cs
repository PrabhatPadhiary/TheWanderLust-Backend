using System.Threading.RateLimiting;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Auth;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Services;
using TheWanderLustWebAPI.Settings;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
});

// Disable file watching to avoid inotify limit issues on shared hosting
builder.Configuration.Sources
    .OfType<Microsoft.Extensions.Configuration.FileConfigurationSource>()
    .ToList()
    .ForEach(s => s.ReloadOnChange = false);

// Initialize Firebase Admin SDK
var firebaseCredentialPath = File.Exists("/etc/secrets/firebase-service-account.json")
    ? "/etc/secrets/firebase-service-account.json"
    : "wanderlust-35cd9-firebase-adminsdk-fbsvc-0e93d55526.json";

FirebaseApp.Create(new AppOptions
{
    Credential = GoogleCredential.FromFile(firebaseCredentialPath)
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication("Firebase")
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthHandler>("Firebase", null);
builder.Services.AddAuthorization();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("SqlServerConnStr")
    ));

builder.Services.Configure<GooglePlacesSettings>(
    builder.Configuration.GetSection("GooglePlaces"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IGooglePlacesService, GooglePlacesService>();
// Place id -> main photo, with its own size-limited cache (see PlacePhotoService).
builder.Services.AddHttpClient(nameof(PlacePhotoService));
builder.Services.AddSingleton<PlacePhotoService>();

builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddSingleton<CloudinaryService>();

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200", "http://localhost:8081", "http://192.168.1.2:8081", "exp://192.168.1.2:8081", "https://thewanderlust-frontend.onrender.com","https://wayraa.com","https://www.wayraa.com")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// ── Rate limits for the endpoints that call Google (paid per request) ──────
// Per client IP, fixed one-minute windows. Real users stay far below these;
// a script hammering the API gets 429s instead of spending the Places quota.
//  - "google": place search / nearby / lists / details
//  - "photos": place photos (a screen can show dozens at once)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };

    static string ClientKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy(RateLimits.Google, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.AddPolicy(RateLimits.Photos, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Behind Render's proxy every request comes from the proxy's address; take the
// real client IP from X-Forwarded-For (only the last hop, which the proxy adds,
// so a client can't fake its IP by sending its own header).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// First, so everything after sees the real client IP and scheme.
app.UseForwardedHeaders();

// Google photo links in any JSON response become links to our photo proxy,
// so the Google API key never leaves the server (see PlacePhotoLinks).
app.UseMiddleware<TheWanderLustWebAPI.Middleware.PhotoLinkRewriteMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Skip HTTPS redirection in Development so the mobile app (Expo Go on a phone)
// can reach the API over plain http://<LAN-IP>:5273 without being bounced to a
// self-signed HTTPS endpoint it can't validate.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
