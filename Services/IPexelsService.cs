namespace TheWanderLustWebAPI.Services
{
    public interface IPexelsService
    {
        Task<List<string>> GetHeroImagesAsync(string placeId, CancellationToken cancellationToken = default);
    }
}
