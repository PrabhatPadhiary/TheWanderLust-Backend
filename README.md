# TheWanderLust Backend

.NET 8 Web API for the WanderLust travel planning application. Provides destination search, place details, hero images, and user authentication.

## Tech Stack

- **Runtime:** .NET 8 / ASP.NET Core
- **Database:** PostgreSQL (hosted on Neon)
- **ORM:** Entity Framework Core + Npgsql
- **Auth:** Firebase Admin SDK (verifies Firebase ID tokens from frontend)
- **Caching:** In-memory cache (IMemoryCache) with 24h TTL

## External Services

| Service | Purpose | Dashboard |
|---------|---------|-----------|
| **Render** | Hosting (Docker) | https://dashboard.render.com |
| **Neon** | PostgreSQL database | https://console.neon.tech |
| **Firebase** | Authentication (frontend login + backend token verification) | https://console.firebase.google.com |
| **Google Places API** | Destination search, text search, place details | https://console.cloud.google.com/apis |
| **Pexels API** | Hero landscape images for destinations | https://www.pexels.com/api |
| **Cloudinary** | Image uploads (reserved for future use) | https://cloudinary.com/console |

## API Endpoints

### Destinations (`/api/destinations`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/search?placeId={id}` | Get destination with restaurants, stays, and attractions |
| GET | `/filter?placeId={id}&filter={type}` | Filter by category (e.g., villas, cafes, museums) |
| GET | `/details?placeId={id}` | Full place details (reviews, hours, phone, website) |
| GET | `/hero-image?placeId={id}` | Get 3 landscape hero images from Pexels |

### Auth (`/api/auth`)

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/login` | Verify Firebase token, create/return user |

## Project Structure

```
Controllers/          API controllers
  AuthController.cs       Firebase login + user sync
  DestinationsController.cs   All destination endpoints
Services/             Business logic + external API calls
  IGooglePlacesService.cs     Interface
  GooglePlacesService.cs      Google Places + Text Search
  IPexelsService.cs           Interface
  PexelsService.cs            Pexels hero images
  GooglePlacesApiResponse.cs  Internal deserialization models
  PexelsResponse.cs           Internal deserialization models
Models/               Data models
  User.cs                 EF Core entity
  Dtos/                   Response DTOs
Settings/             Configuration classes
  GooglePlacesSettings.cs
  PexelsSettings.cs
  CloudinarySettings.cs
Context/              Database
  AppDbContext.cs
Auth/                 Authentication
  FirebaseAuthHandler.cs
```

## Local Development

### Prerequisites
- .NET 8 SDK
- dotnet-ef tool (`dotnet tool install --global dotnet-ef`)
- Firebase service account JSON file in project root

### Setup

1. Clone the repo
2. Create `appsettings.Development.json` with your real keys (see appsettings.json for structure)
3. Place your Firebase service account JSON in the project root
4. Run migrations: `dotnet ef database update`
5. Run: `dotnet run`
6. Swagger UI: http://localhost:5273/swagger

### Configuration (appsettings.Development.json)

```json
{
  "ConnectionStrings": {
    "SqlServerConnStr": "Host=...;Database=wanderlust;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
  },
  "GooglePlaces": {
    "ApiKey": "your-key",
    "BaseUrl": "https://maps.googleapis.com/maps/api/place"
  },
  "Pexels": {
    "ApiKey": "your-key",
    "BaseUrl": "https://api.pexels.com/v1"
  },
  "Cloudinary": {
    "CloudName": "...",
    "ApiKey": "...",
    "ApiSecret": "..."
  }
}
```

## Production Deployment (Render)

### Environment Variables

Set these in Render dashboard > Environment:

| Key | Value |
|-----|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__SqlServerConnStr` | Neon connection string |
| `GooglePlaces__ApiKey` | Google API key |
| `GooglePlaces__BaseUrl` | `https://maps.googleapis.com/maps/api/place` |
| `Pexels__ApiKey` | Pexels API key |
| `Pexels__BaseUrl` | `https://api.pexels.com/v1` |
| `Cloudinary__CloudName` | Cloudinary cloud name |
| `Cloudinary__ApiKey` | Cloudinary API key |
| `Cloudinary__ApiSecret` | Cloudinary API secret |

### Secret Files

Add in Render dashboard > Secret Files:

| Filename | Content |
|----------|---------|
| `firebase-service-account.json` | Firebase Admin SDK service account JSON |

Available at `/etc/secrets/firebase-service-account.json` at runtime.

### Deploy

Push to `main` branch → Render auto-deploys (or click Manual Deploy).

## Database (Neon)

- Provider: Neon (serverless PostgreSQL)
- Database name: `wanderlust`
- Migrations: EF Core Code-First
- Add migration: `dotnet ef migrations add MigrationName`
- Apply: `dotnet ef database update`

Note: Your office network may block port 5432. Use mobile hotspot or personal network for DB operations.

## Caching Strategy

All Google Places and Pexels responses are cached in-memory for 24 hours:
- `destinations_{placeId}` — main search results
- `destinations_filter_{placeId}_{filter}` — filter results
- `place_details_{placeId}` — place details
- `pexels_hero_{placeId}` — hero images

Cache resets on app restart. Check `X-Data-Source` response header (`cache` or `api`) to verify.

## Search Ranking

Results are filtered and ranked:
- Rating >= 4.2 and user ratings >= 500
- Sorted by: `rating × log₁₀(userRatingsTotal + 1)` (balances quality with popularity)
- Top 8 results per category
