using System.DirectoryServices;

namespace dwg2img.Data;

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
                    {
                        var groups = searchResult.Properties[MemberOfProperty];
                        List<string> groupsList = new();
                        foreach (var group in groups)
                        {
                            groupsList.Add(group.ToString()?.Split(',').ElementAtOrDefault(0) ?? "Unknown");
                        }
                        user.MemberOf = groupsList.ToArray();
                    }

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
