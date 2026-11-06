using System.Security.Claims;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Security;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
