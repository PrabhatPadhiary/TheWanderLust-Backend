using System.Text;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Middleware
{
    /// <summary>
    /// Safety net for the Google API key: rewrites any Google place photo link
    /// in a JSON response (which would carry the key, or need it) into a link
    /// to our photo proxy. Covers links stored before the key was stripped
    /// (trip places, favourites, cover photos) on every endpoint.
    ///
    /// Only JSON responses that actually contain such a link are touched; the
    /// photo endpoints themselves (images) are skipped.
    /// </summary>
    public class PhotoLinkRewriteMiddleware
    {
        private readonly RequestDelegate _next;

        public PhotoLinkRewriteMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/photo", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/place-photo", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await _next(context);

                buffer.Position = 0;
                var isJson = context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
                if (isJson && buffer.Length > 0)
                {
                    var text = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                    if (PlacePhotoLinks.Contains(text))
                    {
                        var rewritten = PlacePhotoLinks.RewriteJson(text, PlacePhotoLinks.BaseUrl(context.Request));
                        var bytes = Encoding.UTF8.GetBytes(rewritten);
                        context.Response.ContentLength = bytes.Length;
                        context.Response.Body = original;
                        await original.WriteAsync(bytes, context.RequestAborted);
                        return;
                    }
                    buffer.Position = 0;
                }

                context.Response.Body = original;
                await buffer.CopyToAsync(original, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = original;
            }
        }
    }
}
