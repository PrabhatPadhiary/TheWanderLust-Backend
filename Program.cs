using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddHttpClient<IGooglePlacesService, GooglePlacesService>();

builder.Services.Configure<PexelsSettings>(
    builder.Configuration.GetSection("Pexels"));
builder.Services.AddHttpClient<IPexelsService, PexelsService>();

builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddSingleton<CloudinaryService>();

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200", "https://thewanderlust-frontend.onrender.com","https://wayraa.com","https://www.wayraa.com")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
