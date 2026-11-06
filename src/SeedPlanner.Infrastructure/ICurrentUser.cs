namespace SeedPlanner.Infrastructure;

/// <summary>
/// The user on whose behalf the current request runs. UserId is null if nobody is signed in.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}
