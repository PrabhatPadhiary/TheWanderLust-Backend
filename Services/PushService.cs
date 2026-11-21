using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Sends push notifications through Expo's push service, which delivers to
    /// both Android (FCM) and iPhone (APNs) from one API. Free, no key needed.
    /// Phones Expo says are gone (app uninstalled, token expired) are removed.
    /// Never throws: a failed push is logged and the caller carries on.
    /// </summary>
    public class PushService
    {
        private const string SendUrl = "https://exp.host/--/api/v2/push/send";
        private const int BatchSize = 100; // Expo's limit per request

        /// <summary>Android channel the app creates (see the app's notifications.js).</summary>
        public const string TripUpdatesChannel = "trip-updates";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<PushService> _logger;

        public PushService(HttpClient http, AppDbContext dbContext, ILogger<PushService> logger)
        {
            _http = http;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>Send one notification to every phone of these users.</summary>
        public async Task SendToUsersAsync(IEnumerable<Guid> userIds, string title, string body, object? data = null)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;

            var tokens = await _dbContext.PushTokens
                .Where(t => ids.Contains(t.UserId))
                .Select(t => t.Token)
                .ToListAsync();
            if (tokens.Count == 0) return;

            var messages = tokens.Select(token => new ExpoMessage
            {
                To = token,
                Title = title,
                Body = body,
                Data = data,
                Sound = "default",
                ChannelId = TripUpdatesChannel,
                Priority = "high",
            }).ToList();

            var gone = new List<string>();
            foreach (var batch in messages.Chunk(BatchSize))
            {
                try
                {
                    using var response = await _http.PostAsJsonAsync(SendUrl, batch, Json);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Push: Expo answered {Status}: {Body}",
                            (int)response.StatusCode, await response.Content.ReadAsStringAsync());
                        continue;
                    }

                    // One ticket per message, in the same order.
                    var result = await response.Content.ReadFromJsonAsync<ExpoSendResponse>(Json);
                    var tickets = result?.Data ?? new List<ExpoTicket>();
                    for (var i = 0; i < tickets.Count && i < batch.Length; i++)
                    {
                        var ticket = tickets[i];
                        if (ticket.Status == "ok") continue;
                        if (ticket.Details?.Error == "DeviceNotRegistered")
                            gone.Add(batch[i].To);
                        else
                            _logger.LogWarning("Push: not sent ({Error}): {Message}", ticket.Details?.Error, ticket.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push: sending to Expo failed");
                }
            }

            if (gone.Count > 0)
            {
                await _dbContext.PushTokens.Where(t => gone.Contains(t.Token)).ExecuteDeleteAsync();
                _logger.LogInformation("Push: removed {Count} phone(s) that no longer receive notifications", gone.Count);
            }
        }

        private class ExpoMessage
        {
            public string To { get; set; } = "";
            public string? Title { get; set; }
            public string? Body { get; set; }
            public object? Data { get; set; }
            public string? Sound { get; set; }
            public string? ChannelId { get; set; }
            public string? Priority { get; set; }
        }

        private class ExpoSendResponse
        {
            public List<ExpoTicket>? Data { get; set; }
        }

        private class ExpoTicket
        {
            public string? Status { get; set; }
            public string? Message { get; set; }
            public ExpoTicketDetails? Details { get; set; }
        }

        private class ExpoTicketDetails
        {
            public string? Error { get; set; }
        }
    }
}
