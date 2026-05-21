using Wpf.Ui.Controls;

namespace AIUsageMonitor.Views;

/// <summary>
/// Standard app dialog chrome: Mica backdrop, rounded corners, and content in the title bar area.
/// </summary>
public class FluentAppWindow : FluentWindow
{
    protected FluentAppWindow()
    {
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WindowBackdropType.Mica;
        WindowCornerPreference = WindowCornerPreference.Round;
    }
}
