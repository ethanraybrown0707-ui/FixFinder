using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FixFinder.Gui;

public partial class App : Application
{
    /// <summary>
    /// Catches anything that escapes an event handler, so the window survives and says why.
    /// </summary>
    /// <remarks>
    /// Without this, one unhandled exception closes the whole tool instantly and silently -
    /// no message, no log, and every field the user had filled in gone. That is a poor failure
    /// mode for any application and a worse one here, where the tool may have written files and
    /// the user is left with no idea whether it finished.
    /// </remarks>
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
            // Reporting the failure must not itself fail.
        }

        MessageBox.Show(
            $"Something inside FixFinder failed:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            "The window is still open and nothing has been rolled back automatically. If a patch " +
            $"was being applied, check the backup folder.\n\nDetails were written to:\n{path}",
            "FixFinder", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // No StartupUri, because the window is given the program named on the command line, if any.
    //
    // There is no notice before the window opens. The one that used to be here came from the tool this
    // was modelled on, and it had outlived what it described - FixFinder no longer writes to source
    // files at all. What still deserves a confirmation is launching a program, and that is asked
    // before every run, naming the exact command line.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        // A path on the command line pre-selects the program, so dropping a file onto the exe
        // or its shortcut in Explorer works the same way as choosing one inside the window.
        // Nothing is run by this - it only fills in the choice, and the confirmation still asks.
        var window = new MainWindow(e.Args.FirstOrDefault(a => !a.StartsWith('-') && !a.StartsWith('/')));

        MainWindow = window;
        window.Show();
    }
}
