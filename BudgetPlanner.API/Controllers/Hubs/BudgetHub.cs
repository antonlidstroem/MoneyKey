using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BudgetPlanner.API.Hubs;

[Authorize]
public class BudgetHub : Hub
{
    public async Task JoinBudget(int budgetId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(budgetId));
        await Clients.Caller.SendAsync("JoinedBudget", budgetId);
    }

    public async Task LeaveBudget(int budgetId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(budgetId));

    public static string GroupName(int budgetId) => $"budget-{budgetId}";
}

// Single definition of BudgetEvent — referenced by all controllers via using BudgetPlanner.API.Hubs
public record BudgetEvent(string EventType, int BudgetId, int? EntityId, string? UpdatedByEmail, DateTime Timestamp);
