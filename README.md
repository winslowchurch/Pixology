# Pixology

Pixology is a MonoGame desktop app starter for a pixel-art editor.

## Run

Install the .NET 8 SDK, then run:

```sh
dotnet run
```

The start screen has **New** and **Open File** buttons. **New** opens a transparent 32x32 canvas with a toolbar. **Show Grid** toggles the 16x16-pixel grid, and **Save** writes the canvas to the desktop as `image.png`. **Open File** is not wired up yet. `Icon.ico` is used for the Windows executable icon, `Icon.bmp` for the MonoGame window icon, and `PixelFont.ttf` for the button labels.
