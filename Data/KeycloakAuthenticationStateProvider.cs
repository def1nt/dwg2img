using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace dwg2img.Data;

public class KeycloakAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public KeycloakAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var identity = httpContext.User.Identity;
            var claims = new List<Claim>();

            // Add the name claim if it exists
            if (!string.IsNullOrEmpty(identity.Name))
            {
                claims.Add(new Claim(ClaimTypes.Name, identity.Name));
            }

            // Add all existing claims from the user
            claims.AddRange(httpContext.User.Claims);

            // If there's no name claim but we have a preferred_username claim, use that
            if (!claims.Any(c => c.Type == ClaimTypes.Name) &&
                claims.FirstOrDefault(c => c.Type == "preferred_username") is Claim preferredUsernameClaim)
            {
                claims.Add(new Claim(ClaimTypes.Name, preferredUsernameClaim.Value));
            }

            var claimsIdentity = new ClaimsIdentity(claims, "Keycloak");
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            return Task.FromResult(new AuthenticationState(claimsPrincipal));
        }
        else
        {
            var anonymousIdentity = new ClaimsIdentity();
            var anonymousPrincipal = new ClaimsPrincipal(anonymousIdentity);

            return Task.FromResult(new AuthenticationState(anonymousPrincipal));
        }
    }

    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
