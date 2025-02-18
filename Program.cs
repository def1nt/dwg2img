using Microsoft.AspNetCore.Components.Authorization;
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
        builder.Services.AddScoped<ADUserInfoService>();
        builder.Services.AddScoped<LoadImageService>();
        builder.Services.AddScoped<WebsiteAuthenticator>();
        builder.Services.AddScoped<AuthenticationStateProvider, WebsiteAuthenticator>();
        builder.Services.AddHttpContextAccessor();
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

        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        await app.RunAsync(cancellationToken);
    }
}
