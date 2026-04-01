using Microsoft.AspNetCore.Identity;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.DAL.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName       { get; set; } = string.Empty;
    public string LastName        { get; set; } = string.Empty;
    public string? PreferredCulture { get; set; } = "sv-SE";
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public ICollection<BudgetMembership> Memberships { get; set; } = new List<BudgetMembership>();
}
