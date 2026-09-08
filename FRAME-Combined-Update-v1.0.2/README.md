# FRAME Combined Update v1.0.2

This is one complete source-only patch. Apply it directly to the original FRAME source or to a v1.0.0 installation; do **not** apply v1.0.1 first. It contains no executable, build output, or user image.

## Included updates

1. **Large-image low-memory mode**: Images whose custom in-memory resize would exceed half of available RAM use libvips' sequential pipeline. The batch becomes sequential when needed; normal images retain the custom scale-aware bicubic path.
2. **JPEG transparent-background choice**: JPEG cannot retain alpha. When JPG is selected, the interface shows **White** (default) and **Black** background choices. PNG and WebP preserve alpha and are unaffected.

The 100,000,000-pixel output safety limit remains. Low-memory cubic pixels can differ slightly from FRAME's custom bicubic output, but only for images that use the fallback.

## Apply

Extract this folder, open PowerShell in it, then paste:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Apply-FRAME-Combined-Update.ps1 -ProjectRoot D:\IMG_RESIZE
```

The script backs up every replaced source file in `.frame-combined-update-backup-<timestamp>`, then builds the WPF project. Add `-SkipBuild` to copy only.

## Dependency and distribution

The patch restores `NetVips` 3.2.0 and `NetVips.Native.win-x64` 8.18.6 automatically at build time. Distribute the published `libvips-42.dll` alongside FRAME and carry forward the package's third-party notices.

## Verification

- Release build of the WPF app and resize library.
- Existing bicubic algorithm tests.
- WPF UI pack: PNG/JPEG/WebP, white/black JPEG compositing of transparent pixels, low-memory integration, cancellation, errors, responsive layout, and dimension safety.
- SHA-256 manifest for every patch file.
