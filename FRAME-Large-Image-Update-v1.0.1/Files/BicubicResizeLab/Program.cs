using BicubicResizeLab;
using System.Collections.Concurrent;

if (args.Length > 0)
{
    if (args[0].Equals("--batch", StringComparison.OrdinalIgnoreCase))
        await RunBatchResize(args);
    else
        RunImageResize(args);
    return;
}

string outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDirectory);

Run("Identity preserves pixels", TestIdentity);
Run("Flat colors survive strong downscale", TestFlatColor);
Run("Transparent RGB does not bleed", TestTransparentEdge);
Run("Checkerboard downscale is valid", TestCheckerboardDownscale);

Console.WriteLine($"\nAll tests passed. Preview BMP files: {outputDirectory}");

void RunImageResize(string[] commandLine)
{
    if (commandLine.Length is < 3 or > 4
        || !int.TryParse(commandLine[1], out int width) || width <= 0
        || !int.TryParse(commandLine[2], out int height) || height <= 0)
    {
        Console.Error.WriteLine("Usage: dotnet run -c Release -- <input-image> <width> <height> [output.png]");
        Environment.ExitCode = 2;
        return;
    }

    string inputPath = Path.GetFullPath(commandLine[0]);
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Input file does not exist: {inputPath}");
        Environment.ExitCode = 2;
        return;
    }

    string outputPath = commandLine.Length == 4
        ? Path.GetFullPath(commandLine[3])
        : Path.Combine(AppContext.BaseDirectory, "output", $"{Path.GetFileNameWithoutExtension(inputPath)}_{width}x{height}_bicubic.png");
    if (File.Exists(outputPath))
    {
        Console.Error.WriteLine($"Refusing to overwrite an existing file: {outputPath}");
        Environment.ExitCode = 2;
        return;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

    if (!BatchSafety.TryPlan(inputPath, width, height, out BatchWorkItem workItem, out string planningError))
    {
        Console.Error.WriteLine(planningError);
        Environment.ExitCode = 2;
        return;
    }

    Console.WriteLine($"Loading:  {inputPath}");
    Console.WriteLine($"Source:   {workItem.Width}x{workItem.Height}");
    Console.WriteLine($"Resizing: {width}x{height} ({(workItem.UsesStreamingPipeline ? "low-memory cubic" : "exact-size, scale-aware bicubic")})");
    if (workItem.UsesStreamingPipeline)
    {
        LargeImageBridge.Resize(inputPath, outputPath, width, height, OutputFormat.Png);
    }
    else
    {
        RgbaImage input = ImageSharpBridge.Load(inputPath);
        RgbaImage output = ScaleAwareBicubicResizer.Resize(input, width, height);
        ImageSharpBridge.SavePng(output, outputPath);
    }
    Console.WriteLine($"Saved:    {outputPath}");
}

async Task RunBatchResize(string[] commandLine)
{
    if (commandLine.Length is < 4 or > 5
        || !int.TryParse(commandLine[2], out int width) || width <= 0
        || !int.TryParse(commandLine[3], out int height) || height <= 0)
    {
        Console.Error.WriteLine("Usage: dotnet run -c Release -- --batch <input-folder> <width> <height> [output-folder]");
        Environment.ExitCode = 2;
        return;
    }

    string inputFolder = Path.GetFullPath(commandLine[1]);
    if (!Directory.Exists(inputFolder))
    {
        Console.Error.WriteLine($"Input folder does not exist: {inputFolder}");
        Environment.ExitCode = 2;
        return;
    }

    string outputFolder = commandLine.Length == 5
        ? Path.GetFullPath(commandLine[4])
        : Path.Combine(inputFolder, $"bicubic_{width}x{height}");
    if (string.Equals(Path.TrimEndingDirectorySeparator(inputFolder),
            Path.TrimEndingDirectorySeparator(outputFolder), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Output folder must be different from the input folder.");
        Environment.ExitCode = 2;
        return;
    }
    string[] sourceFiles;
    try
    {
        sourceFiles = Directory.EnumerateFiles(inputFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedInput).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Cannot enumerate the input folder: {exception.Message}");
        Environment.ExitCode = 2;
        return;
    }
    if (sourceFiles.Length == 0)
    {
        Console.Error.WriteLine("No supported images were found. Supported: JPG, PNG, WebP, BMP, TIFF.");
        Environment.ExitCode = 2;
        return;
    }

    var planningErrors = new ConcurrentBag<string>();
    var plannedItems = new List<BatchWorkItem>(sourceFiles.Length);
    foreach (string path in sourceFiles)
    {
        if (BatchSafety.TryPlan(path, width, height, out BatchWorkItem item, out string error))
            plannedItems.Add(item);
        else
            planningErrors.Add($"{Path.GetFileName(path)}: {error}");
    }
    BatchWorkItem[] files = plannedItems.ToArray();
    if (files.Length == 0)
    {
        Console.Error.WriteLine("No images passed the memory and dimension safety checks.");
        Environment.ExitCode = 2;
        return;
    }
    string runFolder;
    try { runFolder = BatchSafety.CreateNewRunDirectory(outputFolder); }
    catch (Exception exception) { Console.Error.WriteLine($"Cannot create output folder: {exception.Message}"); Environment.ExitCode = 2; return; }

    int parallelism = BatchSafety.GetParallelism(files);
    int completed = 0;
    var errors = new ConcurrentBag<string>(planningErrors);
    int streamingCount = files.Count(item => item.UsesStreamingPipeline);
    Console.WriteLine($"Batch: {files.Length} image(s), {width}x{height}, max {parallelism} concurrent job(s)");
    if (streamingCount > 0) Console.WriteLine($"Low-memory mode: {streamingCount} large image(s); the batch runs sequentially.");
    Console.WriteLine($"Output: {runFolder}");

    await Parallel.ForEachAsync(files.Select((workItem, index) => (workItem, index)),
        new ParallelOptions { MaxDegreeOfParallelism = parallelism },
        (item, _) =>
        {
            try
            {
                string name = $"{item.index + 1:D4}_{Path.GetFileNameWithoutExtension(item.workItem.Path)}_{width}x{height}_bicubic.png";
                string destinationPath = Path.Combine(runFolder, name);
                if (item.workItem.UsesStreamingPipeline)
                {
                    LargeImageBridge.Resize(item.workItem.Path, destinationPath, width, height, OutputFormat.Png);
                }
                else
                {
                    RgbaImage input = ImageSharpBridge.Load(item.workItem.Path);
                    RgbaImage output = ScaleAwareBicubicResizer.Resize(input, width, height);
                    ImageSharpBridge.SavePng(output, destinationPath);
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(item.workItem.Path)}: {exception.Message}");
            }
            finally
            {
                int current = Interlocked.Increment(ref completed);
                Console.WriteLine($"[{current}/{files.Length}] {Path.GetFileName(item.workItem.Path)}");
            }
            return ValueTask.CompletedTask;
        });

    Console.WriteLine($"Finished: {sourceFiles.Length - errors.Count}/{sourceFiles.Length} image(s) succeeded.");
    foreach (string error in errors.OrderBy(error => error, StringComparer.OrdinalIgnoreCase))
        Console.Error.WriteLine($"ERROR {error}");
    if (!errors.IsEmpty)
        Environment.ExitCode = 1;
}

bool IsSupportedInput(string path) => Path.GetExtension(path).ToLowerInvariant() is
    ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff";

void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS  {name}");
}

void TestIdentity()
{
    RgbaImage source = CreateGradient(17, 11);
    RgbaImage resized = ScaleAwareBicubicResizer.Resize(source, source.Width, source.Height);

    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            Assert(source[x, y].Equals(resized[x, y]), $"Identity mismatch at {x},{y}: {source[x, y]} != {resized[x, y]}");
}

void TestFlatColor()
{
    var source = new RgbaImage(401, 257);
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            source[x, y] = new Rgba32(40, 120, 210, 180);

    RgbaImage resized = ScaleAwareBicubicResizer.Resize(source, 31, 19);
    for (int y = 0; y < resized.Height; y++)
        for (int x = 0; x < resized.Width; x++)
            Assert(resized[x, y].Equals(new Rgba32(40, 120, 210, 180)), "Flat color changed during resize.");
}

void TestTransparentEdge()
{
    var source = new RgbaImage(2, 1);
    source[0, 0] = new Rgba32(0, 0, 255, 0); // Hidden blue must not contaminate the red edge.
    source[1, 0] = new Rgba32(255, 0, 0, 255);
    RgbaImage resized = ScaleAwareBicubicResizer.Resize(source, 40, 20);
    Rgba32 sample = resized[16, 10];

    Assert(sample.R > 230 && sample.B < 3, $"Transparent blue bled into edge: {sample}");
    BmpPreview.Write(Path.Combine(outputDirectory, "transparent-edge-upscale.bmp"), resized);
}

void TestCheckerboardDownscale()
{
    var source = new RgbaImage(512, 512);
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            bool white = ((x / 4) + (y / 4)) % 2 == 0;
            source[x, y] = white ? new Rgba32(255, 255, 255) : new Rgba32(0, 0, 0);
        }

    RgbaImage resized = ScaleAwareBicubicResizer.Resize(source, 47, 47);
    Assert(resized.Width == 47 && resized.Height == 47, "Incorrect output dimensions.");
    double luminanceSum = 0;
    for (int y = 0; y < resized.Height; y++)
        for (int x = 0; x < resized.Width; x++)
        {
            Rgba32 pixel = resized[x, y];
            Assert(pixel.A == 255, "Opaque input produced transparent output.");
            luminanceSum += pixel.R;
        }

    double meanLuminance = luminanceSum / (resized.Width * resized.Height);
    // A 50/50 black-white average is 0.5 in linear light, which encodes to about 188 in sRGB.
    Assert(meanLuminance is > 175 and < 200,
        $"Strong downscale aliased instead of averaging the high-frequency pattern in linear light (mean={meanLuminance:F1}).");

    BmpPreview.Write(Path.Combine(outputDirectory, "checkerboard-downscale.bmp"), resized);
}

RgbaImage CreateGradient(int width, int height)
{
    var image = new RgbaImage(width, height);
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            image[x, y] = new Rgba32((byte)(x * 255 / (width - 1)), (byte)(y * 255 / (height - 1)), 75, 255);
    return image;
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
