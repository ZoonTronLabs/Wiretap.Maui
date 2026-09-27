using System.Runtime.CompilerServices;

namespace Wiretap.Maui.Tests;

internal static class SqliteTestInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}
