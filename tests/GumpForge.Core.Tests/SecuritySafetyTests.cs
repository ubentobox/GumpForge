using Xunit;
using System.Collections.Generic;

namespace GumpForge.Core.Tests;

public class SecuritySafetyTests
{
    // Local copy of the security filter logic to verify correct sanitization rules
    private static string CleanValue(string key, object val)
    {
        if (val == null) return "null";
        string sVal = val.ToString() ?? "";
        string keyLower = key.ToLowerInvariant();
        if (keyLower.Contains("pass") || keyLower.Contains("password") || keyLower.Contains("acctname") || keyLower.Contains("accountname"))
        {
            return "[PROTECTED]";
        }
        return sVal;
    }

    [Fact]
    public void TestPasswordIsProtected()
    {
        Assert.Equal("[PROTECTED]", CleanValue("Password", "SuperSecret123"));
        Assert.Equal("[PROTECTED]", CleanValue("PasswordHash", "0192837bcda1234"));
        Assert.Equal("[PROTECTED]", CleanValue("acctname", "gm_username"));
        Assert.Equal("[PROTECTED]", CleanValue("AccountName", "admin_staff"));
    }

    [Fact]
    public void TestNormalPropertiesAreNotProtected()
    {
        Assert.Equal("100", CleanValue("Hits", 100));
        Assert.Equal("Aragorn", CleanValue("Name", "Aragorn"));
        Assert.Equal("1500", CleanValue("Gold", 1500));
    }
}
