# Pixology

Pixology is a MonoGame desktop app starter for a pixel-art editor.

## Run

Install the .NET 8 SDK, then run:

```sh
dotnet run
```

On the start screen, **Open File** opens an in-app browser for PNG, JPEG, and BMP images. It initially opens the Desktop, then remembers the last folder you navigated to across app restarts. Use **Home** or **Up** to navigate folders, select an image, and choose **Open**; double-click a folder or image to open it.

The editor's right panel contains a **Layers** stack. Add and select layers, adjust the selected layer's opacity, and drag layers to reorder them. Right-click a layer for the available **Merge Up**/**Merge Down** actions or **Delete**; merge actions appear only when that neighboring layer exists, and the final remaining layer cannot be deleted. Drawing, erasing, and bucket fills only change the selected layer; display and PNG export composite all layers. These operations can be undone with **Command+Z** (macOS) or **Ctrl+Z**.

The **Select** tool is at the top of the left toolbar and is selectable with **S**. It outlines all nontransparent pixels on the active layer. Click and drag inside the selection to move that layer's pixels; the move can be undone.

The start screen has a large **Pixology** title in `#863C57` on the `#FEDCC0` background, plus **New** and **Open File** buttons. **New** opens a transparent 32x32 canvas with a toolbar. The interface and title use the Alice font. On macOS, the app window stays above normal desktop windows. The left toolbar has a **Select** tool (also selectable with **S**), a **Brush** (also selectable with **B**), a fully transparent 2x2-pixel **Erase** tool (also selectable with **E**), a **Paint Bucket** (also selectable with **P**), a **Crop** tool (also selectable with **C**), and an **Eyedropper** (also selectable with **I**) that samples canvas pixels into the color picker. Crop mode initially selects the full canvas. Drag its edges or corners inward to trim or outward to expand; the selected width and height snap to 16-pixel increments and appear beside the mouse. Drag inside the selection to reposition it. Applying the crop preserves pixels in the selected area and leaves expanded areas transparent. Crop/resize can be undone with **Command+Z** (macOS) or **Ctrl+Z**. Use the color picker on the right to choose the brush and bucket color; click the hex field and type or paste a `#RRGGBB` value to select a color. Use **Command+C**/**Command+V** on macOS or **Ctrl+C**/**Ctrl+V** elsewhere to copy or paste the selected color. The bucket's **Contiguous** checkbox controls whether it fills only the connected region of matching pixels or all matching pixels in the image. The cursor previews the active drawing tool's pixel size over the canvas. Click or drag on the canvas to draw or erase; **Command+Z** (macOS) or **Ctrl+Z** undoes the last action. On macOS, pinch on the trackpad to zoom; two-finger scrolling pans the workspace. You can also pan horizontally with horizontal scrolling, or drag with the middle mouse button or **Space**+left mouse button. **Show Grid** (or **G**) toggles the 16x16-pixel grid. **Save** writes a PNG to the Desktop, using `image.png` for new canvases and the opened image's original filename for files opened in the editor. **Escape** exits fullscreen without closing the app. `Icon.ico` is used for the Windows executable icon, and `Icon.bmp` for the MonoGame window icon.
