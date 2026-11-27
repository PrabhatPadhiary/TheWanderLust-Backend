#nullable enable
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// One tour's details for the details page: Viator's product content
    /// (/products/{code}) plus the names of its stops and meeting point
    /// (/locations/bulk). Kept for a day.
    /// </summary>
    public partial class ViatorService
    {
        private static readonly TimeSpan DetailTtl = TimeSpan.FromHours(24);

        /// <summary>
        /// The tour's details; null when Viator has no such tour; Failed when
        /// Viator couldn't be reached.
        /// </summary>
        public async Task<(ActivityDetailDto? Detail, bool Failed)> DetailAsync(string code, CancellationToken ct)
        {
            if (!Enabled) return (null, false);
            var key = $"viator_detail_{code}";
            if (_cache.TryGetValue(key, out ActivityDetailDto? cached) && cached != null) return (cached, false);

            var (doc, status) = await GetDocumentAsync($"/products/{Uri.EscapeDataString(code)}", ct);
            if (doc == null) return (null, status != 400 && status != 404);
            using (doc)
            {
                var p = doc.RootElement;
                if (Str(p, "status") is string st && st != "ACTIVE") return (null, false);
                var detail = MapDetail(p);
                if (detail == null) return (null, false);

                // Names for stops and meeting points (best effort).
                var refs = LocationRefs(p).Distinct().Take(50).ToList();
                var places = refs.Count > 0 ? await LocationsAsync(refs, ct) : new();
                FillLocations(p, detail, places);

                _cache.Set(key, detail, DetailTtl);
                return (detail, false);
            }
        }

        // ── Mapping ──────────────────────────────────────────────────────────

        private ActivityDetailDto? MapDetail(JsonElement p)
        {
            var code = Str(p, "productCode");
            var title = Str(p, "title");
            var url = Str(p, "productUrl");
            if (code == null || title == null || url == null) return null;

            var d = new ActivityDetailDto
            {
                Id = code,
                Title = title.Trim(),
                Description = Str(p, "description")?.Trim(),
                BookingUrl = url, // kept exactly as Viator sends it (affiliate tracking)
            };

            // Photos: each image's variant closest to 720 px wide.
            if (p.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (!img.TryGetProperty("variants", out var vs) || vs.ValueKind != JsonValueKind.Array) continue;
                    var best = vs.EnumerateArray()
                        .Select(v => (url: Str(v, "url"), w: Int(v, "width") ?? 0))
                        .Where(v => v.url != null)
                        .OrderBy(v => Math.Abs(v.w - 720))
                        .FirstOrDefault();
                    if (best.url != null) d.Images.Add(best.url);
                    if (d.Images.Count >= 15) break;
                }
            }

            if (p.TryGetProperty("reviews", out var rv) && rv.ValueKind == JsonValueKind.Object)
            {
                d.ReviewCount = Int(rv, "totalReviews") ?? 0;
                if (d.ReviewCount > 0 && rv.TryGetProperty("combinedAverageRating", out var r) && r.TryGetDouble(out var rating))
                    d.Rating = Math.Round(rating, 1);
            }

            if (p.TryGetProperty("itinerary", out var it) && it.ValueKind == JsonValueKind.Object)
            {
                d.PrivateTour = Bool(it, "privateTour");
                d.SkipTheLine = Bool(it, "skipTheLine");
                d.MaxGroupSize = Int(it, "maxTravelersInSharedTour");
                if (it.TryGetProperty("duration", out var du))
                {
                    var fixedMin = Int(du, "fixedDurationInMinutes");
                    d.DurationMinutes = fixedMin ?? Int(du, "variableDurationFromMinutes");
                    d.DurationToMinutes = fixedMin == null ? Int(du, "variableDurationToMinutes") : null;
                }
                MapItinerary(it, d);
            }

            if (p.TryGetProperty("cancellationPolicy", out var cp) && cp.ValueKind == JsonValueKind.Object)
            {
                d.CancellationPolicy = Str(cp, "description");
                // Viator's "standard" policy is a full refund up to 24 hours before.
                d.FreeCancellation = Str(cp, "type") == "STANDARD" ||
                    (cp.TryGetProperty("refundEligibility", out var re) && re.ValueKind == JsonValueKind.Array &&
                     re.EnumerateArray().Any(x => Int(x, "percentageRefundable") == 100 && (Int(x, "dayRangeMin") ?? 99) <= 1));
            }

            if (p.TryGetProperty("bookingConfirmationSettings", out var bc))
                d.InstantConfirmation = Str(bc, "confirmationType") == "INSTANT";

            d.Inclusions = TextList(p, "inclusions");
            d.Exclusions = TextList(p, "exclusions");
            if (p.TryGetProperty("additionalInfo", out var ai) && ai.ValueKind == JsonValueKind.Array)
                d.GoodToKnow = ai.EnumerateArray().Select(x => Str(x, "description")).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct().ToList();

            if (p.TryGetProperty("languageGuides", out var lg) && lg.ValueKind == JsonValueKind.Array)
                d.Languages = lg.EnumerateArray().Select(x => LanguageName(Str(x, "language"))).Where(s => s != null).Select(s => s!).Distinct().ToList();

            if (p.TryGetProperty("pricingInfo", out var pi))
                d.PricePer = Str(pi, "type") switch { "PER_PERSON" => "person", "UNIT" => "group", _ => null };

            if (p.TryGetProperty("ticketInfo", out var ti))
                d.TicketInfo = Str(ti, "ticketTypeDescription");

            if (p.TryGetProperty("logistics", out var lo) && lo.ValueKind == JsonValueKind.Object)
            {
                d.MeetingPoint = FirstPoint(lo, "start");
                d.EndPoint = FirstPoint(lo, "end");
                if (lo.TryGetProperty("travelerPickup", out var pu) && pu.ValueKind == JsonValueKind.Object)
                {
                    d.Pickup = Str(pu, "pickupOptionType") switch
                    {
                        "PICKUP_EVERYONE" => "Pickup included",
                        "PICKUP_AND_MEET_AT_START_POINT" => "Pickup offered, or meet at the start",
                        _ => null,
                    };
                    d.PickupNote = Str(pu, "additionalInfo")?.Trim();
                }
            }
            return d;
        }

        private static void MapItinerary(JsonElement it, ActivityDetailDto d)
        {
            switch (Str(it, "itineraryType"))
            {
                case "STANDARD":
                    if (it.TryGetProperty("itineraryItems", out var items) && items.ValueKind == JsonValueKind.Array)
                        foreach (var x in items.EnumerateArray()) d.Stops.Add(Stop(x, null));
                    break;
                case "MULTI_DAY_TOUR":
                    if (it.TryGetProperty("days", out var days) && days.ValueKind == JsonValueKind.Array)
                        foreach (var day in days.EnumerateArray())
                        {
                            var label = Int(day, "dayNumber") is int n ? $"Day {n}" : null;
                            var dayTitle = Str(day, "title");
                            if (day.TryGetProperty("items", out var di) && di.ValueKind == JsonValueKind.Array && di.GetArrayLength() > 0)
                                foreach (var x in di.EnumerateArray()) d.Stops.Add(Stop(x, label));
                            else if (dayTitle != null)
                                d.Stops.Add(new ActivityStopDto { Day = label, Description = dayTitle });
                        }
                    break;
                case "ACTIVITY":
                    if (it.TryGetProperty("pointsOfInterest", out var pois) && pois.ValueKind == JsonValueKind.Array)
                        foreach (var x in pois.EnumerateArray())
                            d.Stops.Add(new ActivityStopDto { Name = Ref(x) }); // ref replaced by a name later
                    var info = it.TryGetProperty("activityInfo", out var a) ? Str(a, "description") : null;
                    if (info != null) d.ItineraryText = info.Trim();
                    break;
                case "UNSTRUCTURED":
                    d.ItineraryText = Str(it, "unstructuredDescription")?.Trim();
                    break;
            }
            d.Stops.RemoveAll(s => s.Name == null && string.IsNullOrWhiteSpace(s.Description));
            if (d.Stops.Count > 40) d.Stops = d.Stops.Take(40).ToList();
        }

        private static ActivityStopDto Stop(JsonElement x, string? day)
        {
            var poi = x.TryGetProperty("pointOfInterestLocation", out var pl) ? pl : default;
            bool? admission = Str(x, "admissionIncluded") switch { "YES" => true, "NO" => false, _ => null };
            return new ActivityStopDto
            {
                Name = poi.ValueKind == JsonValueKind.Object ? Ref(poi) : null, // a location ref until filled in
                Description = Str(x, "description")?.Trim(),
                DurationMinutes = x.TryGetProperty("duration", out var du) ? Int(du, "fixedDurationInMinutes") ?? Int(du, "variableDurationFromMinutes") : null,
                Day = day,
                PassBy = Bool(x, "passByWithoutStopping"),
                AdmissionIncluded = admission,
            };
        }

        private static ActivityPointDto? FirstPoint(JsonElement logistics, string prop)
        {
            if (!logistics.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var first = arr.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return null;
            var point = new ActivityPointDto { Name = Ref(first), Description = Str(first, "description")?.Trim() };
            return point.Name == null && point.Description == null ? null : point;
        }

        /// <summary>{"location":{"ref":"LOC-…"}} → "LOC-…" (other refs like CONTACT_SUPPLIER_LATER are dropped).</summary>
        private static string? Ref(JsonElement withLocation)
        {
            if (!withLocation.TryGetProperty("location", out var loc)) return null;
            var r = Str(loc, "ref");
            return r != null && r.StartsWith("LOC-") ? r : null;
        }

        private static IEnumerable<string> LocationRefs(JsonElement p)
        {
            var found = new List<string>();
            void Walk(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in e.EnumerateObject())
                    {
                        if (prop.Name == "ref" && prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is string s && s.StartsWith("LOC-"))
                            found.Add(s);
                        else Walk(prop.Value);
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                    foreach (var x in e.EnumerateArray()) Walk(x);
            }
            if (p.TryGetProperty("itinerary", out var it)) Walk(it);
            if (p.TryGetProperty("logistics", out var lo))
            {
                if (lo.TryGetProperty("start", out var s)) Walk(s);
                if (lo.TryGetProperty("end", out var e)) Walk(e);
            }
            return found;
        }

        /// <summary>Replace location refs with names; drop refs Viator couldn't name.</summary>
        private static void FillLocations(JsonElement p, ActivityDetailDto d, Dictionary<string, ActivityPointDto> places)
        {
            foreach (var s in d.Stops)
                s.Name = s.Name != null && places.TryGetValue(s.Name, out var pl) ? pl.Name : null;
            d.Stops.RemoveAll(s => s.Name == null && string.IsNullOrWhiteSpace(s.Description));

            ActivityPointDto? Fill(ActivityPointDto? point)
            {
                if (point == null) return null;
                if (point.Name != null && places.TryGetValue(point.Name, out var pl))
                {
                    point.Name = pl.Name;
                    point.Address = pl.Address;
                    point.Latitude = pl.Latitude;
                    point.Longitude = pl.Longitude;
                }
                else point.Name = null;
                return point.Name == null && point.Description == null ? null : point;
            }
            d.MeetingPoint = Fill(d.MeetingPoint);
            d.EndPoint = Fill(d.EndPoint);
            // Ends where it starts: say it once.
            if (d.EndPoint != null && d.MeetingPoint != null && d.EndPoint.Name == d.MeetingPoint.Name && d.EndPoint.Description == d.MeetingPoint.Description)
                d.EndPoint = null;
        }

        private async Task<Dictionary<string, ActivityPointDto>> LocationsAsync(List<string> refs, CancellationToken ct)
        {
            var result = new Dictionary<string, ActivityPointDto>();
            var (doc, _) = await PostDocumentAsync("/locations/bulk", new { locations = refs }, ct);
            if (doc == null) return result;
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("locations", out var locs) || locs.ValueKind != JsonValueKind.Array) return result;
                foreach (var l in locs.EnumerateArray())
                {
                    var r = Str(l, "reference");
                    var name = Str(l, "name");
                    if (r == null || string.IsNullOrWhiteSpace(name)) continue;
                    var addr = l.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object
                        ? string.Join(", ", new[] { Str(a, "street"), Str(a, "administrativeArea"), Str(a, "country") }
                            .Where(x => !string.IsNullOrWhiteSpace(x) && x != name).Distinct())
                        : null;
                    double? lat = null, lng = null;
                    if (l.TryGetProperty("center", out var c) && c.ValueKind == JsonValueKind.Object)
                    {
                        if (c.TryGetProperty("latitude", out var la) && la.TryGetDouble(out var v1)) lat = v1;
                        if (c.TryGetProperty("longitude", out var lo) && lo.TryGetDouble(out var v2)) lng = v2;
                    }
                    result[r] = new ActivityPointDto { Name = name.Trim(), Address = string.IsNullOrWhiteSpace(addr) ? null : addr, Latitude = lat, Longitude = lng };
                }
            }
            return result;
        }

        // ── Raw calls (JSON documents) ───────────────────────────────────────

        private async Task<(JsonDocument? Doc, int Status)> GetDocumentAsync(string path, CancellationToken ct) =>
            await SendDocumentAsync(Request(HttpMethod.Get, path), path, ct);

        private async Task<(JsonDocument? Doc, int Status)> PostDocumentAsync(string path, object body, CancellationToken ct)
        {
            var req = Request(HttpMethod.Post, path);
            req.Content = JsonContent.Create(body);
            return await SendDocumentAsync(req, path, ct);
        }

        /// <summary>Status 0 = network error or timeout.</summary>
        private async Task<(JsonDocument? Doc, int Status)> SendDocumentAsync(HttpRequestMessage req, string path, CancellationToken ct)
        {
            if (_cache.TryGetValue(DownKey, out _)) return (null, 0);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(SearchTimeout);
            try
            {
                using (req)
                using (var res = await _http.SendAsync(req, limit.Token))
                {
                    var status = (int)res.StatusCode;
                    if (!res.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Viator {Path} returned {Status}", path, status);
                        if (status == 429 || status >= 500) _cache.Set(DownKey, true, FailureTtl);
                        return (null, status);
                    }
                    await using var stream = await res.Content.ReadAsStreamAsync(limit.Token);
                    return (await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token), status);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Viator {Path} failed: {Error}", path, ex.Message);
                _cache.Set(DownKey, true, FailureTtl);
                return (null, 0);
            }
        }

        // ── JSON helpers ─────────────────────────────────────────────────────

        private static string? Str(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? (string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString())
                : null;

        private static int? Int(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

        private static bool Bool(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

        /// <summary>Inclusions / exclusions: the supplier's text, else Viator's label.</summary>
        private static List<string> TextList(JsonElement p, string prop)
        {
            if (!p.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
            return arr.EnumerateArray()
                .Select(x => Str(x, "description") ?? Str(x, "otherDescription") ?? Str(x, "typeDescription"))
                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "Other")
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>"en" → "English", "zh-CN" → "Chinese (China)".</summary>
        // Common ones by hand: the server may run without full culture data.
        private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English", ["hi"] = "Hindi", ["es"] = "Spanish", ["fr"] = "French", ["de"] = "German",
            ["it"] = "Italian", ["pt"] = "Portuguese", ["ja"] = "Japanese", ["zh"] = "Chinese", ["ko"] = "Korean",
            ["ru"] = "Russian", ["ar"] = "Arabic", ["nl"] = "Dutch", ["th"] = "Thai", ["id"] = "Indonesian",
            ["tr"] = "Turkish", ["vi"] = "Vietnamese", ["ms"] = "Malay", ["he"] = "Hebrew", ["el"] = "Greek",
        };

        private static string? LanguageName(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            if (Languages.TryGetValue(code, out var known)) return known;
            if (Languages.TryGetValue(code.Split('-', '_')[0], out var basic)) return basic;
            try
            {
                var name = CultureInfo.GetCultureInfo(code).EnglishName;
                return string.IsNullOrWhiteSpace(name) || name.StartsWith("Unknown") ? code : name;
            }
            catch (CultureNotFoundException)
            {
                return code;
            }
        }
    }
}
