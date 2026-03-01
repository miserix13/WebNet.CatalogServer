namespace WebNet.CatalogServer;

public sealed class WindowsTokenAuthorizer : ITokenAuthorizer
{
    private readonly HashSet<string> allowedSubjects;
    private readonly IReadOnlyDictionary<CommandKind, HashSet<string>> commandRolePolicy;

    public WindowsTokenAuthorizer(LiteGraphAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.allowedSubjects = options.AllowedWindowsSubjects
            .Select(subject => subject.Trim())
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        this.commandRolePolicy = options.CommandRolePolicy
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(role => role.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal));
    }

    public bool Authorize(SecurityContext securityContext, CommandKind command)
    {
        if (!IsSubjectAllowed(securityContext.Subject))
        {
            return false;
        }

        var roles = ParseRoles(securityContext.Roles);
        if (roles.Count == 0)
        {
            return false;
        }

        if (!this.commandRolePolicy.TryGetValue(command, out var allowedRoles) || allowedRoles.Count == 0)
        {
            return false;
        }

        return roles.Any(role => allowedRoles.Contains(role));
    }

    private bool IsSubjectAllowed(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        if (this.allowedSubjects.Count == 0)
        {
            return true;
        }

        return this.allowedSubjects.Contains(subject.Trim());
    }

    private static HashSet<string> ParseRoles(IReadOnlyCollection<string>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return [];
        }

        return roles
            .SelectMany(role => role.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(role => role.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }
}