using NetVips;

namespace BicubicResizeLab;

/// <summary>
/// Disk-friendly fallback for source images which cannot safely use the custom
/// in-memory resizer. libvips evaluates sequentially, so source pixels are
/// requested in small regions while the result is written to disk.
/// </summary>
public static class LargeImageBridge
{
    public static void Resize(
        string inputPath,
        string outputPath,
        int targetWidth,
        int targetHeight,
        OutputFormat format,
        JpegBackground jpegBackground = JpegBackground.White)
    {
        using var source = Image.NewFromFile(inputPath, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
        using var oriented = source.Autorot();
        using var resized = oriented.Resize(
            targetWidth / (double)oriented.Width,
            kernel: Enums.Kernel.Cubic,
            vscale: targetHeight / (double)oriented.Height);

        var options = new VOption();
        if (format is OutputFormat.Jpeg or OutputFormat.Webp)
            options.Add("Q", 92);

        if (format == OutputFormat.Jpeg)
        {
            double[] background = jpegBackground == JpegBackground.White ? [255, 255, 255] : [0, 0, 0];
            using var flattened = resized.Flatten(background);
            flattened.WriteToFile(outputPath, options);
        }
        else
        {
            resized.WriteToFile(outputPath, options);
        }
    }
}
