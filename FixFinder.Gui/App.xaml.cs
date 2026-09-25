using System.IO;
using System.Windows;
using System.Windows.Threading;

using FixFinder.Core.Engine;

namespace FixFinder.Gui;

public partial class App : Application
{
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var path = Path.Combine(AppContext.BaseDirectory, "Logs", "FixFinder-crash.txt");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }

        MessageBox.Show(
            $"Something inside FixFinder failed:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            "The window is still open and nothing has been rolled back automatically. If a patch " +
            $"was being applied, check the backup folder.\n\nDetails were written to:\n{path}",
            "FixFinder", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        // Before the window exists, so it is built in the colours it will keep rather than repainted on sight.
        var preferences = Preferences.Load();
        Theme.Apply(preferences.Appearance);

        // Before anything is compiled, so the first build and the first fix checked are both held to the course's version.
        Core.Execution.LanguageStandards.Current = preferences.Standards;

        var window = new MainWindow(e.Args.FirstOrDefault(a => !a.StartsWith('-') && !a.StartsWith('/')));

        MainWindow = window;
        window.Show();
    }
}
