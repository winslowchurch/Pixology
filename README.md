# Pixology

Pixology is a MonoGame desktop app starter for a pixel-art editor.

## Run

Install the .NET 8 SDK, then run:

```sh
dotnet run
```

The start screen has **New** and **Open File** buttons. **New** opens a transparent 32x32 canvas with a toolbar. On macOS, the app window stays above normal desktop windows. UI text uses native-size Pixel Operator fonts and nearest-neighbor sampling for crisp pixel edges. The left toolbar has a **Brush** (also selectable with **B**), a fully transparent 2x2-pixel **Erase** tool (also selectable with **E**), and a **Paint Bucket** (also selectable with **P**). Use the color picker on the right to choose the brush and bucket color. The bucket's **Contiguous** checkbox controls whether it fills only the connected region of matching pixels or all matching pixels in the image. The cursor previews the active drawing tool's pixel size over the canvas. Click or drag on the canvas to draw or erase; **Command+Z** (macOS) or **Ctrl+Z** undoes the last action. On macOS, pinch on the trackpad to zoom; two-finger scrolling pans the workspace. You can also pan horizontally with horizontal scrolling, or drag with the middle mouse button or **Space**+left mouse button. **Show Grid** (or **G**) toggles the 16x16-pixel grid, and **Save** writes the canvas to the desktop as `image.png`. **Escape** exits fullscreen without closing the app. **Open File** is not wired up yet. `Icon.ico` is used for the Windows executable icon, `Icon.bmp` for the MonoGame window icon, and `PixelFont.ttf` for the button labels.
