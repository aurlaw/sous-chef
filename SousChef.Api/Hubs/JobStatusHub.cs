using Microsoft.AspNetCore.SignalR;

namespace SousChef.Api.Hubs;

public class JobStatusHub : Hub
{
    // Clients connect and receive broadcasts for all job status changes.
    // No client-to-server methods needed for Phase 3a.
}
