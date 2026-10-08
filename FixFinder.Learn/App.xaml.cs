using System.IO;
using System.Windows;
using System.Windows.Threading;
using FixFinder.Core.Engine;
using FixFinder.Desktop;

namespace FixFinder.Learn;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        // FixFinder's colours, as chosen in FixFinder's own Settings, so the two look alike side by side.
        Theme.Apply(Preferences.Load().Appearance);

        // FixFinder starts this with the code of the problem to open; started by hand, it opens on its lessons.
        var window = new LearnWindow(e.Args.FirstOrDefault(argument => !argument.StartsWith('-') && !argument.StartsWith('/')));
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var path = Path.Combine(Path.GetTempPath(), "FixFinder-Learn-crash.txt");

        try
        {
            File.AppendAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }

        MessageBox.Show(
            $"Something inside FixFinder Learn failed:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            $"The window is still open. Details were written to:\n{path}",
            "FixFinder Learn", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
