using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Wiretap.Maui.Services;

internal static class WiretapServiceLocator
{
    public static IServiceProvider? GetServices()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
#if ANDROID
        services ??= IPlatformApplication.Current?.Services;
#endif
        return services;
    }
}
