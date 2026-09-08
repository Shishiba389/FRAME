using BicubicResizeLab;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace BicubicResizeDropTest;

public enum PresetKind { None, Square, Portrait, Wide }

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    /// Fires exactly once per finished batch (not on cancel). The main window subscribes to show
    /// the completion popup that replaces the old always-visible activity log.
    public event Action<BatchSummary>? BatchCompleted;

    public readonly record struct BatchSummary(bool HasErrors, int Saved, int Total, int ErrorCount, IReadOnlyList<string> Errors, string? RunFolder);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string sourceFolder = "";
    public string SourceFolder { get => sourceFolder; private set => Set(ref sourceFolder, value); }

    private string destinationFolder = "";
    public string DestinationFolder { get => destinationFolder; set => Set(ref destinationFolder, value); }

    private int width = 2000;
    public int Width
    {
        get => width;
        set
        {
            int clamped = Math.Clamp(value, 1, 20000);
            if (clamped == width) return;
            width = clamped;
            Raise(nameof(Width));
            if (LockAspectRatio && lockedRatio > 0 && !suppressRatioSync)
            {
                suppressRatioSync = true;
                Height = Math.Max(1, (int)Math.Round(width / lockedRatio));
                suppressRatioSync = false;
            }
            if (!suppressPresetReset) ActivePreset = PresetKind.None;
            RefreshDimensionsSummary();
            RefreshSuggestedDestination();
        }
    }

    private int height = 2000;
    public int Height
    {
        get => height;
        set
        {
            int clamped = Math.Clamp(value, 1, 20000);
            if (clamped == height) return;
            height = clamped;
            Raise(nameof(Height));
            if (LockAspectRatio && lockedRatio > 0 && !suppressRatioSync)
            {
                suppressRatioSync = true;
                Width = Math.Max(1, (int)Math.Round(height * lockedRatio));
                suppressRatioSync = false;
            }
            if (!suppressPresetReset) ActivePreset = PresetKind.None;
            RefreshDimensionsSummary();
            RefreshSuggestedDestination();
        }
    }

    private bool lockAspectRatio;
    private double lockedRatio;         // ratio used while lock is engaged
    private double sourceAspectRatio;   // W/H of the current preview source; 0 when no image loaded
    private bool suppressRatioSync;
    private bool suppressPresetReset;
    public bool LockAspectRatio
    {
        get => lockAspectRatio;
        set
        {
            if (lockAspectRatio == value) return;
            lockAspectRatio = value;
            if (value)
            {
                // Prefer the loaded source image's aspect ratio (matches photo-editor convention);
                // fall back to the current W/H ratio when no preview is available yet.
                lockedRatio = sourceAspectRatio > 0 ? sourceAspectRatio : (height > 0 ? (double)width / height : 1);
            }
            Raise(nameof(LockAspectRatio));
        }
    }

    private OutputFormat outputFormat = OutputFormat.Png;
    public OutputFormat OutputFormat
    {
        get => outputFormat;
        set
        {
            if (outputFormat == value) return;
            outputFormat = value;
            Raise(nameof(OutputFormat)); Raise(nameof(IsPng)); Raise(nameof(IsJpeg)); Raise(nameof(IsWebp));
            RefreshDimensionsSummary();
        }
    }
    public bool IsPng { get => OutputFormat == OutputFormat.Png; set { if (value) OutputFormat = OutputFormat.Png; } }
    public bool IsJpeg { get => OutputFormat == OutputFormat.Jpeg; set { if (value) OutputFormat = OutputFormat.Jpeg; } }
    public bool IsWebp { get => OutputFormat == OutputFormat.Webp; set { if (value) OutputFormat = OutputFormat.Webp; } }

    private PresetKind activePreset = PresetKind.Square;   // default 2000×2000 == Square
    public PresetKind ActivePreset
    {
        get => activePreset;
        private set
        {
            if (activePreset == value) return;
            activePreset = value;
            Raise(nameof(ActivePreset)); Raise(nameof(IsSquarePreset)); Raise(nameof(IsPortraitPreset)); Raise(nameof(IsWidePreset));
        }
    }
    // Radios bind here — setting to true from XAML is ignored (only ApplyPreset changes the state).
    public bool IsSquarePreset { get => ActivePreset == PresetKind.Square; set { } }
    public bool IsPortraitPreset { get => ActivePreset == PresetKind.Portrait; set { } }
    public bool IsWidePreset { get => ActivePreset == PresetKind.Wide; set { } }

    public bool HasImages => FileCount > 0;

    private string namingSuffix = "";
    public string NamingSuffix
    {
        get => namingSuffix;
        set => Set(ref namingSuffix, value ?? "");
    }

    private string dimensionsSummary = "2,000 × 2,000 px  /  PNG";
    public string DimensionsSummary { get => dimensionsSummary; private set => Set(ref dimensionsSummary, value); }

    private BitmapSource? previewImage;
    public BitmapSource? PreviewImage { get => previewImage; private set => Set(ref previewImage, value); }

    private string previewHeading = "Drop your image folder here";
    public string PreviewHeading { get => previewHeading; private set => Set(ref previewHeading, value); }

    private string previewSubtitle = "or click Browse below";
    public string PreviewSubtitle { get => previewSubtitle; private set => Set(ref previewSubtitle, value); }

    private string previewCaption = "";
    public string PreviewCaption { get => previewCaption; private set => Set(ref previewCaption, value); }

    private int fileCount;
    public int FileCount { get => fileCount; private set { Set(ref fileCount, value); Raise(nameof(SourceCaption)); Raise(nameof(PagerLabel)); Raise(nameof(CanPagePrev)); Raise(nameof(CanPageNext)); Raise(nameof(HasImages)); } }
    public string SourceCaption => FileCount == 0 ? "01 / YOUR SOURCE" : $"01 / YOUR SOURCE  ·  {FileCount} FILE{(FileCount == 1 ? "" : "S")}";

    private string statusText = "Choose a folder to start your first batch.";
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }

    private string phaseText = "READY WHEN YOU ARE";
    public string PhaseText { get => phaseText; private set { Set(ref phaseText, value); Raise(nameof(ShowSuccessTick)); } }
    public bool ShowSuccessTick => phaseText.Contains("COMPLETE") && !phaseText.Contains("ERROR");

    private int progressValue;
    public int ProgressValue { get => progressValue; private set => Set(ref progressValue, value); }

    private bool busy;
    public bool Busy { get => busy; private set => Set(ref busy, value); }

    private bool startEnabled;
    public bool StartEnabled { get => startEnabled; private set => Set(ref startEnabled, value); }

    private bool cancelEnabled;
    public bool CancelEnabled { get => cancelEnabled; private set => Set(ref cancelEnabled, value); }

    private bool openEnabled;
    public bool OpenEnabled { get => openEnabled; private set => Set(ref openEnabled, value); }

    private bool isDragOver;
    public bool IsDragOver { get => isDragOver; set => Set(ref isDragOver, value); }

    public string? RunFolder { get; private set; }

    /// Test-only hook: when true, the next batch cancels itself as soon as processing starts.
    internal bool CancelOnProcessingForTest { get; set; }

    private CancellationTokenSource? cancellation;
    private string? suggestedOutput;
    private int previewVersion;
    private int runVersion;

    // Paging state — snapshot of the folder scan, kept for the < > pager.
    private string[] sourceFiles = Array.Empty<string>();
    private int currentPreviewIndex;
    public int CurrentPreviewIndex { get => currentPreviewIndex; private set { Set(ref currentPreviewIndex, value); Raise(nameof(PagerLabel)); Raise(nameof(CanPagePrev)); Raise(nameof(CanPageNext)); } }
    public string PagerLabel => sourceFiles.Length == 0 ? "0 / 0" : $"{currentPreviewIndex + 1} / {sourceFiles.Length}";
    public bool CanPagePrev => sourceFiles.Length > 1 && currentPreviewIndex > 0;
    public bool CanPageNext => sourceFiles.Length > 1 && currentPreviewIndex < sourceFiles.Length - 1;

    private void RefreshDimensionsSummary() => DimensionsSummary = $"{Width:N0} × {Height:N0} px  /  {OutputFormat.ToString().ToUpperInvariant()}";

    private void RefreshSuggestedDestination()
    {
        if (suggestedOutput is null || DestinationFolder != suggestedOutput) return;
        suggestedOutput = Path.Combine(SourceFolder, $"bicubic_{Width}x{Height}");
        DestinationFolder = suggestedOutput;
    }

    public void ApplyPreset(PresetKind kind, int presetWidth, int presetHeight)
    {
        suppressRatioSync = true;      // preset picks a fresh W and H, don't let lock re-derive one from the other
        suppressPresetReset = true;    // and don't let the W/H setters clear ActivePreset right after we set it
        Width = presetWidth;
        Height = presetHeight;
        suppressRatioSync = false;
        suppressPresetReset = false;
        if (LockAspectRatio && Height > 0) lockedRatio = (double)Width / Height;
        ActivePreset = kind;
    }

    public async Task SetInputFolderAsync(string folder)
    {
        if (Busy) return;
        int version = ++previewVersion;
        SourceFolder = folder;
        suggestedOutput = Path.Combine(folder, $"bicubic_{Width}x{Height}");
        DestinationFolder = suggestedOutput;
        PreviewImage = null;
        PreviewHeading = "Reading your folder…";
        PreviewSubtitle = "Finding an image for your worktable.";
        StartEnabled = false;
        OpenEnabled = false;
        RunFolder = null;
        ProgressValue = 0;
        PhaseText = "READING SOURCE";
        sourceFiles = Array.Empty<string>();
        FileCount = 0;
        CurrentPreviewIndex = 0;
        sourceAspectRatio = 0;
        try
        {
            string[] files = await Task.Run(() => Directory.EnumerateFiles(folder).Where(IsSupportedImage)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray());
            if (version != previewVersion) return;
            sourceFiles = files;
            FileCount = files.Length;
            PreviewHeading = files.Length == 0 ? "A little too empty." : new DirectoryInfo(folder).Name;
            PreviewSubtitle = files.Length == 0
                ? "Choose a folder containing supported images."
                : $"{files.Length} images on the table. Let's give them a new size.";
            StatusText = files.Length == 0
                ? "No supported images found. Choose another folder."
                : "Source ready. Set dimensions and destination, then resize.";
            PhaseText = files.Length == 0 ? "NO IMAGES FOUND" : "SOURCE READY";
            StartEnabled = files.Length > 0;
            if (files.Length > 0)
            {
                await LoadPreviewAtIndexAsync(0, version);
            }
        }
        catch (Exception ex)
        {
            if (version != previewVersion) return;
            ShowError($"Cannot read source folder: {ex.Message}");
            PreviewHeading = "Try another folder.";
            PreviewSubtitle = "Use Choose a folder to continue.";
        }
    }

    public async Task PagePreviousAsync() { if (CanPagePrev) await LoadPreviewAtIndexAsync(currentPreviewIndex - 1, ++previewVersion); }
    public async Task PageNextAsync() { if (CanPageNext) await LoadPreviewAtIndexAsync(currentPreviewIndex + 1, ++previewVersion); }

    /// Loads a specific file's preview (used by both initial folder scan and the < > pager).
    /// Falls through to the next file if the chosen one fails, so a single bad image never
    /// leaves the stage blank while other images in the folder are fine.
    private async Task LoadPreviewAtIndexAsync(int index, int version)
    {
        if (sourceFiles.Length == 0) return;
        int start = Math.Clamp(index, 0, sourceFiles.Length - 1);
        (BitmapSource? image, string caption, int actualIndex) = await Task.Run(() =>
        {
            for (int offset = 0; offset < Math.Min(sourceFiles.Length, 8); offset++)
            {
                int i = (start + offset) % sourceFiles.Length;
                (BitmapSource? img, string cap) = LoadPreview(sourceFiles[i]);
                if (img is not null) return (img, cap, i);
            }
            return ((BitmapSource?)null, "SOURCE PREVIEW UNAVAILABLE", start);
        });
        if (version != previewVersion) return;
        CurrentPreviewIndex = actualIndex;
        PreviewCaption = $"{caption}  ·  {PagerLabel}";
        PreviewImage = image;
        // Capture the source's aspect ratio so the lock button snaps to the real photo's shape,
        // not whatever the target W/H happened to be when the user clicked lock.
        if (image is not null && image.PixelHeight > 0)
        {
            sourceAspectRatio = (double)image.PixelWidth / image.PixelHeight;
            if (LockAspectRatio) lockedRatio = sourceAspectRatio;
        }
        if (image is null) StatusText = "Preview unavailable. Resize checks each file and reports errors.";
    }

    private static (BitmapSource? Image, string Caption) LoadPreview(string file)
    {
        try
        {
            (int w, int h) = ImageSharpBridge.IdentifyDimensions(file);
            if ((long)w * h > 24_000_000) return (null, "");
            using var decoded = SixLabors.ImageSharp.Image.Load(file);
            decoded.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(1200, 900),
                Mode = ResizeMode.Max
            }));
            using var stream = new MemoryStream();
            decoded.SaveAsBmp(stream);
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return (bitmap, $"{Path.GetFileName(file)}   /   SOURCE PREVIEW");
        }
        catch { return (null, ""); }
    }

    public void CancelBatch()
    {
        cancellation?.Cancel();
        CancelEnabled = false;
        PhaseText = "CANCELING";
        StatusText = "Finishing the current operation. Saved files stay in the run folder.";
    }

    public async Task ResizeFolderAsync()
    {
        if (Busy) return;
        string input = SourceFolder, output = DestinationFolder;
        int targetWidth = Width, targetHeight = Height;
        OutputFormat format = OutputFormat;
        string suffix = SanitizeSuffix(NamingSuffix);
        try
        {
            if (!Directory.Exists(input)) { ShowError("Choose an existing input folder."); return; }
            if (string.IsNullOrWhiteSpace(output)) { ShowError("Choose a destination folder before resizing."); return; }
            if (SameFolder(input, output)) { ShowError("Choose a destination different from the source folder."); return; }
        }
        catch (Exception ex) { ShowError($"Invalid destination: {ex.Message}"); return; }

        int currentRun = ++runVersion;
        RunFolder = null;
        SetBusy(true);
        cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        ProgressValue = 0;
        PhaseText = "CHECKING YOUR IMAGES";
        StatusText = "Checking dimensions and available memory…";
        int saved = 0, processed = 0;
        var errors = new ConcurrentBag<string>();
        try
        {
            (string[] Paths, List<BatchWorkItem> Items) planned = await Task.Run(() =>
            {
                string[] paths = Directory.EnumerateFiles(input).Where(IsSupportedImage)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
                var items = new List<BatchWorkItem>();
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    if (BatchSafety.TryPlan(path, targetWidth, targetHeight, out BatchWorkItem item, out string error)) items.Add(item);
                    else errors.Add($"{Path.GetFileName(path)} — {error}");
                }
                return (paths, items);
            }, token);
            token.ThrowIfCancellationRequested();
            if (planned.Items.Count == 0)
            {
                ShowError(planned.Paths.Length == 0
                    ? "No supported images. Choose another source."
                    : "No images passed safety checks. Review errors or reduce dimensions.");
                return;
            }
            RunFolder = BatchSafety.CreateNewRunDirectory(output);
            PhaseText = "RESIZING / IN PROGRESS";
            StatusText = $"0 / {planned.Paths.Length} saved · You can cancel this batch.";
            int streamingCount = planned.Items.Count(item => item.UsesStreamingPipeline);
            if (streamingCount > 0)
                StatusText = $"{streamingCount} large image{(streamingCount == 1 ? "" : "s")} will use low-memory mode, one at a time.";
            if (CancelOnProcessingForTest) CancelBatch();
            int lastProcessed = 0;
            var reporter = new Progress<(int Done, int Saved)>(p =>
            {
                if (!Busy || currentRun != runVersion || p.Done < lastProcessed || token.IsCancellationRequested) return;
                lastProcessed = p.Done;
                ProgressValue = Math.Clamp(p.Done * 100 / planned.Items.Count, 0, 100);
                StatusText = $"{p.Saved} / {planned.Paths.Length} saved · {p.Done} checked · {errors.Count} errors";
            });
            string extension = format.Extension();
            await Parallel.ForEachAsync(planned.Items.Select((item, index) => (item, index)),
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = BatchSafety.GetParallelism(planned.Items) },
                (entry, ct) =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        string name = $"{entry.index + 1:D4}_{Path.GetFileNameWithoutExtension(entry.item.Path)}{suffix}_{targetWidth}x{targetHeight}.{extension}";
                        string destinationPath = Path.Combine(RunFolder, name);
                        if (entry.item.UsesStreamingPipeline)
                        {
                            LargeImageBridge.Resize(entry.item.Path, destinationPath, targetWidth, targetHeight, format);
                        }
                        else
                        {
                            RgbaImage source = ImageSharpBridge.Load(entry.item.Path);
                            ct.ThrowIfCancellationRequested();
                            RgbaImage resized = ScaleAwareBicubicResizer.Resize(source, targetWidth, targetHeight);
                            ct.ThrowIfCancellationRequested();
                            ImageSharpBridge.Save(resized, destinationPath, format);
                        }
                        Interlocked.Increment(ref saved);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { errors.Add($"{Path.GetFileName(entry.item.Path)} — {ex.Message}"); }
                    finally
                    {
                        int done = Interlocked.Increment(ref processed);
                        ((IProgress<(int, int)>)reporter).Report((done, Volatile.Read(ref saved)));
                    }
                    return ValueTask.CompletedTask;
                });
            token.ThrowIfCancellationRequested();
            ProgressValue = 100;
            PhaseText = errors.IsEmpty ? "BATCH COMPLETE / NICE WORK" : "COMPLETE WITH ERRORS";
            StatusText = $"{saved} / {planned.Paths.Length} images saved · {errors.Count} errors";
            batchCompletedSuccess = (planned.Paths.Length, saved);
        }
        catch (OperationCanceledException)
        {
            PhaseText = "BATCH CANCELED";
            StatusText = $"Canceled · {saved} images saved · {errors.Count} errors. You can start a new batch.";
        }
        catch (Exception ex) { ShowError($"Could not finish this batch: {ex.Message}"); }
        finally
        {
            string[] errorList = errors.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            cancellation.Dispose();
            cancellation = null;
            SetBusy(false);
            if (batchCompletedSuccess is (int total, int savedCount))
            {
                batchCompletedSuccess = null;
                BatchCompleted?.Invoke(new BatchSummary(HasErrors: errorList.Length > 0, Saved: savedCount, Total: total, ErrorCount: errorList.Length, Errors: errorList, RunFolder: RunFolder));
            }
        }
    }

    // Set inside the try only when Parallel.ForEachAsync finished without cancellation.
    // Deferred to the finally block so the popup opens after SetBusy has released the busy flag.
    private (int Total, int Saved)? batchCompletedSuccess;

    private void SetBusy(bool value)
    {
        Busy = value;
        StartEnabled = !value && sourceFiles.Length > 0 && Directory.Exists(SourceFolder);
        CancelEnabled = value;
        OpenEnabled = !value && RunFolder is not null && Directory.Exists(RunFolder);
        if (value) ++previewVersion;
    }

    private void ShowError(string message)
    {
        PhaseText = "NEEDS YOUR ATTENTION";
        StatusText = message;
    }

    /// Trims and strips characters that would produce an invalid file name; keeps letters,
    /// digits, hyphen, underscore, and dot. Falls back to empty if nothing survives.
    private static string SanitizeSuffix(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        char[] invalid = Path.GetInvalidFileNameChars();
        var clean = new string(input.Trim().Where(c => !invalid.Contains(c) && c is not '/' and not '\\').ToArray());
        return string.IsNullOrEmpty(clean) ? "" : (clean.StartsWith('_') ? clean : "_" + clean);
    }

    private static bool SameFolder(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff";
}
