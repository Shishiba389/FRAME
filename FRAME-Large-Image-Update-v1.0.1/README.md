# FRAME Large Image Update v1.0.1

This is a source-only, copy-paste patch. It contains no executable, build output, user image, or unrelated project file.

## What it changes

- Retains FRAME's existing scale-aware bicubic path for normal images.
- Sends an image whose estimated in-memory resize would exceed half of available RAM through libvips' sequential low-memory path.
- Runs a batch sequentially when it includes one or more low-memory images.
- Preserves the exact output dimensions and the selected PNG, JPG, or WebP format.
- Keeps the 100,000,000-pixel output safety limit.

The low-memory path uses cubic interpolation. Its pixels can differ slightly from FRAME's custom bicubic path; this is limited to images that need the fallback.

## Apply

Extract this folder, open PowerShell in it, then paste:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Apply-FRAME-Large-Image-Update.ps1 -ProjectRoot D:\IMG_RESIZE
```

The script backs up every replaced source file under `.frame-large-image-backup-<timestamp>` before copying the patch, then builds the WPF app. To copy without building, add `-SkipBuild`.

## Build and distribution

The patch adds `NetVips` 3.2.0 and `NetVips.Native.win-x64` 8.18.6. `dotnet build` restores them automatically. A release build must be distributed with `runtimes\win-x64\native\libvips-42.dll`; verify that file exists in the published folder. Include the libvips third-party notices from the native NuGet package when redistributing FRAME.

## Verification performed

- Release build: `BicubicResizeDropTest` and `BicubicResizeLab`.
- Existing algorithm tests: identity, flat color, transparent edge, and strong downscale.
- Existing WPF UI pack: PNG/JPEG/WebP output, corrupt image reporting, cancellation, compact layout, and output safety limit.
- Direct libvips sequential resize smoke test: PNG output verified at 320 x 240.
