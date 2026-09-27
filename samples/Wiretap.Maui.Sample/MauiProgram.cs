using CommunityToolkit.Maui;
using Wiretap.Maui;

namespace Wiretap.Maui.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        SQLitePCL.Batteries_V2.Init();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
#if DEBUG
            // Step 1: Add Wiretap services to the app
            .UseWiretap(options =>
            {
                options.MaxStoredRequests = 100;       // Keep last 100 requests
                options.MaskSensitiveHeaders = true;   // Mask Authorization, API keys, etc.
                options.PrettyPrintJson = true;        // Format JSON in detail view
                options.ShowFloatingButton = true;     // Show the floating inspector button
            })
#endif
            ;

        // Step 2: Configure HttpClient with Wiretap handler
        builder.Services.AddHttpClient("DemoApi", client =>
        {
            client.BaseAddress = new Uri("https://jsonplaceholder.typicode.com/");
            client.DefaultRequestHeaders.Add("X-Api-Key", "demo-api-key-12345"); // Will be masked
        })
#if DEBUG
        .AddWiretapHandler() // Add this to capture HTTP traffic
#endif
        ;

        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
