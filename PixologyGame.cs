using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Pixology;

public sealed class PixologyGame : Game
{
    private const int ButtonWidth = 220;
    private const int ButtonHeight = 64;
    private const int ButtonGap = 24;
    private const int CanvasSize = 32;
    private const int ToolbarHeight = 56;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;
    private Texture2D _canvas = null!;
    private Texture2D _checkerboard = null!;
    private Color[] _canvasPixels = null!;
    private bool _isEditing;
    private bool _showGrid;
    private string _saveMessage = string.Empty;
    private MouseState _previousMouse;

    public PixologyGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 640
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Pixology";
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("PixelFont");
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _canvasPixels = new Color[CanvasSize * CanvasSize];
        _canvas = new Texture2D(GraphicsDevice, CanvasSize, CanvasSize);
        _canvas.SetData(_canvasPixels);
        _checkerboard = new Texture2D(GraphicsDevice, CanvasSize, CanvasSize);

        var checkerboardPixels = new Color[CanvasSize * CanvasSize];
        for (var y = 0; y < CanvasSize; y++)
        {
            for (var x = 0; x < CanvasSize; x++)
            {
                var isLight = ((x / 8) + (y / 8)) % 2 == 0;
                checkerboardPixels[y * CanvasSize + x] = isLight
                    ? new Color(238, 238, 238)
                    : Color.White;
            }
        }

        _checkerboard.SetData(checkerboardPixels);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        var mouse = Mouse.GetState();
        if (mouse.LeftButton == ButtonState.Pressed &&
            _previousMouse.LeftButton == ButtonState.Released)
        {
            if (_isEditing)
                HandleEditorClick(mouse.Position);
            else
                HandleStartScreenClick(mouse.Position);
        }

        _previousMouse = mouse;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.White);

        var mouse = Mouse.GetState();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_isEditing)
            DrawEditor(mouse);
        else
            DrawStartScreen(mouse);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawStartScreen(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        var firstButton = new Rectangle(
            (viewport.Width - (ButtonWidth * 2 + ButtonGap)) / 2,
            (viewport.Height - ButtonHeight) / 2,
            ButtonWidth,
            ButtonHeight);
        var secondButton = firstButton;
        secondButton.X += ButtonWidth + ButtonGap;

        DrawButton(firstButton, "New", mouse);
        DrawButton(secondButton, "Open File", mouse);
    }

    private void DrawEditor(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        DrawRectangle(new Rectangle(0, 0, viewport.Width, ToolbarHeight), new Color(246, 246, 246));
        DrawRectangle(new Rectangle(0, ToolbarHeight - 1, viewport.Width, 1), new Color(210, 210, 210));

        var saveButton = new Rectangle(12, 8, 92, 40);
        DrawButton(saveButton, "Save", mouse);

        var checkbox = new Rectangle(124, 17, 22, 22);
        DrawRectangle(checkbox, Color.White);
        DrawOutline(checkbox, Color.Black, 2);
        if (_showGrid)
        {
            DrawRectangle(new Rectangle(checkbox.X + 4, checkbox.Y + 10, 5, 4), Color.Black);
            DrawRectangle(new Rectangle(checkbox.X + 8, checkbox.Y + 13, 4, 6), Color.Black);
            DrawRectangle(new Rectangle(checkbox.X + 11, checkbox.Y + 8, 7, 4), Color.Black);
            DrawRectangle(new Rectangle(checkbox.X + 15, checkbox.Y + 5, 4, 5), Color.Black);
        }

        _spriteBatch.DrawString(_font, "Show Grid", new Vector2(154, 17), Color.Black);
        if (_saveMessage.Length > 0)
        {
            var messageSize = _font.MeasureString(_saveMessage);
            _spriteBatch.DrawString(
                _font,
                _saveMessage,
                new Vector2(viewport.Width - messageSize.X - 12, 17),
                Color.Black);
        }

        var canvasBounds = GetCanvasBounds(viewport);
        _spriteBatch.Draw(_checkerboard, canvasBounds, Color.White);
        _spriteBatch.Draw(_canvas, canvasBounds, Color.White);
        if (_showGrid)
            DrawCanvasGrid(canvasBounds);
    }

    private void DrawCanvasGrid(Rectangle canvasBounds)
    {
        var cellWidth = canvasBounds.Width / 2;
        var cellHeight = canvasBounds.Height / 2;
        var gridColor = new Color(0, 0, 0, 110);

        DrawRectangle(new Rectangle(canvasBounds.X + cellWidth - 1, canvasBounds.Y, 2, canvasBounds.Height), gridColor);
        DrawRectangle(new Rectangle(canvasBounds.X, canvasBounds.Y + cellHeight - 1, canvasBounds.Width, 2), gridColor);
        DrawOutline(canvasBounds, gridColor, 2);
    }

    private void HandleStartScreenClick(Point position)
    {
        var viewport = GraphicsDevice.Viewport;
        var firstButton = new Rectangle(
            (viewport.Width - (ButtonWidth * 2 + ButtonGap)) / 2,
            (viewport.Height - ButtonHeight) / 2,
            ButtonWidth,
            ButtonHeight);

        if (firstButton.Contains(position))
        {
            Array.Clear(_canvasPixels);
            _canvas.SetData(_canvasPixels);
            _showGrid = false;
            _saveMessage = string.Empty;
            _isEditing = true;
        }
    }

    private void HandleEditorClick(Point position)
    {
        if (new Rectangle(12, 8, 92, 40).Contains(position))
        {
            SaveCanvas();
            return;
        }

        if (new Rectangle(124, 17, 22, 22).Contains(position) ||
            new Rectangle(154, 14, 110, 28).Contains(position))
        {
            _showGrid = !_showGrid;
        }
    }

    private void SaveCanvas()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var filePath = Path.Combine(desktopPath, "image.png");
        using var stream = File.Create(filePath);
        _canvas.SaveAsPng(stream, CanvasSize, CanvasSize);
        _saveMessage = "Saved image.png to Desktop";
    }

    private static Rectangle GetCanvasBounds(Viewport viewport)
    {
        var availableHeight = viewport.Height - ToolbarHeight;
        var scale = Math.Max(1, Math.Min(
            (viewport.Width - 64) / CanvasSize,
            (availableHeight - 64) / CanvasSize));
        var size = CanvasSize * scale;
        return new Rectangle(
            (viewport.Width - size) / 2,
            ToolbarHeight + (availableHeight - size) / 2,
            size,
            size);
    }

    private void DrawButton(Rectangle bounds, string label, MouseState mouse)
    {
        var hovered = bounds.Contains(mouse.Position);
        var fill = hovered ? new Color(242, 242, 242) : Color.White;
        const int borderWidth = 2;

        DrawRectangle(bounds, fill);
        DrawOutline(bounds, Color.Black, borderWidth);

        var textSize = _font.MeasureString(label);
        var textPosition = new Vector2(
            bounds.Center.X - textSize.X / 2,
            bounds.Center.Y - textSize.Y / 2);
        _spriteBatch.DrawString(_font, label, textPosition, Color.Black);
    }

    private void DrawRectangle(Rectangle bounds, Color color)
    {
        _spriteBatch.Draw(_pixel, bounds, color);
    }

    private void DrawOutline(Rectangle bounds, Color color, int thickness)
    {
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
        DrawRectangle(new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
        DrawRectangle(new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
    }
}
