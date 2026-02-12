using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace dwg2img.Controllers
{
    [Route("[controller]")]
    public class AuthController : Controller
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        [HttpGet("login")]
        public async Task<IActionResult> Login(string? redirectUri = "/")
        {
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

        [HttpGet("signin-oidc")]
        public async Task<IActionResult> SigninOidc()
        {
            // Handle the OIDC callback
            // The OpenID Connect middleware will process the response automatically
            // This endpoint is primarily used as a callback URL
            return Ok();
        }
    }
}
