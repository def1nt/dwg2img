using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace dwg2img.Controllers
{
    [Route("[controller]")]
    public class AuthController : Controller
    {
        [HttpGet("login")]
        public async Task<IActionResult> Login(string? redirectUri = "/")
        {
            // Store the redirect URI in TempData or session
            if (!string.IsNullOrEmpty(redirectUri))
            {
                TempData["RedirectUri"] = redirectUri;
            }

            // Challenge the user to authenticate with OIDC
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = redirectUri ?? "/"
            }, "oidc");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            // Sign out of both cookie and OIDC authentication
            await HttpContext.SignOutAsync("Cookies");
            await HttpContext.SignOutAsync("oidc", new AuthenticationProperties
            {
                RedirectUri = "/"
            });

            return Redirect("/");
        }
    }
}
