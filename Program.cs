using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using dwg2img.Data;

namespace dwg2img;

static partial class Application
{
    public async static Task Start(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Usage of System.Drawing and System.DirectoryServices requires application to be run on Windows");

        Application.ParseArgs();

        var builder = WebApplication.CreateBuilder();

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
        .AddCookie("Cookies")
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
                    context.Response.Redirect($"{context.Request.Scheme}://{context.Request.Host}/Account/AccessDenied");
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

        // Add authentication middleware
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        await app.RunAsync(cancellationToken);
    }
}
