using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TikTokScrapper.Core;

namespace TikTokScrapper;

public partial class MainWindow : Window
{
    private AppSettings settings = SettingsStore.Load();
    private readonly EngineManager engine = new();
    private readonly CompletionNotifier completionNotifier = new();
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? engineCancellation;
    private bool busy, finished, closeWhenStopped;
    private string? resultFolder;
    private readonly Queue<string> logLines = new();
    private readonly object logLock = new();
    private static readonly Brush Mint = new SolidColorBrush(Color.FromRgb(168, 240, 205));
    private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(240, 180, 147));

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => completionNotifier.Dispose();
        RefreshFolder();
        SourceInitialized += (_, _) => { try { int value = 2; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref value, 4); } catch (DllNotFoundException) { } };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not System.Windows.Controls.Button) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        engineCancellation?.Cancel();
        if (!busy) return;
        e.Cancel = true; closeWhenStopped = true; cancellation?.Cancel();
        StatusTitle.Text = "Stopping safely…";
    }
    private void ProfileInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DownloadButton is null) return;
        ValidateInput();
    }
    private void ValidateInput()
    {
        var valid = ProfileUrl.TryParse(ProfileInput.Text, out var profile);
        Placeholder.Visibility = string.IsNullOrEmpty(ProfileInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = valid && !busy;
        ValidationText.Text = string.IsNullOrWhiteSpace(ProfileInput.Text) ? "Paste a profile link to get started." : valid ? $"Profile link looks good · @{profile!.Username}" : "Not a valid TikTok profile link. Use tiktok.com/@username.";
        ValidationText.Foreground = string.IsNullOrWhiteSpace(ProfileInput.Text) ? Brushes.DarkGray : valid ? Mint : Warning;
    }
    private void ProfileInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && DownloadButton.IsEnabled && !finished) Download_Click(sender, e); }
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (busy || engineCancellation is not null || !ProfileUrl.TryParse(ProfileInput.Text, out var profile)) return;
        busy = true; finished = false; resultFolder = null;
        SetDownloadingLayout(true);
        cancellation = new();
        ProfileInput.IsEnabled = false; SettingsButton.IsEnabled = false; DownloadButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible; PercentText.Visibility = Visibility.Collapsed;
        DetailsExpander.Visibility = Visibility.Visible; DetailsExpander.IsExpanded = false;
        StatusTitle.Foreground = Mint; StatusDot.Fill = Mint; StatusTitle.Text = "Preparing your download…";
        OverallProgress.Value = 0; OverallProgress.IsIndeterminate = true; ExtraCountText.Text = ""; CountText.Text = "0 of 0 downloaded";
        lock (logLock) logLines.Clear();
        LogText.Clear();
        try
        {
            await engine.EnsureAsync(new Progress<string>(message => DetailText.Text = message), cancellation.Token);
            var service = new DownloadService(new ProcessRunner(), avatarEncoder: new AvatarEncoder());
            var result = await service.DownloadAsync(profile!, settings, engine.EnginePath, new Progress<DownloadProgress>(RenderProgress), Log, cancellation.Token);
            resultFolder = result.Folder;
            OverallProgress.IsIndeterminate = false; OverallProgress.Value = 100; PercentText.Text = "100%";
            var success = result.Failed.Count == 0;
            finished = true;
            ShowResult(success ? "Download Completed" : "Finished with failed files",
                $"{result.Videos} videos · {result.Images} images" + (result.AvatarSaved ? " · profile avatar" : "") + $" · {result.Skipped} already saved" + (success ? ". Your posts are ready." : $" · {result.Failed.Count} failed. Download again to retry missing files."),
                $"{result.Downloaded} of {result.Total} saved", success, !success);
            RefreshFolder();
            if (!closeWhenStopped && CompletionNotice.Create(settings, result) is { } notice)
            {
                try { completionNotifier.Show(new WindowInteropHelper(this).Handle, notice); }
                catch (Exception ex) { Log("Completion notification: " + ex.Message); }
            }
        }
        catch (OperationCanceledException)
        {
            ShowResult("Download stopped", "Completed files are safe. Download again to skip saved files and retry the remaining ones.", CountText.Text, false, false);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            ShowResult("Couldn't finish this download", ex.Message, CountText.Text, false, true);
        }
        finally
        {
            busy = false; OverallProgress.IsIndeterminate = false;
            SetDownloadingLayout(false);
            CancelButton.Visibility = Visibility.Collapsed; CancelButton.IsEnabled = true; PercentText.Visibility = Visibility.Visible;
            ProfileInput.IsEnabled = !finished; SettingsButton.IsEnabled = true;
            cancellation.Dispose(); cancellation = null; ValidateInput();
            if (closeWhenStopped) Close();
        }
    }
    private void SetDownloadingLayout(bool active)
    {
        ProfileCard.Visibility = active || finished ? Visibility.Collapsed : Visibility.Visible;
        StatusCard.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active) ResultCard.Visibility = Visibility.Collapsed;
    }
    private void ShowResult(string title, string detail, string count, bool success, bool showDetails)
    {
        ResultTitle.Text = title; ResultDetail.Text = detail; ResultCount.Text = count;
        ResultTitle.Foreground = success ? Mint : Warning; ResultDot.Fill = ResultTitle.Foreground;
        ResultDetailsExpander.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        ResultDetailsExpander.IsExpanded = showDetails;
        CompletionButtons.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Visible;
    }
    private void RenderProgress(DownloadProgress progress)
    {
        OverallProgress.IsIndeterminate = progress.Phase == "scan";
        StatusTitle.Text = progress.Phase == "scan" ? "Finding your posts…" : progress.ActivePosts > 1 ? $"Saving your posts · {progress.ActivePosts} active" : "Saving your posts…";
        CountText.Text = $"{progress.Completed} of {progress.Total} files downloaded";
        ExtraCountText.Text = string.Join(" · ", new[] { progress.Skipped > 0 ? $"{progress.Skipped} already saved" : "", progress.Failed > 0 ? $"{progress.Failed} failed" : "" }.Where(t => t.Length > 0));
        var percent = progress.Total == 0 ? 0 : 100d * (progress.Processed + progress.Fraction) / progress.Total;
        OverallProgress.Value = percent; PercentText.Text = $"{percent:0}%"; DetailText.Text = progress.Message;
    }
    private void Log(string line)
    {
        lock (logLock) { logLines.Enqueue(line); while (logLines.Count > 100) logLines.Dequeue(); }
        Dispatcher.BeginInvoke(() => { lock (logLock) LogText.Text = string.Join(Environment.NewLine, logLines); LogText.ScrollToEnd(); });
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; StatusTitle.Text = "Stopping safely…"; }
    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        finished = false; ProfileInput.IsEnabled = true; ProfileInput.Clear(); resultFolder = null;
        SetDownloadingLayout(false); ResultCard.Visibility = Visibility.Collapsed;
        CompletionButtons.Visibility = Visibility.Collapsed;
        StatusTitle.Text = "Ready when you are"; StatusTitle.Foreground = Brushes.WhiteSmoke; StatusDot.Fill = Brushes.Gray;
        OverallProgress.Value = 0; PercentText.Text = "0%"; CountText.Text = "0 of 0 downloaded"; ExtraCountText.Text = "";
        DetailText.Text = "Your download progress will appear here."; DetailsExpander.Visibility = Visibility.Collapsed; DetailsExpander.IsExpanded = false;
        RefreshFolder(); ProfileInput.Focus();
    }
    private void RefreshFolder() { FolderText.Text = resultFolder ?? settings.SaveFolder; FolderText.ToolTip = FolderText.Text; }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { var folder = resultFolder ?? settings.SaveFolder; Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true }); }
        catch (Exception ex) { ShowResult("Couldn't open the folder", ex.Message, "", false, false); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SaveFolderInput.Text = settings.SaveFolder; CookiesInput.Text = settings.CookiesFile ?? ""; SettingsError.Text = "";
        NotifyWhenFinishedCheckBox.IsChecked = settings.NotifyWhenFinished;
        ConcurrentDownloadsSlider.Value = settings.DownloadLimit;
        SettingsOverlay.Visibility = Visibility.Visible; SaveFolderInput.Focus();
    }
    private void CloseSettings_Click(object sender, RoutedEventArgs e) { engineCancellation?.Cancel(); SettingsOverlay.Visibility = Visibility.Collapsed; }
    private void ConcurrentDownloadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConcurrentDownloadsValue is not null) ConcurrentDownloadsValue.Text = Math.Round(e.NewValue).ToString("0");
    }
    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to save TikTok posts", Multiselect = false };
        if (Directory.Exists(SaveFolderInput.Text)) dialog.InitialDirectory = SaveFolderInput.Text;
        if (dialog.ShowDialog(this) == true) SaveFolderInput.Text = dialog.FolderName;
    }
    private void ResetFolder_Click(object sender, RoutedEventArgs e) => SaveFolderInput.Text = SettingsStore.DefaultFolder;
    private void BrowseCookies_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose your TikTok cookies file", Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) CookiesInput.Text = dialog.FileName;
    }
    private void ClearCookies_Click(object sender, RoutedEventArgs e) => CookiesInput.Clear();
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = SaveFolderInput.Text.Trim();
            if (!Path.IsPathFullyQualified(path)) throw new IOException("Please choose a full folder path, such as E:\\Videos.");
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-test-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            var cookies = string.IsNullOrWhiteSpace(CookiesInput.Text) ? null : CookiesInput.Text;
            if (cookies is not null && !File.Exists(cookies)) throw new IOException("The cookies file could not be found.");
            var updated = new AppSettings(path, cookies, NotifyWhenFinishedCheckBox.IsChecked == true, (int)Math.Round(ConcurrentDownloadsSlider.Value));
            SettingsStore.Save(updated); settings = updated; resultFolder = null; RefreshFolder();
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { SettingsError.Text = ex.Message; }
    }
    private async void UpdateEngine_Click(object sender, RoutedEventArgs e)
    {
        if (engineCancellation is not null) return;
        engineCancellation = new(); UpdateEngineButton.IsEnabled = false; SaveSettingsButton.IsEnabled = false;
        try { await engine.UpdateAsync(new Progress<string>(message => EngineStatus.Text = message), engineCancellation.Token); }
        catch (OperationCanceledException) { EngineStatus.Text = "Update cancelled. Your existing engine is unchanged."; }
        catch (Exception ex) { EngineStatus.Text = "Update failed: " + ex.Message; }
        finally { UpdateEngineButton.IsEnabled = true; SaveSettingsButton.IsEnabled = true; engineCancellation.Dispose(); engineCancellation = null; }
    }

    // Deterministic design export for reviewers; this mode makes no network requests.
    internal async Task RenderPreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        // Fresh detached visuals avoid WPF render-cache artifacts between states.
        // No HWND is created and no desktop window is displayed.
        async Task Capture(MainWindow window, string filename)
        {
            var surface = (FrameworkElement)window.Content;
            window.Content = null;
            surface.Width = window.Width;
            surface.Height = window.Height;
            surface.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
            surface.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
            ((System.Windows.Controls.Border)surface).Background = window.Background;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            surface.Measure(new Size(window.Width, window.Height));
            surface.Arrange(new Rect(0, 0, window.Width, window.Height));
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, filename)); encoder.Save(file);
        }
        await Capture(this, "interface.png");
        var downloading = new MainWindow();
        downloading.ProfileInput.Text = "https://www.tiktok.com/@orgonite222888";
        downloading.SetDownloadingLayout(true);
        downloading.RenderProgress(new("download", "Photo post · Image 2 of 4", 11, 5, 0, 2, 5, 1.64, 3));
        downloading.StatusTitle.Foreground = Mint; downloading.StatusDot.Fill = Mint;
        downloading.SettingsButton.IsEnabled = false;
        downloading.CancelButton.Visibility = Visibility.Visible; downloading.PercentText.Visibility = Visibility.Collapsed;
        await Capture(downloading, "downloading.png");
        var completed = new MainWindow { finished = true };
        completed.SetDownloadingLayout(false);
        completed.ShowResult("Download Completed", "6 videos · 4 images · profile avatar · 3 already saved. Your posts are ready.", "11 of 11 saved", true, false);
        await Capture(completed, "completed.png");
        var preferences = new MainWindow();
        preferences.Settings_Click(preferences, new RoutedEventArgs());
        await Capture(preferences, "settings.png");
    }
}
