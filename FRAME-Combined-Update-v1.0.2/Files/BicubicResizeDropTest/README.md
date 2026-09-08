# MINIMA — Native Image Resize Studio

A WPF studio that accepts a folder drag-and-drop or a folder chosen with Browse. It applies the `BicubicResizeLab` algorithm to every supported static image in that folder. MINIMA includes a source preview, exact-size print ticket, presets, activity ledger and output access.

```powershell
cd D:\IMG_RESIZE\BicubicResizeDropTest
dotnet run -c Release
```

Drop a folder, set width and height, select an output format, verify the editable output base folder, then select **Resize folder**. Each batch creates a new `run-...` subfolder with numbered files in the selected format, so existing results and source files remain untouched. The Cancel button stops work between images; partial results remain isolated in that run folder. JPG, PNG, WebP, BMP, and TIFF are supported; GIF and SVG remain intentionally excluded.

Exact-size resizing can stretch images. Source preview is not an output preview.

## Large images

Normal images use FRAME's scale-aware bicubic implementation. If preflight estimates that this in-memory path would exceed half of the available RAM, FRAME automatically switches the affected source image to **low-memory mode**. This uses libvips' sequential, disk-friendly pipeline and runs the batch one image at a time, avoiding the previous per-image RAM rejection. Output dimensions and the selected output format remain unchanged; cubic interpolation may differ slightly at a pixel level from the normal bicubic path. Outputs above 100,000,000 pixels remain blocked as a separate safety limit.

## JPEG transparent-background handling

JPEG cannot store transparency. When JPG is selected, FRAME shows a background choice for transparent source pixels: **White** (the default) or **Black**. This flattening applies only to JPG; PNG and WebP retain their alpha channel unchanged.

## Visual language

Modern Soft UI: cool grey ground (`#F4F5F7`), floating white cards (radius 20,
soft drop-shadow), pill-shaped buttons and preset toggles, filled-borderless
inputs (`#F8F9FA` with focus-glow border easing to black). Headings use Segoe UI
Variable Display; numbers use Consolas. The Editorial-Georgia treatment of the
earlier revisions is gone. A single accent red (`#C84B31`) is kept only for the
small dimensions callout, so the eye lands there when scanning.

## Completion popup

There is no persistent activity log. When a batch finishes (success or
partial-success with errors), a rounded modal pops up with an animated tick,
the "N / M images saved" summary, the error list if any, and Open Output /
Close buttons. Canceling a batch does **not** trigger the popup — the user
already knows.

## Features

- **Lock aspect ratio** (🔓/🔒 toggle next to W×H): once locked, editing width scales
  height proportionally to the ratio at lock-time (and vice versa). Preset buttons
  reset the ratio to their new W:H.
- **Output format** (PNG / JPG / WEBP pills): picks the encoder for every image in the
  batch. JPG and WEBP use quality 92 by default.
- **Filename suffix** (optional textbox): inserted before the size suffix, so
  `myphoto.jpg → 0001_myphoto_resized_320x240.png`. Invalid path characters are
  stripped; empty means no extra suffix.
- **Preview pager** (< / > overlaid on the source stage, `1 / N` badge top-right):
  step through images in the folder without opening them individually. Cross-fades
  when the image changes.
- **Drag-drop feedback**: dropping a folder pulses a dashed accent border on the stage.
- **Success checkmark**: a green ✓ pops in (scale 0 → 1.2 → 1.0) when a batch completes.
- **Micro-interactions**: buttons scale to 1.02× on hover / 0.98× on press; text inputs
  ease their border color to accent on focus; preset toggles cross-fade their fill.

## Two layouts, one window

The window switches its visual structure — not just its column widths — at a
760px breakpoint (`MainWindow.xaml.cs`, `Window_SizeChanged`):

- **Landscape** (≥760px): two-column workspace (source card / print-ticket
  card) with a fixed bottom dock (status, progress, Open/Cancel/Resize).
- **Compact** (<760px): the same fields stacked in a scrolling column, mobile
  list-style — a single source of truth (`MainViewModel`) drives both, so
  resizing the window mid-batch never loses state.

## Architecture

- `MainViewModel.cs` — all business logic (folder scanning, preview
  generation, batch resize, cancellation, safety checks). Framework-agnostic
  aside from `BitmapSource` for the preview thumbnail.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — the two layouts, both bound to
  the same `MainViewModel` instance via `DataContext`.
- `Theme.cs` — the color palette (canvas/surface/ink/accent).
- `Converters.cs` — small `IValueConverter`s used by the bindings.
- `MainWindow.Verification.cs` — the `--ui-pack <dir>` headless QA harness
  (`dotnet run -- --ui-pack <dir>`), renders each state to PNG via
  `RenderTargetBitmap` and asserts the key behaviors (same-folder validation,
  batch counts/dimensions, unique run folders, corrupt-file reporting,
  cancellation recovery, compact layout, empty folder, dimension safety).

`BicubicResizeLab` (the resize algorithm, `RgbaImage`, `ImageSharpBridge`,
`BatchSafety`) is untouched by the UI framework and reused as-is.
