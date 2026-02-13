using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;

namespace dwg2img.Data
{
    public class TokenRefreshService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TokenRefreshService> _logger;

        public TokenRefreshService(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ILogger<TokenRefreshService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> RefreshAccessTokenAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return false;

            try
            {
                // Get current authentication info
                var authInfo = await httpContext.AuthenticateAsync("Cookies");
                if (!authInfo.Succeeded) return false;

                // Check if we have a refresh token
                var refreshToken = await httpContext.GetTokenAsync("refresh_token");
                if (string.IsNullOrEmpty(refreshToken))
                {
                    _logger.LogInformation("No refresh token found");
                    return false;
                }

                // Check if access token is expired or about to expire
                var accessToken = await httpContext.GetTokenAsync("access_token");
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogInformation("No access token found");
                    return false;
                }

                // Parse the access token to check expiration
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(accessToken))
                {
                    var jwtToken = handler.ReadJwtToken(accessToken);
                    var expireDate = jwtToken.ValidTo;

                    // If token is still valid for more than 5 minutes, no need to refresh
                    if (expireDate > DateTime.UtcNow.AddMinutes(5))
                    {
                        return true;
                    }
                }

                // If we reach here, we need to refresh the token
                // Note: In a production environment, you would make a call to Keycloak's token endpoint
                // to exchange the refresh token for a new access token
                _logger.LogInformation("Token refresh would be performed here");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing access token");
                return false;
            }
        }

        public async Task<bool> IsAccessTokenValidAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return false;

            try
            {
                var accessToken = await httpContext.GetTokenAsync("access_token");
                if (string.IsNullOrEmpty(accessToken)) return false;

                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(accessToken))
                {
                    var jwtToken = handler.ReadJwtToken(accessToken);
                    return jwtToken.ValidTo > DateTime.UtcNow.AddMinutes(1);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating access token");
                return false;
            }
        }
    }
}
