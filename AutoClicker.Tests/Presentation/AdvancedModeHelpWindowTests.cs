using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AdvancedModeHelpWindowTests
{
    [STATestMethod]
    public void Constructor_LoadsTheEmbeddedApplicationIcon()
    {
        var application = new App();
        application.InitializeComponent();
        AdvancedModeHelpWindow? window = null;
        try
        {
            window = new AdvancedModeHelpWindow();

            Assert.IsNotNull(window.Icon);
        }
        finally
        {
            window?.Close();
            application.Shutdown();
        }
    }
}
