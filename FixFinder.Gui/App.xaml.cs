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

    // No StartupUri on purpose - MainWindow is created only after this blocking consent
    // prompt is accepted, so a stray double-click on the .exe can't reach the tool by accident.
    //
    // FixFinder has three capabilities that each deserve to be named out loud before the
    // window exists, because none of them is obvious from the name of the tool:
    //   1. it launches a program you choose, as you, with your environment;
    //   2. it sends your captured error text to github.com and api.stackexchange.com;
    //   3. it can write to source files under a folder you choose.
    // This is the outermost of four gates - run-fixfinder.cmd (type YES) sits outside it, and
    // inside there is a per-target confirmation before launch and a type-APPLY box before any
    // file is written, with dry-run on by default. See the plan's "Consent" section.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var confirmed = MessageBox.Show(
            "FixFinder runs a program you choose, watches it for a crash, and looks for a " +
            "published fix.\n\n" +
            "Before you continue, note what it is able to do:\n\n" +
            "•  It LAUNCHES a program you pick, as you, with your environment, and " +
            "captures everything it writes to the terminal.\n\n" +
            "•  It SENDS the captured error text to github.com and api.stackexchange.com " +
            "to search for a fix. Scrub anything you would not want to send before running a " +
            "target that prints secrets.\n\n" +
            "•  It can MODIFY SOURCE FILES under a folder you choose - only a patch you " +
            "have previewed and confirmed, always backed up first, and never outside that " +
            "folder.\n\n" +
            "Only continue if you intended to run this tool right now.",
            "FixFinder",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        if (confirmed != MessageBoxResult.OK)
        {
            Shutdown();
            return;
        }

        // A path on the command line pre-selects the program, so dropping a file onto the exe
        // or its shortcut in Explorer works the same way as choosing one inside the window.
        // Nothing is run by this - it only fills in the choice, and the confirmation still asks.
        var window = new MainWindow(e.Args.FirstOrDefault(a => !a.StartsWith('-') && !a.StartsWith('/')));

        MainWindow = window;
        window.Show();
    }
}
