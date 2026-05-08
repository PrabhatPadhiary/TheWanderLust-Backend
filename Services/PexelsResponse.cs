using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Services
{
    internal class PexelsResponse
    {
        [JsonPropertyName("photos")]
        public List<PexelsPhoto> Photos { get; set; }
    }

    internal class PexelsPhoto
    {
        [JsonPropertyName("src")]
        public PexelsPhotoSrc Src { get; set; }
    }

    internal class PexelsPhotoSrc
    {
        [JsonPropertyName("landscape")]
        public string Landscape { get; set; }

        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("large2x")]
        public string Large2x { get; set; }
    }
}
