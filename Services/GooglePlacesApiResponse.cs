using System.Text.Json.Serialization;

namespace TheWanderLustWebAPI.Services
{
    internal class GooglePlacesApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("result")]
        public GooglePlaceResult Result { get; set; }
    }

    internal class GoogleNearbySearchResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleNearbyResult> Results { get; set; }
    }

    internal class GoogleTextSearchResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleTextSearchResult> Results { get; set; }
    }

    internal class GoogleTextSearchResult
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("formatted_address")]
        public string FormattedAddress { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("user_ratings_total")]
        public int? UserRatingsTotal { get; set; }

        [JsonPropertyName("price_level")]
        public int? PriceLevel { get; set; }

        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; }

        [JsonPropertyName("photos")]
        public List<GooglePhoto> Photos { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; }
    }

    internal class GoogleNearbyResult
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("vicinity")]
        public string Vicinity { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; }

        [JsonPropertyName("photos")]
        public List<GooglePhoto> Photos { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; }
    }

    internal class GooglePlaceResult
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("formatted_address")]
        public string FormattedAddress { get; set; }

        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; }
    }

    internal class GooglePlaceDetailsResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("result")]
        public GooglePlaceDetailResult Result { get; set; }
    }

    internal class GooglePlaceDetailResult
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("formatted_address")]
        public string FormattedAddress { get; set; }

        [JsonPropertyName("formatted_phone_number")]
        public string FormattedPhoneNumber { get; set; }

        [JsonPropertyName("website")]
        public string Website { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("user_ratings_total")]
        public int? UserRatingsTotal { get; set; }

        [JsonPropertyName("price_level")]
        public int? PriceLevel { get; set; }

        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; }

        [JsonPropertyName("photos")]
        public List<GooglePhoto> Photos { get; set; }

        [JsonPropertyName("reviews")]
        public List<GoogleReview> Reviews { get; set; }

        [JsonPropertyName("opening_hours")]
        public GoogleOpeningHours OpeningHours { get; set; }
    }

    internal class GoogleReview
    {
        [JsonPropertyName("author_name")]
        public string AuthorName { get; set; }

        [JsonPropertyName("profile_photo_url")]
        public string ProfilePhotoUrl { get; set; }

        [JsonPropertyName("rating")]
        public int Rating { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("relative_time_description")]
        public string RelativeTimeDescription { get; set; }
    }

    internal class GoogleOpeningHours
    {
        [JsonPropertyName("open_now")]
        public bool? OpenNow { get; set; }

        [JsonPropertyName("weekday_text")]
        public List<string> WeekdayText { get; set; }
    }

    internal class GoogleGeometry
    {
        [JsonPropertyName("location")]
        public GoogleLocation Location { get; set; }
    }

    internal class GoogleLocation
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }

    internal class GooglePhoto
    {
        [JsonPropertyName("photo_reference")]
        public string PhotoReference { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }
}
