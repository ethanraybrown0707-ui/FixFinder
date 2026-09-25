using System.Diagnostics;
using System.IO;
using System.Windows;
using FixFinder.Core.Http;
using FixFinder.Core.Security;

using System.Windows.Controls;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;

namespace FixFinder.Gui;

/// <summary>Holds the two optional API credentials and shows what is stored on disk.</summary>
public partial class SettingsWindow : Window
{
    private readonly FixFinderHttpClient _http;
    private StoredCredentials _stored;

    public bool Saved { get; private set; }

    public SettingsWindow(FixFinderHttpClient http)
    {
        InitializeComponent();

        _http = http;
        _stored = TokenStore.Load();

        CredentialPathText.Text = TokenStore.FilePath;
        ToolchainsText.Text = string.Join(Environment.NewLine, Core.Execution.Toolchains.Describe());

        AppearanceBox.SelectedIndex = (int)_preferences.Appearance;

        Fill(CStandardBox, LanguageStandards.CChoices, _preferences.CStandard, choice => choice.Length == 0 ? "Compiler's default" : choice.ToUpperInvariant());
        Fill(CppStandardBox, LanguageStandards.CppChoices, _preferences.CppStandard, choice => choice.Replace("c++", "C++"));
        Fill(JavaReleaseBox, LanguageStandards.JavaChoices, _preferences.JavaRelease, choice => choice.Length == 0 ? "The installed JDK" : $"Java {choice}");
        _standardsReady = true;

        RefreshCredentialState();
        RefreshCacheState();

        if (!TokenStore.IsSupported)
        {
            SettingsStatusText.Text = "Credentials cannot be stored on this platform.";
            GitHubTokenBox.IsEnabled = false;
            StackExchangeKeyBox.IsEnabled = false;
            SaveSettingsButton.IsEnabled = false;
        }
    }

    /// <summary>What the person chose last time, read once so changing it here can be written straight back.</summary>
    private readonly Preferences _preferences = Preferences.Load();

    /// <summary>
    /// Repaints the whole application at once, and writes the choice down.
    /// </summary>
    /// <remarks>
    /// Nothing is checked again and no finding moves: the colours are the only thing this touches. Failing to write
    /// the preference is not worth interrupting anybody over - it holds for this session either way.
    /// </remarks>
    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AppearanceBox.SelectedIndex < 0) return;

        var chosen = (AppearanceChoice)AppearanceBox.SelectedIndex;
        if (chosen == _preferences.Appearance) return;

        _preferences.Appearance = chosen;
        Theme.Apply(chosen);
        _preferences.Save();
    }

    /// <summary>Set once the boxes are filled, so filling them is not mistaken for somebody choosing.</summary>
    private bool _standardsReady;

    /// <summary>One version box: every listed choice, named for a reader, with the saved one selected.</summary>
    private static void Fill(ComboBox box, string[] choices, string saved, Func<string, string> named)
    {
        foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = named(choice), Tag = choice });

        var index = Array.IndexOf(choices, saved);
        box.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string Chosen(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>
    /// Takes effect for the next build and the next fix checked, and is written down. A program already checked is not
    /// checked again: the report on screen was made under the version that was set when it was made.
    /// </summary>
    private void Standard_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_standardsReady) return;

        _preferences.CStandard = Chosen(CStandardBox);
        _preferences.CppStandard = Chosen(CppStandardBox);
        _preferences.JavaRelease = Chosen(JavaReleaseBox);

        LanguageStandards.Current = _preferences.Standards;
        _preferences.Save();
    }

    private void RefreshCredentialState()
    {
        GitHubTokenStateText.Text = $"Stored: {TokenStore.Mask(_stored.GitHubToken)}";
        StackExchangeKeyStateText.Text = $"Stored: {TokenStore.Mask(_stored.StackExchangeKey)}";

        ClearGitHubTokenButton.IsEnabled = _stored.GitHubToken is not null;
        ClearStackExchangeKeyButton.IsEnabled = _stored.StackExchangeKey is not null;
    }

    private void RefreshCacheState()
    {
        try
        {
            var cache = _http.Cache;
            CacheStateText.Text = $"Response cache: {cache.Count} stored response(s) in {cache.Directory}";
        }
        catch (IOException ex)
        {
            CacheStateText.Text = $"Response cache could not be read: {ex.Message}";
        }
    }

    private void ClearGitHubTokenButton_Click(object sender, RoutedEventArgs e)
    {
        _stored = _stored with { GitHubToken = null };
        GitHubTokenBox.Clear();
        RefreshCredentialState();

        SettingsStatusText.Text = "Token will be removed when you press Save.";
    }

    private void ClearStackExchangeKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _stored = _stored with { StackExchangeKey = null };
        StackExchangeKeyBox.Clear();
        RefreshCredentialState();

        SettingsStatusText.Text = "Key will be removed when you press Save.";
    }

    private void OpenCacheFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_http.Cache.Directory);

            Process.Start(new ProcessStartInfo(_http.Cache.Directory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Could not open the folder:\n\n{ex.Message}",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Delete every cached API response?\n\n" +
            "Nothing is lost permanently - each one will simply be fetched again the next time " +
            "it is needed. But those requests come out of your rate-limit allowance, so a search " +
            "you have already run will cost quota a second time.",
            "FixFinder", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK) return;

        _http.Cache.Clear();
        RefreshCacheState();

        SettingsStatusText.Text = "Cache cleared.";
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var github = GitHubTokenBox.Password is { Length: > 0 } typedToken
            ? typedToken.Trim()
            : _stored.GitHubToken;

        var stackExchange = StackExchangeKeyBox.Password is { Length: > 0 } typedKey
            ? typedKey.Trim()
            : _stored.StackExchangeKey;

        var credentials = new StoredCredentials(
            string.IsNullOrWhiteSpace(github) ? null : github,
            string.IsNullOrWhiteSpace(stackExchange) ? null : stackExchange);

        if (credentials.GitHubToken is null && credentials.StackExchangeKey is null)
        {
            TokenStore.Clear();
        }
        else if (!TokenStore.Save(credentials))
        {
            MessageBox.Show(this,
                "The credentials could not be encrypted and stored. They have not been saved.",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        _http.SetGitHubToken(credentials.GitHubToken);
        _http.SetStackExchangeKey(credentials.StackExchangeKey);

        GitHubTokenBox.Clear();
        StackExchangeKeyBox.Clear();

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubTokenBox.Clear();
        StackExchangeKeyBox.Clear();

        DialogResult = false;
        Close();
    }
}
