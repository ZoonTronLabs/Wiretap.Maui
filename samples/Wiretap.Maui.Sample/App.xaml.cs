#if IOS
using UserNotifications;
#endif

namespace Wiretap.Maui.Sample;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = _serviceProvider.GetRequiredService<MainPage>();
        var window = new Window(mainPage);

        // Show the notification entry point after the window is ready.
        window.Created += (s, e) =>
        {
#if DEBUG
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () => _ = ShowEntryPointAsync());
#endif
        };

        return window;
    }

#if DEBUG
    private async Task ShowEntryPointAsync()
    {
#if IOS
        var authorization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert,
            (granted, error) => authorization.TrySetResult(granted && error is null));
        if (!await authorization.Task)
            return;
#elif ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            await Permissions.RequestAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
            return;
#endif
        _serviceProvider.ShowWiretapEntryPoint();
    }
#endif
}
