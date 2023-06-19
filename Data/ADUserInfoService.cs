using System.DirectoryServices;

namespace auth.Data;

public class ADUserInfoService
{
    private const string SamAccountNameProperty = "SamAccountName";
    private const string CanonicalNameProperty = "CN";
    private const string MemberOfProperty = "MemberOf";
    private const string AllowedGroup = "CN=SearchQR";

    public List<ADUser> GetUsers()
    {
        List<ADUser> users = new() { };
        if (!OperatingSystem.IsWindows()) return users;

        var domain = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
        using (DirectoryEntry searchRoot = new(@$"LDAP://{domain.Name}"))
        using (DirectorySearcher directorySearcher = new(searchRoot))
        {
            // Set the filter
            directorySearcher.Filter = "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";

            // Set the properties to load.
            directorySearcher.PropertiesToLoad.Add(CanonicalNameProperty);
            directorySearcher.PropertiesToLoad.Add(SamAccountNameProperty);
            directorySearcher.PropertiesToLoad.Add(MemberOfProperty);

            using (SearchResultCollection searchResultCollection = directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResultCollection)
                {
                    // Create new ADUser instance
                    var user = new ADUser();

                    // Set CN if available.
                    if (searchResult.Properties[CanonicalNameProperty].Count > 0)
                        user.CN = searchResult.Properties[CanonicalNameProperty][0].ToString() ?? "Unknown";

                    // Set SAMAccountName if available
                    if (searchResult.Properties[SamAccountNameProperty].Count > 0)
                        user.SamAcountName = searchResult.Properties[SamAccountNameProperty][0].ToString() ?? "Unknown";

                    // Set MemberOf if available
                    if (searchResult.Properties[MemberOfProperty].Count > 0)
                        user.MemberOf = searchResult.Properties[MemberOfProperty][0]?.ToString()?.Split(',') ?? Array.Empty<string>();

                    // Add user to users list.
                    users.Add(user);
                }
            }
        }

        // return users;
        return users.Where(u => u.MemberOf.Contains(AllowedGroup)).ToList();
    }
}

public struct ADUser
{
    public string CN { get; set; }
    public string SamAcountName { get; set; }
    public string[] MemberOf { get; set; }
    public ADUser() { CN = ""; SamAcountName = ""; MemberOf = Array.Empty<string>(); }
}
