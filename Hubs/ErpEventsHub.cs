using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EJCFitnessGym.Hubs
{
    [Authorize]
    public class ErpEventsHub : Hub
    {
        private static readonly string[] KnownRoles = { "Member", "Staff", "Finance", "Admin", "SuperAdmin" };

        public override async Task OnConnectedAsync()
        {
            if (Context.User?.Identity?.IsAuthenticated != true)
            {
                await base.OnConnectedAsync();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, "role:Authenticated");

            var userId = Context.UserIdentifier ?? Context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            }

            var isBackOffice = false;
            foreach (var role in KnownRoles)
            {
                if (Context.User.IsInRole(role))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
                    if (!string.Equals(role, "Member", StringComparison.OrdinalIgnoreCase))
                    {
                        isBackOffice = true;
                    }
                }
            }

            if (isBackOffice)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "role:BackOffice");
            }

            await base.OnConnectedAsync();
        }
    }
}
