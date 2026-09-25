using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikTokScrapper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Use the real window/template without creating a native window or stealing focus.
        var app = new App();
        app.InitializeComponent();
        var checks = 0;
        foreach (var scenario in new[] { "empty", "pasted", "typed" })
        {
            var window = new MainWindow();
            var input = (TextBox)window.FindName("ProfileInput");
            var surface = (FrameworkElement)window.Content;
            window.Content = null;
            surface.Width = window.Width;
            surface.Height = window.Height;
            surface.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
            surface.SetValue(TextElement.ForegroundProperty, window.Foreground);
            ((Border)surface).Background = window.Background;
            void Layout()
            {
                surface.Measure(new Size(window.Width, window.Height));
                surface.Arrange(new Rect(0, 0, window.Width, window.Height));
                surface.UpdateLayout();
            }
            Layout();
            const string text = "https://www.tiktok.com/@profile_name";
            if (scenario == "pasted") input.Text = text;
            if (scenario == "typed")
            {
                foreach (var character in text)
                {
                    input.Select(input.Text.Length, 0);
                    input.SelectedText = character.ToString();
                    Layout();
                }
            }
            Layout();
            var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
            var viewport = Descendants(host).OfType<ScrollContentPresenter>().First();
            var origin = viewport.TransformToAncestor(input).Transform(new Point());
            var first = input.GetRectFromCharacterIndex(0);
            var caret = input.GetRectFromCharacterIndex(input.Text.Length, true);
            if (viewport.ActualHeight < input.FontSize || first.IsEmpty || caret.IsEmpty ||
                first.Top < origin.Y - 1 || first.Bottom > origin.Y + viewport.ActualHeight + 1 ||
                caret.Top < origin.Y - 1 || caret.Bottom > origin.Y + viewport.ActualHeight + 1)
                throw new Exception($"{scenario}: text/caret clipped. Viewport height={viewport.ActualHeight}; first={first}; caret={caret}.");
            if (scenario != "empty" && input.Text != text) throw new Exception($"{scenario}: input did not retain the entered text.");
            if (input.CaretBrush is not SolidColorBrush { Color.A: > 0 }) throw new Exception("Caret brush must be visible.");
            if (input.Foreground is not SolidColorBrush { Color.A: > 0 }) throw new Exception("Input text must be visible.");
            Console.WriteLine($"PASS {scenario}: text and caret fit the {viewport.ActualHeight:0.##} px viewport.");
            checks++;
            if (args.Length == 1 && scenario == "pasted")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
                var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(args[0]); encoder.Save(output);
            }
        }
        var settingsWindow = new MainWindow();
        var checkbox = (CheckBox)settingsWindow.FindName("NotifyWhenFinishedCheckBox");
        if (checkbox.IsChecked != false) throw new Exception("Completion notification checkbox must default to off.");
        Console.WriteLine("PASS completion notification checkbox defaults to off."); checks++;
        var concurrency = (Slider)settingsWindow.FindName("ConcurrentDownloadsSlider");
        if (concurrency.Minimum != 1 || concurrency.Maximum != 10 || concurrency.Value != 3 || !concurrency.IsSnapToTickEnabled || concurrency.TickFrequency != 1)
            throw new Exception("Concurrency slider must default to three and snap to integers from one through ten.");
        Console.WriteLine("PASS concurrent-download slider range and default."); checks++;
        concurrency.Value = 7;
        settingsWindow.UpdateLayout();
        var valueLabel = (TextBlock)settingsWindow.FindName("ConcurrentDownloadsValue");
        if (valueLabel.Text != "7") throw new Exception("Slider's numeric label must follow its value.");
        Console.WriteLine("PASS slider value label updates."); checks++;
        var scratch = Path.Combine(Path.GetTempPath(), "TikTokScrapper-avatar-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var inputFile = Path.Combine(scratch, "avatar.png");
            var outputFile = Path.Combine(scratch, "_profile.jpeg");
            var input = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 0, 0, 0, 0 }, 8);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(input));
            using (var stream = File.Create(inputFile)) png.Save(stream);
            new AvatarEncoder().WriteJpeg(inputFile, outputFile);
            using var jpeg = File.OpenRead(outputFile);
            var decoded = BitmapDecoder.Create(jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            if (decoded.PixelWidth != 2 || decoded.PixelHeight != 1 || TikTokScrapper.Core.PhotoDownloader.DetectExtension(outputFile) != ".jpg")
                throw new Exception("Avatar must be a decodable JPEG with original dimensions.");
            Console.WriteLine("PASS transparent PNG avatar converts to a real JPEG without resizing."); checks++;
        }
        finally { Directory.Delete(scratch, true); }
        Console.WriteLine($"{checks} offscreen input-rendering regression checks passed.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
