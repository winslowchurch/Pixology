# Pixology

Pixology is a MonoGame desktop app starter for a pixel-art editor.

## Run

Install the .NET 8 SDK, then run:

```sh
dotnet run
```

On the start screen, **Open File** opens an in-app browser for PNG, JPEG, and BMP images. It initially opens the Desktop, then remembers the last folder you navigated to across app restarts. Use **Home** or **Up** to navigate folders, select an image, and choose **Open**; double-click a folder or image to open it.

The editor's right panel contains a **Layers** stack. Add and select layers, adjust the selected layer's opacity, and drag layers to reorder them. Right-click a layer for **Merge Up**, **Merge Down**, or **Delete**; the final remaining layer cannot be deleted. Drawing, erasing, and bucket fills only change the selected layer; display and PNG export composite all layers. These operations can be undone with **Command+Z** (macOS) or **Ctrl+Z**.

The **Select** tool (also selectable with **V**) outlines all nontransparent pixels on the active layer. Click and drag inside the selection to move that layer's pixels; the move can be undone.

The start screen has a large, crisp, two-tone **Pixology** title on the `#FEDCC0` background, plus **New** and **Open File** buttons. The title's top half is `#863C57` and its bottom half is `#5C1B3B`. **New** opens a transparent 32x32 canvas with a toolbar. On macOS, the app window stays above normal desktop windows. UI text uses Pixel Operator fonts with hard-edged glyphs and integer scaling. The left toolbar has a **Brush** (also selectable with **B**), a fully transparent 2x2-pixel **Erase** tool (also selectable with **E**), a **Paint Bucket** (also selectable with **P**), a **Crop** tool (also selectable with **C**), and an **Eyedropper** (also selectable with **I**) that samples canvas pixels into the color picker. Crop mode initially selects the full canvas. Drag its edges or corners inward to trim or outward to expand; the selected width and height snap to 16-pixel increments and appear beside the mouse. Drag inside the selection to reposition it. Applying the crop preserves pixels in the selected area and leaves expanded areas transparent. Crop/resize can be undone with **Command+Z** (macOS) or **Ctrl+Z**. Use the color picker on the right to choose the brush and bucket color; click the hex field to select the `#RRGGBB` value, then use **Command+C**/**Command+V** on macOS or **Ctrl+C**/**Ctrl+V** elsewhere to copy or paste a color. The bucket's **Contiguous** checkbox controls whether it fills only the connected region of matching pixels or all matching pixels in the image. The cursor previews the active drawing tool's pixel size over the canvas. Click or drag on the canvas to draw or erase; **Command+Z** (macOS) or **Ctrl+Z** undoes the last action. On macOS, pinch on the trackpad to zoom; two-finger scrolling pans the workspace. You can also pan horizontally with horizontal scrolling, or drag with the middle mouse button or **Space**+left mouse button. **Show Grid** (or **G**) toggles the 16x16-pixel grid, and **Save** writes the canvas to the desktop as `image.png`. **Escape** exits fullscreen without closing the app. `Icon.ico` is used for the Windows executable icon, `Icon.bmp` for the MonoGame window icon, and `PixelFont.ttf` for the button labels.
