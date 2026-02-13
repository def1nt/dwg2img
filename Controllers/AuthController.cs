using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using dwg2img.Data;

namespace dwg2img.Controllers
{
    [Route("[controller]")]
    public class AuthController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly TokenRefreshService _tokenRefreshService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IConfiguration configuration, TokenRefreshService tokenRefreshService, ILogger<AuthController> logger)
        {
            _configuration = configuration;
            _tokenRefreshService = tokenRefreshService;
            _logger = logger;
        }
        [HttpGet("login")]
        public async Task<IActionResult> Login(string? redirectUri = "/")
        {
            await Task.CompletedTask;
            // Get redirect configuration
            var redirectHost = _configuration["Redirect:Host"];

            // Build the redirect URI with the configured host and protocol
            var fullRedirectUri = string.IsNullOrEmpty(redirectHost)
                ? redirectUri ?? "/"
                : $"{redirectHost}{redirectUri ?? "/"}";

            // Store the redirect URI in TempData or session
            if (!string.IsNullOrEmpty(redirectUri))
            {
                TempData["RedirectUri"] = redirectUri;
            }

            // Challenge the user to authenticate with OIDC
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = fullRedirectUri
            }, "oidc");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            // Get redirect configuration
            var redirectHost = _configuration["Redirect:Host"];

            // Build the redirect URI with the configured host and protocol
            var redirectUri = string.IsNullOrEmpty(redirectHost)
                ? "/"
                : $"{redirectHost}/";

            // Sign out of both cookie and OIDC authentication
            await HttpContext.SignOutAsync("Cookies");
            await HttpContext.SignOutAsync("oidc", new AuthenticationProperties
            {
                RedirectUri = redirectUri
            });

            // Redirect to the configured URI
            var fullRedirectUri = string.IsNullOrEmpty(redirectHost)
                ? "/"
                : $"{redirectHost}/";
            return Redirect(fullRedirectUri);
        }

        [HttpGet("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            try
            {
                var result = await _tokenRefreshService.RefreshAccessTokenAsync();
                if (result)
                {
                    return Ok(new { message = "Token refreshed successfully" });
                }
                else
                {
                    return Unauthorized(new { message = "Failed to refresh token" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }
    }
}
