using System.Drawing;
using System.IO;
using WpfApplication = System.Windows.Application;

namespace AIUsageMonitor.Services;

internal static class AppIconService
{
    public static Icon LoadTrayIcon()
    {
        try
        {
            var resource = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
            if (resource?.Stream is not null)
            {
                using var icon = new Icon(resource.Stream);
                return (Icon)icon.Clone();
            }
        }
        catch (IOException)
        {
        }
        catch (ArgumentException)
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
