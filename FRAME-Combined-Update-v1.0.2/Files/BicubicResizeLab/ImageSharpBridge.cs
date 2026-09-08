using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using ImageSharpRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace BicubicResizeLab;

public enum OutputFormat { Png, Jpeg, Webp }
public enum JpegBackground { White, Black }

/// <summary>ImageSharp is used only for decoding, EXIF orientation, and PNG/JPG/WEBP encoding.</summary>
public static class ImageSharpBridge
{
    public static string Extension(this OutputFormat format) => format switch
    {
        OutputFormat.Jpeg => "jpg",
        OutputFormat.Webp => "webp",
        _ => "png"
    };
    public static (int Width, int Height) IdentifyDimensions(string path)
    {
        var info = ImageSharpImage.Identify(path)
            ?? throw new InvalidDataException("The image format could not be identified.");
        return (info.Width, info.Height);
    }

    public static RgbaImage Load(string path)
    {
        using Image<ImageSharpRgba32> decoded = ImageSharpImage.Load<ImageSharpRgba32>(path);
        decoded.Mutate(context => context.AutoOrient());

        var result = new RgbaImage(decoded.Width, decoded.Height);
        for (int y = 0; y < decoded.Height; y++)
            for (int x = 0; x < decoded.Width; x++)
            {
                ImageSharpRgba32 pixel = decoded[x, y];
                result[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A);
            }
        return result;
    }

    public static void SavePng(RgbaImage source, string path) => Save(source, path, OutputFormat.Png);

    public static void Save(RgbaImage source, string path, OutputFormat format, JpegBackground jpegBackground = JpegBackground.White)
    {
        using var encoded = new Image<ImageSharpRgba32>(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Rgba32 pixel = source[x, y];
                encoded[x, y] = new ImageSharpRgba32(pixel.R, pixel.G, pixel.B, pixel.A);
            }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        switch (format)
        {
            case OutputFormat.Jpeg:
                encoded.Mutate(context => context.BackgroundColor(jpegBackground == JpegBackground.White ? Color.White : Color.Black));
                encoded.SaveAsJpeg(stream, new JpegEncoder { Quality = 92 });
                break;
            case OutputFormat.Webp: encoded.SaveAsWebp(stream, new WebpEncoder { Quality = 92 }); break;
            default: encoded.SaveAsPng(stream); break;
        }
    }
}
