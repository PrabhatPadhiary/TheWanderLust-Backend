using System.Text.RegularExpressions;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Google place photo links, without our API key.
    ///
    /// Google's photo URL needs the key in its query string, so a link like
    /// that must never reach a browser or the app. Clients get a link to our
    /// own photo proxy instead (/api/Destinations/photo), which adds the key on
    /// the server:
    ///   https://&lt;api host&gt;/api/Destinations/photo?photo_reference=…&amp;maxWidth=800
    /// The parameter is called photo_reference so apps that read the reference
    /// out of the link keep working.
    /// </summary>
    public static class PlacePhotoLinks
    {
        public const string ProxyPath = "/api/Destinations/photo";

        // A Google place photo URL inside text or JSON (where "&" may be
        // escaped as &), up to the closing quote / whitespace.
        private static readonly Regex GooglePhotoUrl = new(
            @"https://maps\.googleapis\.com/maps/api/place/photo\?[^""\s<>]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Our proxy link for a photo reference.</summary>
        public static string Proxy(string apiBaseUrl, string reference, int maxWidth = 800) =>
            $"{apiBaseUrl}{ProxyPath}?photo_reference={Uri.EscapeDataString(reference)}&maxWidth={maxWidth}";

        /// <summary>This API's public base URL (scheme + host), from the request.</summary>
        public static string BaseUrl(HttpRequest request) =>
            $"{request.Scheme}://{request.Host}{request.PathBase}";

        /// <summary>
        /// For storing: a Google photo URL without its key (host-independent;
        /// turned into a proxy link on the way out). Anything else unchanged.
        /// </summary>
        public static string StripKey(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            var parts = Parse(url);
            return parts == null
                ? url
                : $"https://maps.googleapis.com/maps/api/place/photo?maxwidth={parts.Value.Width}&photo_reference={Uri.EscapeDataString(parts.Value.Reference)}";
        }

        /// <summary>True if the text has a Google photo link to rewrite.</summary>
        public static bool Contains(string text) =>
            text != null && text.Contains("maps.googleapis.com/maps/api/place/photo", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Replace every Google photo link in a JSON response with our proxy
        /// link. Links without a usable reference are dropped to "".
        /// </summary>
        public static string RewriteJson(string json, string apiBaseUrl) =>
            GooglePhotoUrl.Replace(json, m =>
            {
                var parts = Parse(m.Value);
                return parts == null ? "" : Proxy(apiBaseUrl, parts.Value.Reference, parts.Value.Width);
            });

        private static (string Reference, int Width)? Parse(string url)
        {
            if (!url.StartsWith("https://maps.googleapis.com/maps/api/place/photo", StringComparison.OrdinalIgnoreCase))
                return null;
            var query = url.Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase);
            var q = query.IndexOf('?');
            if (q < 0) return null;

            string reference = null;
            var width = 800;
            foreach (var pair in query[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var name = pair[..eq];
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                if (name.Equals("photo_reference", StringComparison.OrdinalIgnoreCase) || name.Equals("photoreference", StringComparison.OrdinalIgnoreCase))
                    reference = value;
                else if (name.Equals("maxwidth", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var w))
                    width = Math.Clamp(w, 50, 1600);
            }
            return string.IsNullOrWhiteSpace(reference) ? null : (reference, width);
        }
    }
}
