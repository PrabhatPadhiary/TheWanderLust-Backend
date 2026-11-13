using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Live
{
    /// <summary>
    /// After any successful change to a trip (POST/PUT/PATCH/DELETE on the trip
    /// controllers), tells everyone viewing that trip, e.g.
    ///   { section: "spots", action: "added", actorName: "Satish", itemName: "Burj Khalifa" }
    /// so their trip screen refreshes and shows "Satish added Burj Khalifa".
    ///
    /// Registered globally; only the controllers in <see cref="Sections"/> are
    /// watched, so new endpoints on them are covered without extra code. A failed
    /// broadcast is logged and never fails the request.
    /// </summary>
    public class TripLiveUpdatesFilter : IAsyncActionFilter
    {
        // Controller → the part of the trip screen it changes.
        private static readonly Dictionary<string, string> Sections = new()
        {
            ["Trips"] = "details",
            ["TripPlaces"] = "spots",
            ["Itinerary"] = "itinerary",
            ["TripExpenses"] = "budget",
            ["TripChecklist"] = "checklist",
            ["TripMembers"] = "travellers",
            ["Invitations"] = "travellers",
        };

        // Changes nobody else needs to see (creating a trip, managing invite links).
        private static readonly HashSet<string> Ignored = new()
        {
            "Trips.Create",
            "Invitations.Create",
            "Invitations.Revoke",
            "Invitations.ResetShareLink",
        };

        // Where the item's name lives on the returned object, in order of preference.
        private static readonly string[] NameProperties = { "PlaceName", "Title", "Name", "Text" };

        private readonly AppDbContext _dbContext;
        private readonly IHubContext<TripHub> _hub;
        private readonly ILogger<TripLiveUpdatesFilter> _logger;

        public TripLiveUpdatesFilter(AppDbContext dbContext, IHubContext<TripHub> hub, ILogger<TripLiveUpdatesFilter> logger)
        {
            _dbContext = dbContext;
            _hub = hub;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
            var controller = descriptor?.ControllerName;
            var actionName = descriptor?.ActionName;
            if (HttpMethods.IsGet(context.HttpContext.Request.Method)
                || controller == null
                || !Sections.ContainsKey(controller)
                || Ignored.Contains($"{controller}.{actionName}"))
            {
                await next();
                return;
            }

            // Look the trip up first: after a delete the row may be gone.
            Guid? tripId = null;
            try
            {
                tripId = await ResolveTripId(context, controller);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live updates: couldn't resolve the trip for {Controller}.{Action}", controller, actionName);
            }

            // Role changes and removals name the person they're about (looked up
            // before the action: after a removal their membership is gone).
            (Guid Id, string Name)? target = null;
            if (controller == "TripMembers"
                && context.RouteData.Values.TryGetValue("memberId", out var memberRoute)
                && Guid.TryParse(memberRoute?.ToString(), out var memberId))
            {
                var user = await _dbContext.Users
                    .Where(u => u.Id == memberId)
                    .Select(u => new { u.Id, u.Name })
                    .FirstOrDefaultAsync();
                if (user != null) target = (user.Id, user.Name);
            }

            var executed = await next();
            if (tripId == null || executed.Exception != null && !executed.ExceptionHandled)
                return;

            var status = executed.Result is IStatusCodeActionResult s ? s.StatusCode ?? 200 : 200;
            if (status < 200 || status >= 300)
                return;

            try
            {
                var (section, change) = Describe(controller, actionName!, context.HttpContext.Request.Method);
                var firebaseUid = context.HttpContext.User.FindFirst("firebase_uid")?.Value;
                var actor = firebaseUid == null
                    ? null
                    : await _dbContext.Users
                        .Where(u => u.FirebaseId == firebaseUid)
                        .Select(u => new { u.Id, u.Name })
                        .FirstOrDefaultAsync();

                await _hub.Clients.Group(TripHub.GroupFor(tripId.Value)).SendAsync("tripChanged", new
                {
                    tripId = tripId.Value,
                    section,
                    action = change,
                    actorId = actor?.Id,
                    actorName = actor?.Name,
                    itemName = section == "details" ? null : ItemName(executed.Result),
                    targetId = target?.Id,
                    targetName = target?.Name,
                    role = StringProperty(executed.Result, "Role"),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live updates: broadcast failed for trip {TripId}", tripId);
            }
        }

        private async Task<Guid?> ResolveTripId(ActionExecutingContext context, string controller)
        {
            var route = context.RouteData.Values;
            Guid? FromRoute(string key) =>
                route.TryGetValue(key, out var v) && Guid.TryParse(v?.ToString(), out var g) ? g : null;

            if (FromRoute("tripId") is Guid tripId)
                return tripId;

            switch (controller)
            {
                case "Trips":
                    return FromRoute("id");
                case "TripPlaces" when FromRoute("id") is Guid placeId: // DELETE /api/trip-places/{id}
                    return await _dbContext.TripPlaces
                        .Where(p => p.Id == placeId)
                        .Select(p => (Guid?)p.TripDestination.TripId)
                        .FirstOrDefaultAsync();
                case "Invitations" when FromRoute("inviteId") is Guid inviteId: // POST /api/join/{inviteId}
                    return await _dbContext.Invitations
                        .Where(i => i.Id == inviteId)
                        .Select(i => (Guid?)i.TripId)
                        .FirstOrDefaultAsync();
                default:
                    return null;
            }
        }

        /// <summary>Which part of the trip changed, and how ("added", "removed", "updated", "joined", "deleted").</summary>
        private static (string Section, string Change) Describe(string controller, string action, string method)
        {
            var change = method switch
            {
                "POST" => "added",
                "DELETE" => "removed",
                _ => "updated",
            };

            return (controller, action) switch
            {
                ("Invitations", "Join") => ("travellers", "joined"),
                ("Trips", "Delete") => ("trip", "deleted"),
                ("Trips", "SetBudget") => ("budget", "updated"),
                ("Trips", "AddDestination" or "DeleteDestination" or "UpdateDestinationDates"
                    or "ReorderDestinations" or "SetPrimaryDestination") => ("stops", change),
                _ => (Sections[controller], change),
            };
        }

        private static string? StringProperty(IActionResult? result, string name) =>
            (result as ObjectResult)?.Value?.GetType().GetProperty(name)?.GetValue((result as ObjectResult)!.Value) as string;

        /// <summary>The changed item's name from the response, e.g. "Burj Khalifa" (deletes have none).</summary>
        private static string? ItemName(IActionResult? result)
        {
            var value = (result as ObjectResult)?.Value;
            if (value == null) return null;
            foreach (var name in NameProperties)
            {
                if (value.GetType().GetProperty(name)?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            return null;
        }
    }
}
