namespace NAPS2.App.Tests.Targets;

public class MacAppTestTarget : IAppTestTarget
{
    public AppTestExe Console => GetAppTestExe("console");
    public AppTestExe Gui => GetAppTestExe(null);
    public AppTestExe Worker => GetAppTestExe("worker");
    public AppTestExe Server => GetAppTestExe("server");
    public bool IsWindows => false;

    private AppTestExe GetAppTestExe(string argPrefix)
    {
        return new AppTestExe(
            Path.Combine(AppTestHelper.SolutionRoot, "NAPS2.App.Mac", "bin", "Debug", "net9-macos"),
            Path.Combine("NAPS2.app", "Contents", "MacOS", "ScanMe"),
            argPrefix);
    }

    public override string ToString() => "Mac";
}