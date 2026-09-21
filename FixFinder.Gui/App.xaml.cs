using System.IO;
using System.Windows;
using System.Windows.Threading;

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

        var window = new MainWindow(e.Args.FirstOrDefault(a => !a.StartsWith('-') && !a.StartsWith('/')));

        MainWindow = window;
        window.Show();
    }
}
