using System;
using System.Windows;

namespace GeminiLiveTranslate.App;

/// <summary>
/// Application entry point and global exception/lifecycle management for Gemini 3.5 Live Translate.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"[App] Unhandled UI Exception: {args.Exception.Message}");
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
