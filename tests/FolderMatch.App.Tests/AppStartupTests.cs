using Avalonia;
using Avalonia.Headless;
using Xunit;

namespace FolderMatch.App.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public void App_InitializesWithHeadlessDesktopPlatform()
    {
        var builder = AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

        builder.SetupWithoutStarting();

        Assert.IsType<App>(Application.Current);
    }
}
