using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using dwg2img.Data;
using Microsoft.AspNetCore.HttpOverrides;

namespace dwg2img;

static partial class Application
{
    public async static Task Start(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Usage of System.Drawing and System.DirectoryServices requires application to be run on Windows");

        Application.ParseArgs();

        var builder = WebApplication.CreateBuilder();

        // Configure for proxy scenarios
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                     ForwardedHeaders.XForwardedHost |
                                     ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();
        builder.Services.AddControllers();

        // Add Keycloak authentication services
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "oidc";
        })
        .AddCookie("Cookies", options =>
            {
                // Set cookie to persist for a longer duration
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                // Important: This allows the cookie to persist after browser closure
                options.Cookie.MaxAge = TimeSpan.FromDays(30);
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            })
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = builder.Configuration["Keycloak:Authority"];
            options.ClientId = builder.Configuration["Keycloak:ClientId"];
            options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("roles");
            options.Scope.Add("offline_access");

            // Configure callback paths for proxy scenarios
            var redirectHost = builder.Configuration["Redirect:Host"];
            if (!string.IsNullOrEmpty(redirectHost))
            {
                options.CallbackPath = "/signin-oidc";
                options.SignedOutRedirectUri = redirectHost;
            }

            // Configure for proxy scenarios
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (!string.IsNullOrEmpty(redirectHost))
                {
                    context.ProtocolMessage.RedirectUri = $"{redirectHost}/signin-oidc";
                    context.ProtocolMessage.PostLogoutRedirectUri = redirectHost;
                }
                return Task.CompletedTask;
            };

            // Source - https://stackoverflow.com/a/79190782
            // Posted by R10t--, modified by community. See post 'Timeline' for change history
            // Retrieved 2026-02-11, License - CC BY-SA 4.0
            options.Events.OnTokenValidated = async ctx =>
            {
                var token = ctx.TokenEndpointResponse.AccessToken;
                var handler = new JwtSecurityTokenHandler();
                var parsedJwt = handler.ReadJwtToken(token);

                var updatedClaims = parsedJwt.Claims.ToList().Select(c =>
                {
                    return c.Type == "role" ? new Claim(ClaimTypes.Role, c.Value) : c;
                });

                ctx.Principal.AddIdentity(new ClaimsIdentity(updatedClaims));
            };

            // Map Keycloak roles to claims
            // options.TokenValidationParameters = new TokenValidationParameters
            // {
            //     RoleClaimType = "role"
            // };

            // Handle sign-out
            options.Events.OnRemoteFailure = context =>
            {
                if (context.Request.Form["error_description"].FirstOrDefault()?.Contains("Account is disabled") == true)
                {
                    context.HandleResponse();
                    // Get redirect configuration
                    var redirectHost = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Redirect:Host"];

                    // Build the redirect URI with the configured host and protocol
                    var redirectUri = string.IsNullOrEmpty(redirectHost)
                        ? $"{context.Request.Scheme}://{context.Request.Host}/Account/AccessDenied"
                        : $"{redirectHost}/Account/AccessDenied";

                    context.Response.Redirect(redirectUri);
                }
                return Task.CompletedTask;
            };
        });

        // Add authorization policy for SearchQR role
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("SearchQR", policy => policy.RequireRole("SearchQR"));
        });

        // Replace custom authentication state provider with standard one
        builder.Services.AddScoped<LoadImageService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuthenticationStateProvider, KeycloakAuthenticationStateProvider>();
        builder.Services.AddScoped<TokenRefreshService>();
        builder.Services.AddLogging();

        builder.Logging.ClearProviders();
        builder.Logging.AddEventLog(new Microsoft.Extensions.Logging.EventLog.EventLogSettings
        {
            SourceName = "dwg2img",
            LogName = "Application"
        });
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.Urls.Add("http://*:5262");
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.Urls.Add("http://*:80");
        }

        app.UseStaticFiles();
        app.UseRouting();

        // Add forwarded headers middleware for proxy scenarios
        app.UseForwardedHeaders();

        // Add authentication middleware
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        await app.RunAsync(cancellationToken);
    }
}
