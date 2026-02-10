using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace dwg2img.Data;

public class KeycloakAuthenticationService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public KeycloakAuthenticationService(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider;
    }

    public async Task<string> GetCurrentUserAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.Name ?? "None";
    }

    public async Task<bool> IsUserInRoleAsync(string role)
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User.IsInRole(role);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.IsAuthenticated ?? false;
    }

    public async Task<ClaimsPrincipal> GetCurrentUserPrincipalAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User;
    }
}
