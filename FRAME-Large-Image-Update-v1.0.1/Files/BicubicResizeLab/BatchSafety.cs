namespace BicubicResizeLab;

public readonly record struct BatchWorkItem(
    string Path,
    int Width,
    int Height,
    long PeakBytes,
    bool UsesStreamingPipeline);

public static class BatchSafety
{
    private const long MaxDestinationPixels = 100_000_000;

    public static bool TryPlan(string path, int destinationWidth, int destinationHeight, out BatchWorkItem item, out string error)
    {
        item = default;
        error = string.Empty;
        long destinationPixels = checked((long)destinationWidth * destinationHeight);
        if (destinationPixels > MaxDestinationPixels)
        {
            error = $"Output exceeds the {MaxDestinationPixels:N0}-pixel safety limit.";
            return false;
        }

        try
        {
            (int width, int height) = ImageSharpBridge.IdentifyDimensions(path);
            long peak = EstimatePeakBytes(width, height, destinationWidth, destinationHeight);
            long budget = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 2;
            // Keep the pixel-perfect in-memory resizer for normal images. A source that would
            // exceed its working-memory budget can be handled by the sequential libvips fallback.
            bool usesStreamingPipeline = budget > 0 && peak > budget;
            item = new BatchWorkItem(path, width, height, peak, usesStreamingPipeline);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static int GetParallelism(IEnumerable<BatchWorkItem> items)
    {
        if (items.Any(item => item.UsesStreamingPipeline))
            return 1;

        long largest = items.Select(item => item.PeakBytes).DefaultIfEmpty().Max();
        long budget = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 2;
        if (budget <= 0 || largest <= 0)
            return 1;
        return Math.Clamp((int)Math.Min(4, Math.Max(1, budget / largest)), 1, 4);
    }

    public static string CreateNewRunDirectory(string outputBase)
    {
        string fullBase = Path.GetFullPath(outputBase);
        Directory.CreateDirectory(fullBase);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        for (int suffix = 0; suffix < 10_000; suffix++)
        {
            string candidate = Path.Combine(fullBase, suffix == 0 ? $"run-{stamp}" : $"run-{stamp}-{suffix:D2}");
            if (Directory.Exists(candidate))
                continue;
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        throw new IOException("Could not create a unique output run folder.");
    }

    private static long EstimatePeakBytes(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
    {
        long sourcePixels = checked((long)sourceWidth * sourceHeight);
        long destinationPixels = checked((long)destinationWidth * destinationHeight);
        int preWidth = sourceWidth > checked(destinationWidth * 2) ? Math.Min(sourceWidth, checked(destinationWidth * 2)) : sourceWidth;
        int preHeight = sourceHeight > checked(destinationHeight * 2) ? Math.Min(sourceHeight, checked(destinationHeight * 2)) : sourceHeight;
        long prePixels = checked((long)preWidth * preHeight);
        long areaHorizontalPixels = checked((long)preWidth * sourceHeight);
        long resizeHorizontalPixels = checked((long)destinationWidth * preHeight);

        // RGBA source (4 B/px) plus linear-premultiplied work buffers (32 B/px).
        return checked(sourcePixels * 36 + Math.Max(areaHorizontalPixels + prePixels, resizeHorizontalPixels) * 32 + destinationPixels * 4);
    }
}
