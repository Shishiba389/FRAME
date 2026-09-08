using BicubicResizeLab;
using System.IO;
using System.Windows.Media.Imaging;

namespace BicubicResizeDropTest;

public partial class MainWindow
{
    private string? verificationDirectory;

    private void SaveSheet(string name)
    {
        if (verificationDirectory is null) return;
        UpdateLayout();
        var target = new RenderTargetBitmap(Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(Path.Combine(verificationDirectory, name + ".png"));
        encoder.Save(stream);
    }

    internal async Task VerifyPackAsync(string directory)
    {
        SuppressCompletionDialog = true;
        verificationDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(verificationDirectory);
        string previousError = Path.Combine(verificationDirectory, "verification-error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        string fixtures = Path.Combine(verificationDirectory, "fixtures", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(fixtures);
        SaveSheet("01-empty");

        for (int i = 0; i < 3; i++)
        {
            var image = new RgbaImage(720, 480);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    bool sun = (x - 510) * (x - 510) + (y - 132) * (y - 132) < 72 * 72;
                    bool hill = y > 280 + 75 * Math.Sin(x / 180.0 + i);
                    image[x, y] = sun ? new Rgba32(236, 119, 78) : hill ? new Rgba32(57, 91, 77) : new Rgba32(221, 225, 204);
                }
            ImageSharpBridge.SavePng(image, Path.Combine(fixtures, $"study-{i + 1}.png"));
        }

        await ViewModel.SetInputFolderAsync(fixtures);
        SaveSheet("02-ready");

        ViewModel.Width = 1600;
        ViewModel.Height = 2000;
        string expectedSuggestedOutput = Path.Combine(fixtures, "bicubic_1600x2000");
        if (ViewModel.DestinationFolder != expectedSuggestedOutput)
            throw new Exception("Suggested destination did not follow target dimensions.");
        ViewModel.ApplyPreset(PresetKind.Portrait, 1600, 2000);
        if (!ViewModel.IsPortraitPreset)
            throw new Exception("Preset state did not track the applied dimensions.");

        ViewModel.DestinationFolder = fixtures;
        await ViewModel.ResizeFolderAsync();
        if (!ViewModel.PhaseText.Contains("ATTENTION")) throw new Exception("Same-folder validation failed.");
        SaveSheet("03-validation");

        ViewModel.DestinationFolder = Path.Combine(fixtures, "exports");
        ViewModel.Width = 320; ViewModel.Height = 240;
        await ViewModel.ResizeFolderAsync();
        if (ViewModel.RunFolder is null || Directory.GetFiles(ViewModel.RunFolder, "*.png").Length != 3)
            throw new Exception("Batch output count failed.");
        foreach (string file in Directory.GetFiles(ViewModel.RunFolder, "*.png"))
            if (ImageSharpBridge.IdentifyDimensions(file) != (320, 240)) throw new Exception("Output dimensions failed.");
        SaveSheet("04-complete");

        ViewModel.OutputFormat = OutputFormat.Jpeg;
        if (!ViewModel.IsJpegBackgroundWhite) throw new Exception("JPEG background should default to white.");
        ViewModel.IsJpegBackgroundBlack = true;
        if (ViewModel.JpegBackground != JpegBackground.Black) throw new Exception("JPEG black background selection failed.");
        ViewModel.IsJpegBackgroundWhite = true;
        ViewModel.NamingSuffix = "review:/copy";
        ViewModel.DestinationFolder = Path.Combine(fixtures, "jpeg-exports");
        await ViewModel.ResizeFolderAsync();
        if (ViewModel.RunFolder is null || Directory.GetFiles(ViewModel.RunFolder, "*.jpg").Length != 3
            || Directory.GetFiles(ViewModel.RunFolder).Any(path => !Path.GetFileName(path).Contains("_reviewcopy_", StringComparison.Ordinal)))
            throw new Exception("JPEG format or filename suffix output failed.");

        var transparent = new RgbaImage(64, 64);
        string jpegAlphaFixtures = Path.Combine(verificationDirectory, "jpeg-alpha-fixtures");
        Directory.CreateDirectory(jpegAlphaFixtures);
        string whiteJpeg = Path.Combine(jpegAlphaFixtures, "transparent-white.jpg");
        ImageSharpBridge.Save(transparent, whiteJpeg, OutputFormat.Jpeg, JpegBackground.White);
        Rgba32 whitePixel = ImageSharpBridge.Load(whiteJpeg)[32, 32];
        if (whitePixel.R < 245 || whitePixel.G < 245 || whitePixel.B < 245 || whitePixel.A != 255)
            throw new Exception("Transparent PNG pixels did not flatten onto the selected white JPEG background.");

        string blackJpeg = Path.Combine(jpegAlphaFixtures, "transparent-black.jpg");
        ImageSharpBridge.Save(transparent, blackJpeg, OutputFormat.Jpeg, JpegBackground.Black);
        Rgba32 blackPixel = ImageSharpBridge.Load(blackJpeg)[32, 32];
        if (blackPixel.R > 10 || blackPixel.G > 10 || blackPixel.B > 10 || blackPixel.A != 255)
            throw new Exception("Transparent PNG pixels did not flatten onto the selected black JPEG background.");

        ViewModel.OutputFormat = OutputFormat.Webp;
        ViewModel.NamingSuffix = "";
        ViewModel.DestinationFolder = Path.Combine(fixtures, "webp-exports");
        await ViewModel.ResizeFolderAsync();
        if (ViewModel.RunFolder is null || Directory.GetFiles(ViewModel.RunFolder, "*.webp").Length != 3)
            throw new Exception("WebP format output failed.");
        ViewModel.OutputFormat = OutputFormat.Png;

        string firstRun = ViewModel.RunFolder;
        await ViewModel.ResizeFolderAsync();
        if (ViewModel.RunFolder == firstRun || Directory.GetFiles(firstRun, "*.webp").Length != 3)
            throw new Exception("Unique output run failed.");

        await File.WriteAllTextAsync(Path.Combine(fixtures, "broken.png"), "invalid-image-fixture");
        await ViewModel.ResizeFolderAsync();
        if (!ViewModel.PhaseText.Contains("ERRORS")) throw new Exception("Corrupt image reporting failed.");
        SaveSheet("05-file-error");

        ViewModel.CancelOnProcessingForTest = true;
        await ViewModel.ResizeFolderAsync();
        ViewModel.CancelOnProcessingForTest = false;
        if (ViewModel.PhaseText != "BATCH CANCELED" || ViewModel.Busy) throw new Exception("Cancellation recovery failed.");
        SaveSheet("06-canceled");

        Width = 700; UpdateLayout(); SaveSheet("07-compact");
        Width = 1220; UpdateLayout();

        string empty = Path.Combine(fixtures, "empty");
        Directory.CreateDirectory(empty);
        await ViewModel.SetInputFolderAsync(empty);
        if (ViewModel.StartEnabled || ViewModel.HasImages || ViewModel.PagerLabel != "0 / 0")
            throw new Exception("Empty folder must clear the resize and preview state.");
        SaveSheet("08-no-images");

        await ViewModel.SetInputFolderAsync(fixtures);
        ViewModel.Width = 20000; ViewModel.Height = 20000;
        await ViewModel.ResizeFolderAsync();
        if (!ViewModel.PhaseText.Contains("ATTENTION")) throw new Exception("Dimension safety feedback failed.");
        SaveSheet("09-safety-error");

        await File.WriteAllTextAsync(Path.Combine(verificationDirectory, "verification.txt"),
            "PASS: WPF rendering; source preview; same-folder validation; PNG, JPEG, and WebP output; safe filename suffixes; unique runs preserve previous output; " +
            "corrupt file reporting; JPEG transparency backgrounds; cancellation recovery; compact layout; suggested destination updates; empty folder reset; dimension safety limit.\n" +
            "Fixtures are generated locally; no user images were processed.\n");
    }
}
