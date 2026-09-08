# Scale-aware bicubic resize lab

This is a dependency-free algorithm test, not an application UI.

It implements a separable Catmull-Rom (`a = -0.5`) resize with:

- inverse pixel-center coordinate mapping;
- clamped borders and normalized finite weights;
- an exact area/box prefilter for strong downscales, followed by bicubic;
- linear-light, premultiplied RGBA filtering; and
- two passes with precomputed X/Y sample maps.

Run the algorithm tests from this directory:

```powershell
dotnet run -c Release
```

The test harness validates identity, flat-color preservation, alpha behavior, and a strong checkerboard downscale. It also writes two BMP previews under `bin\Release\net10.0\output`.

## Resize your own image

The command below reads JPG, PNG, WebP, BMP, and other formats ImageSharp can decode; it applies EXIF orientation, uses this project's algorithm, and writes a new PNG. The original is never changed.

```powershell
dotnet run -c Release -- "C:\path\to\image.jpg" 2000 2000
```

Optionally give the output path as the fourth argument:

```powershell
dotnet run -c Release -- "C:\path\to\image.png" 800 600 "D:\output\result.png"
```

This command currently performs exact-size resize. Aspect-ratio policies, animated GIF/SVG handling, and selectable output encoders remain deliberately outside this algorithm test.

## Large-image fallback

The normal path keeps the custom scale-aware bicubic algorithm. Batch preflight switches an image to a sequential libvips cubic pipeline only when the normal path's estimated working memory exceeds half of the available RAM. This avoids loading large source pixel buffers into managed memory, but its cubic output can differ slightly at a pixel level from the custom implementation. Images selected for this fallback make the batch run sequentially. The 100,000,000-pixel output safety limit still applies.

When encoding JPEG, transparent pixels are flattened because JPEG has no alpha channel. The WPF application offers white (default) and black JPEG backgrounds; PNG and WebP preserve transparency.

## Resize a folder (batch test)

The batch command assigns each supported static image a deterministic alphabetical number and writes it to a new `run-...` subfolder on every run, so it never overwrites earlier results. It derives safe parallelism from image dimensions and available memory, and does not recurse into child folders.

```powershell
dotnet run -c Release -- --batch "C:\images\input" 2000 2000 "C:\images\output"
```

Supported inputs are JPG, PNG, WebP, BMP, and TIFF. Animated GIF and SVG are intentionally excluded at this stage because they need frame/vector-specific rules.
