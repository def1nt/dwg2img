using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using dwg2img.Data;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Usage of System.Drawing and System.DirectoryServices requires application to be run on Windows");

var builder = WebApplication.CreateBuilder(args);

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

app.Run();
