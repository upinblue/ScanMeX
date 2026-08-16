#nullable enable
using NAPS2.SharePoint;
using Xunit;

namespace NAPS2.Lib.Tests.SharePoint;

/// <summary>
/// Covers the Graph path syntax used to address the uploaded item. Uploading into the library root and
/// uploading into a subfolder go through the same expression, and a stray colon between the folder and
/// the file name only breaks the second case -- which is exactly how it reached the customer.
/// </summary>
public class SharePointUploadUrlTests
{
    private const string Site = "contoso.sharepoint.com,111,222";
    private const string Drive = "b!DRIVE";

    [Fact]
    public void BuildUploadUrl_LibraryRoot_UsesSinglePathExpression()
    {
        var url = SharePointUploadService.BuildUploadUrl(Site, Drive, null, "Test_1.pdf");

        Assert.Equal(
            $"https://graph.microsoft.com/v1.0/sites/{Site}/drives/{Drive}/root:/Test_1.pdf:/content",
            url);
    }

    [Fact]
    public void BuildUploadUrl_Subfolder_HasNoColonAfterTheFolder()
    {
        var url = SharePointUploadService.BuildUploadUrl(Site, Drive, "Robin_Test", "Test_1.pdf");

        Assert.Equal(
            $"https://graph.microsoft.com/v1.0/sites/{Site}/drives/{Drive}/root:/Robin_Test/Test_1.pdf:/content",
            url);
        Assert.DoesNotContain("Robin_Test:/", url);
    }

    [Fact]
    public void BuildUploadUrl_NestedFolders_KeepSeparatorsBetweenSegments()
    {
        var url = SharePointUploadService.BuildUploadUrl(Site, Drive, "2026/KW08", "4711.pdf");

        Assert.EndsWith("/root:/2026/KW08/4711.pdf:/content", url);
    }

    [Fact]
    public void BuildUploadUrl_EncodesSpacesAndSpecialCharactersPerSegment()
    {
        var url = SharePointUploadService.BuildUploadUrl(Site, Drive, "Eingang 2026/Q&A", "Auftrag 4711.pdf");

        Assert.EndsWith("/root:/Eingang%202026/Q%26A/Auftrag%204711.pdf:/content", url);
    }

    [Fact]
    public void BuildUploadUrl_EscapesASlashInTheFileNameInsteadOfNestingIt()
    {
        // The file name is a name, not a path. A slash that survived sanitizing must not silently move the
        // document into a folder nobody configured.
        var url = SharePointUploadService.BuildUploadUrl(Site, Drive, "Inbox", "a/b.pdf");

        Assert.EndsWith("/root:/Inbox/a%2Fb.pdf:/content", url);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "   ", null)]
    [InlineData("Sub", null, "Sub")]
    [InlineData(null, "Robin_Test", "Robin_Test")]
    [InlineData("/Sub/", "/Robin_Test/", "Sub/Robin_Test")]
    [InlineData("Sub//Deep", "Robin_Test", "Sub/Deep/Robin_Test")]
    public void CombineFolders_JoinsAndTrims(string? librarySubPath, string? folderPath, string? expected)
    {
        Assert.Equal(expected, SharePointUploadService.CombineFolders(librarySubPath, folderPath));
    }
}
