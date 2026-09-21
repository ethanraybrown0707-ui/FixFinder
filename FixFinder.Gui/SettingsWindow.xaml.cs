using System.Diagnostics;
using System.IO;
using System.Windows;
using FixFinder.Core.Http;
using FixFinder.Core.Security;

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
