#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AApplication = Android.App.Application;

namespace Wiretap.Maui.Services;

public sealed partial class WiretapEntryPointService
{
    private const string ChannelId = "wiretap_entry_point";
    private const int NotificationId = 43100;
    internal const string OpenWiretapExtra = "wiretap_open";

    partial void ShowPlatform(int count)
    {
        var context = AApplication.Context;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;

        EnsureChannel(manager);

        var packageName = context.PackageName;
        if (packageName is null)
            return;

        var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(packageName);
        if (launchIntent == null)
            return;

        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        launchIntent.PutExtra(OpenWiretapExtra, true);

        var pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            pendingFlags |= PendingIntentFlags.Immutable;

        var pendingIntent = PendingIntent.GetActivity(context, 0, launchIntent, pendingFlags);

        var contentText = count == 0
            ? "No captured requests"
            : $"{count} captured request{(count == 1 ? "" : "s")}";

        var lines = BuildPreviewLines();

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetContentTitle("Wiretap");
        builder.SetContentText(lines.Count > 0 ? lines[0] : contentText);
        builder.SetSmallIcon(Android.Resource.Drawable.IcMenuInfoDetails);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetContentIntent(pendingIntent);
        builder.SetPriority((int)NotificationCompat.PriorityLow);

        if (lines.Count > 0)
        {
            var inboxStyle = new NotificationCompat.InboxStyle();
            foreach (var line in lines)
                inboxStyle.AddLine(line);

            if (count > lines.Count)
                inboxStyle.SetSummaryText($"{count} total");

            builder.SetStyle(inboxStyle);
        }

        var notification = builder.Build();
        if (notification is not null)
            NotificationManagerCompat.From(context)?.Notify(NotificationId, notification);
    }

    partial void HidePlatform()
    {
        var context = AApplication.Context;
        NotificationManagerCompat.From(context)?.Cancel(NotificationId);
    }

    private static void EnsureChannel(NotificationManager manager)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(ChannelId, "Wiretap", NotificationImportance.Low)
        {
            Description = "Wiretap inspector entry point"
        };
        manager.CreateNotificationChannel(channel);
    }

    internal static void TryHandleIntent(Intent? intent)
    {
        if (intent?.GetBooleanExtra(OpenWiretapExtra, false) != true)
            return;

        intent.RemoveExtra(OpenWiretapExtra);
        OpenInspectorFromNotification();
    }
}
#endif
