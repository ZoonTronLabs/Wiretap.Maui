#if IOS
using UserNotifications;

namespace Wiretap.Maui.Services;

public sealed partial class WiretapEntryPointService
{
    private const string NotificationId = "wiretap_entry_point";
    private static readonly object DelegateSync = new();
    private static WiretapNotificationDelegate? _wiretapDelegate;

    partial void ShowPlatform(int count)
    {
        EnsureNotificationDelegate();

        var center = UNUserNotificationCenter.Current;
        center.GetNotificationSettings(settings =>
        {
            if (!IsVisible || settings.AuthorizationStatus != UNAuthorizationStatus.Authorized)
                return;

            var content = new UNMutableNotificationContent
            {
                Title = "Wiretap",
                Body = count == 0
                    ? "No captured requests"
                    : $"{count} captured request{(count == 1 ? "" : "s")}",
                InterruptionLevel = UNNotificationInterruptionLevel.Passive2
            };
            var request = UNNotificationRequest.FromIdentifier(NotificationId, content, null);
            center.AddNotificationRequest(request, _ => { });
        });
    }

    partial void HidePlatform()
    {
        var center = UNUserNotificationCenter.Current;
        center.RemovePendingNotificationRequests(new[] { NotificationId });
        center.RemoveDeliveredNotifications(new[] { NotificationId });
    }

    private static void EnsureNotificationDelegate()
    {
        lock (DelegateSync)
        {
            var center = UNUserNotificationCenter.Current;
            if (center.Delegate is WiretapNotificationDelegate)
                return;

            _wiretapDelegate = new WiretapNotificationDelegate(center.Delegate);
            center.Delegate = _wiretapDelegate;
        }
    }

    private sealed class WiretapNotificationDelegate : UNUserNotificationCenterDelegate
    {
        private readonly IUNUserNotificationCenterDelegate? _inner;

        public WiretapNotificationDelegate(IUNUserNotificationCenterDelegate? inner)
        {
            _inner = inner;
        }

        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center,
            UNNotificationResponse response,
            Action completionHandler)
        {
            try
            {
                if (IsWiretapNotification(response.Notification))
                    OpenInspectorFromNotification();
                else if (_inner != null)
                {
                    _inner.DidReceiveNotificationResponse(center, response, completionHandler);
                    return;
                }
            }
            finally
            {
                if (IsWiretapNotification(response.Notification) || _inner == null)
                    completionHandler();
            }
        }

        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
        {
            if (IsWiretapNotification(notification))
                completionHandler(UNNotificationPresentationOptions.List);
            else if (_inner != null)
                _inner.WillPresentNotification(center, notification, completionHandler);
            else
                completionHandler(UNNotificationPresentationOptions.Badge);
        }

        private static bool IsWiretapNotification(UNNotification notification)
        {
            return notification.Request.Identifier == NotificationId;
        }
    }
}
#endif
