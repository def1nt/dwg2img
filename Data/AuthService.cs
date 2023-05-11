using System.Security.Claims;
using System.DirectoryServices.AccountManagement;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text.Json;
namespace auth.Data;

public class WebsiteAuthenticator : AuthenticationStateProvider
{
    private string DomainName = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain().Name;

    private readonly ProtectedLocalStorage _protectedLocalStorage;
    private readonly ILogger<WebsiteAuthenticator> _logger;
    private readonly ADUserInfoService _adUserInfoService;

    public WebsiteAuthenticator(ProtectedLocalStorage protectedLocalStorage, ILogger<WebsiteAuthenticator> logger, ADUserInfoService adUserInfoService)
    {
        _adUserInfoService = adUserInfoService;
        _protectedLocalStorage = protectedLocalStorage;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = new ClaimsPrincipal();

        try
        {
            var storedPrincipal = await _protectedLocalStorage.GetAsync<string>("identity");

            if (storedPrincipal.Success)
            {
                var user = JsonSerializer.Deserialize<User>(storedPrincipal.Value!);
                var (_, isLookUpSuccess) = LookUpUser(user!.Username, user.Password);

                if (isLookUpSuccess)
                {
                    var identity = CreateIdentityFromUser(user);
                    principal = new(identity);
                }
            }
        }
        catch { }

        return new AuthenticationState(principal);
    }

    public async Task<bool> LoginAsync(string Username, string Password)
    {
        var (userInDatabase, isSuccess) = LookUpUser(Username, Password);
        var principal = new ClaimsPrincipal();

        if (isSuccess)
        {
            var identity = CreateIdentityFromUser(userInDatabase!);
            principal = new ClaimsPrincipal(identity);
            await _protectedLocalStorage.SetAsync("identity", JsonSerializer.Serialize(userInDatabase));
            _logger.LogInformation($"Login attempt for '{Username}' was successful");
        }
        else
        {
            _logger.LogWarning($"Login attempt for '{Username}' was unsuccessful");
        }

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
        return isSuccess;
    }

    public async Task LogoutAsync()
    {
        await _protectedLocalStorage.DeleteAsync("identity");
        var principal = new ClaimsPrincipal();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
    }

    private static ClaimsIdentity CreateIdentityFromUser(User user)
    {
        return new ClaimsIdentity(new Claim[]
        {
            new (ClaimTypes.Name, user.Username),
            new (ClaimTypes.Hash, user.Password)
        }, "WebsiteAuthenticator");
    }

    public (User?, bool) LookUpUser(string username, string password)
    {
        if (!OperatingSystem.IsWindows()) return (null, false);
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return (null, false);
        if (!IsUserAvailable(username)) return (null, false);

        // using AD to lookup user
        var context = new PrincipalContext(ContextType.Domain, DomainName);
        var user = UserPrincipal.FindByIdentity(context, username);
        if (user != null)
        {
            var isValid = context.ValidateCredentials(username, password);
            if (isValid)
            {
                return (new User { Username = username, Password = password }, true);
            }
            else _logger.LogWarning($"Invalid credentials for '{username}'");
        }
        else _logger.LogWarning($"User '{username}' not found");

        return (null, false);
    }

    public bool IsUserAvailable(string username)
    {
        return _adUserInfoService.GetUsers().Any(x => x.SamAcountName == username);
    }

    public async Task<string> GetAccountAsync()
    {
        try
        {
            var storedPrincipal = await _protectedLocalStorage.GetAsync<string>("identity");

            if (storedPrincipal.Success)
            {
                var user = JsonSerializer.Deserialize<User>(storedPrincipal.Value!);
                return user!.Username;
            }
        }
        catch
        {
            return "Error!";
        }
        return "None";
    }
}

public class User
{
    public string Name { get; set; } = "John Doe";
    public string Username { get; set; } = "user";
    public string Password { get; set; } = "";
}
