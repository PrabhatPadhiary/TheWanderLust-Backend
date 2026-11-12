using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Settings;

public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IOptions<CloudinarySettings> options)
    {
        var settings = options.Value;

        var account = new Account(
            settings.CloudName,
            settings.ApiKey,
            settings.ApiSecret
        );

        _cloudinary = new Cloudinary(account);
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folder)
    {
        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        return uploadResult.SecureUrl.ToString();
    }

    /// <summary>
    /// Delete one image by its Cloudinary URL
    /// (https://res.cloudinary.com/&lt;cloud&gt;/image/upload/v123/journals/&lt;id&gt;/photo.jpg).
    /// URLs that aren't Cloudinary uploads are ignored.
    /// </summary>
    public async Task DeleteImageByUrlAsync(string url)
    {
        var publicId = PublicIdFromUrl(url);
        if (publicId == null) return;
        await _cloudinary.DestroyAsync(new DeletionParams(publicId) { ResourceType = ResourceType.Image });
    }

    /// <summary>
    /// The public id in a Cloudinary upload URL: the path after "/upload/",
    /// without transformations, the version ("v123") or the file extension.
    /// </summary>
    public static string? PublicIdFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("res.cloudinary.com")) return null;
        var marker = "/upload/";
        var at = url.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        var path = url[(at + marker.Length)..].Split('?')[0];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Drop transformation segments (e.g. "w_400,c_fill") and the version.
        while (parts.Count > 1 && (parts[0].Contains(',') || parts[0].Contains('_') && !parts[0].Contains('.') && parts.Count > 2 && parts[0].Length < 40 && parts[0].Split('_')[0].Length <= 2))
            parts.RemoveAt(0);
        if (parts.Count > 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^v\d+$"))
            parts.RemoveAt(0);
        if (parts.Count == 0) return null;
        var last = parts[^1];
        var dot = last.LastIndexOf('.');
        if (dot > 0) parts[^1] = last[..dot];
        return Uri.UnescapeDataString(string.Join('/', parts));
    }

    /// <summary>
    /// Delete every image under a folder (e.g. "journals/{id}") and the folder
    /// itself. Used when a story or an account is deleted.
    /// </summary>
    public async Task DeleteFolderAsync(string folder)
    {
        var prefix = folder.TrimEnd('/') + "/";
        await _cloudinary.DeleteResourcesByPrefixAsync(prefix);
        await _cloudinary.DeleteFolderAsync(folder.TrimEnd('/'));
    }
}

