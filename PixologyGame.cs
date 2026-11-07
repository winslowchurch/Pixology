using System;
using System.Collections.Generic;
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
    private const int SidebarWidth = 88;
    private const float TitleScale = 3f;
    private const float MinimumZoom = 0.25f;
    private const float MaximumZoom = 8f;

    private enum EditorTool
    {
        Brush,
        Erase
    }

    private readonly GraphicsDeviceManager _graphics;
    private readonly Stack<Color[]> _undoHistory = new();
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;
    private Texture2D _canvas = null!;
    private Texture2D _checkerboard = null!;
    private RasterizerState _canvasRasterizer = null!;
    private Color[] _canvasPixels = null!;
    private bool _isEditing;
    private bool _showGrid;
    private bool _isDrawingStroke;
    private bool _strokeChanged;
    private bool _isPanning;
    private EditorTool _activeTool;
    private Color[] _strokeSnapshot = Array.Empty<Color>();
    private Vector2 _canvasPan;
    private float _zoom = 1f;
    private MacTrackpadPinch _trackpadPinch = null!;
    private string _saveMessage = string.Empty;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

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
        _canvasRasterizer = new RasterizerState { ScissorTestEnable = true };
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
        if (OperatingSystem.IsMacOS())
        {
            _trackpadPinch = new MacTrackpadPinch(HandleTrackpadMagnification);
            Exiting += (_, _) => _trackpadPinch.Dispose();
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();

        if (_isEditing)
        {
            _trackpadPinch?.TryAttach();

            if (keyboard.IsKeyDown(Keys.B))
                _activeTool = EditorTool.Brush;
            else if (keyboard.IsKeyDown(Keys.E))
                _activeTool = EditorTool.Erase;

            if (keyboard.IsKeyDown(Keys.G) && !_previousKeyboard.IsKeyDown(Keys.G))
                _showGrid = !_showGrid;

            var commandOrControlDown =
                keyboard.IsKeyDown(Keys.LeftControl) ||
                keyboard.IsKeyDown(Keys.RightControl) ||
                keyboard.IsKeyDown(Keys.LeftWindows) ||
                keyboard.IsKeyDown(Keys.RightWindows);
            if (commandOrControlDown &&
                keyboard.IsKeyDown(Keys.Z) &&
                !_previousKeyboard.IsKeyDown(Keys.Z))
            {
                if (_isDrawingStroke)
                    FinishStroke();
                Undo();
            }
        }

        var mouse = Mouse.GetState();
        var leftPressed = mouse.LeftButton == ButtonState.Pressed;
        var middlePressed = mouse.MiddleButton == ButtonState.Pressed;
        var spacePressed = keyboard.IsKeyDown(Keys.Space);
        var justPressedLeft = leftPressed && _previousMouse.LeftButton == ButtonState.Released;
        var justPressedMiddle = middlePressed && _previousMouse.MiddleButton == ButtonState.Released;

        if (_isEditing && (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue ||
                           mouse.HorizontalScrollWheelValue != _previousMouse.HorizontalScrollWheelValue))
        {
            HandleScroll(mouse);
        }

        if (_isEditing && (justPressedMiddle || (justPressedLeft && spacePressed)))
            _isPanning = true;

        if (_isEditing && justPressedLeft && !_isPanning)
        {
            HandleEditorClick(mouse.Position);
            if (IsInsideCanvas(mouse.Position))
                BeginStroke();
        }
        else if (!_isEditing && justPressedLeft)
            HandleStartScreenClick(mouse.Position);

        if (_isEditing && _isPanning &&
            (middlePressed || (leftPressed && spacePressed)) &&
            !justPressedMiddle &&
            !(justPressedLeft && spacePressed))
        {
            _canvasPan.X += mouse.X - _previousMouse.X;
            _canvasPan.Y += mouse.Y - _previousMouse.Y;
        }

        if (_isEditing && _isDrawingStroke && leftPressed && !_isPanning)
        {
            var start = justPressedLeft ? mouse.Position : _previousMouse.Position;
            DrawOnCanvas(start, mouse.Position);
        }

        if (_isDrawingStroke && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishStroke();

        if (_isPanning && !middlePressed && !(leftPressed && spacePressed))
            _isPanning = false;

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.White);

        var mouse = Mouse.GetState();
        if (_isEditing)
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawEditor(mouse);
            _spriteBatch.End();

            GraphicsDevice.ScissorRectangle = GetWorkspaceBounds(GraphicsDevice.Viewport);
            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                rasterizerState: _canvasRasterizer);
            DrawCanvasArea(mouse.Position);
            _spriteBatch.End();
            GraphicsDevice.ScissorRectangle = GraphicsDevice.Viewport.Bounds;
        }
        else
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawStartScreen(mouse);
            _spriteBatch.End();
        }

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

        const string title = "Pixology";
        var titleSize = _font.MeasureString(title) * TitleScale;
        var titlePosition = new Vector2(
            (viewport.Width - titleSize.X) / 2,
            firstButton.Y - titleSize.Y - 28);
        _spriteBatch.DrawString(_font, title, titlePosition, new Color(0x1e, 0x58, 0xbe), 0f, Vector2.Zero, TitleScale, SpriteEffects.None, 0f);

        DrawButton(firstButton, "New", mouse);
        DrawButton(secondButton, "Open File", mouse);
    }

    private void DrawEditor(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        DrawRectangle(new Rectangle(0, 0, viewport.Width, ToolbarHeight), new Color(246, 246, 246));
        DrawRectangle(new Rectangle(0, ToolbarHeight - 1, viewport.Width, 1), new Color(210, 210, 210));
        DrawRectangle(new Rectangle(0, ToolbarHeight, SidebarWidth, viewport.Height - ToolbarHeight), new Color(246, 246, 246));
        DrawRectangle(new Rectangle(SidebarWidth - 1, ToolbarHeight, 1, viewport.Height - ToolbarHeight), new Color(210, 210, 210));

        var saveButton = new Rectangle(12, 8, 92, 40);
        DrawButton(saveButton, "Save", mouse);

        DrawToolButton(new Rectangle(8, 72, SidebarWidth - 16, 56), "Brush", EditorTool.Brush, mouse);
        DrawToolButton(new Rectangle(8, 136, SidebarWidth - 16, 56), "Erase", EditorTool.Erase, mouse);

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

    }

    private void DrawCanvasArea(Point mousePosition)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        _spriteBatch.Draw(_checkerboard, canvasBounds, Color.White);
        _spriteBatch.Draw(_canvas, canvasBounds, Color.White);
        if (_showGrid)
            DrawCanvasGrid(canvasBounds);
        DrawBrushCursor(canvasBounds, mousePosition);
    }

    private void DrawBrushCursor(Rectangle canvasBounds, Point mousePosition)
    {
        if (!TryGetCanvasPixel(mousePosition, canvasBounds, out var pixelX, out var pixelY))
            return;

        var pixelScale = canvasBounds.Width / CanvasSize;
        var toolSize = _activeTool == EditorTool.Brush ? 1 : 2;
        var cursorBounds = new Rectangle(
            canvasBounds.X + pixelX * pixelScale,
            canvasBounds.Y + pixelY * pixelScale,
            Math.Min(toolSize, CanvasSize - pixelX) * pixelScale,
            Math.Min(toolSize, CanvasSize - pixelY) * pixelScale);

        DrawOutline(cursorBounds, Color.White, 4);
        if (cursorBounds.Width > 4 && cursorBounds.Height > 4)
        {
            var innerBounds = new Rectangle(
                cursorBounds.X + 2,
                cursorBounds.Y + 2,
                cursorBounds.Width - 4,
                cursorBounds.Height - 4);
            DrawOutline(innerBounds, Color.Black, 2);
        }
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
            _activeTool = EditorTool.Brush;
            _undoHistory.Clear();
            _canvasPan = Vector2.Zero;
            _zoom = 1f;
            _saveMessage = string.Empty;
            _isEditing = true;
        }
    }

    private void BeginStroke()
    {
        _isDrawingStroke = true;
        _strokeChanged = false;
        _strokeSnapshot = (Color[])_canvasPixels.Clone();
    }

    private void FinishStroke()
    {
        _isDrawingStroke = false;
        if (_strokeChanged)
            _undoHistory.Push(_strokeSnapshot);

        _strokeSnapshot = Array.Empty<Color>();
        _strokeChanged = false;
    }

    private void Undo()
    {
        if (_undoHistory.Count == 0)
            return;

        _canvasPixels = _undoHistory.Pop();
        _canvas.SetData(_canvasPixels);
    }

    private void DrawOnCanvas(Point start, Point end)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        var x = start.X;
        var y = start.Y;
        var deltaX = Math.Abs(end.X - start.X);
        var stepX = start.X < end.X ? 1 : -1;
        var deltaY = -Math.Abs(end.Y - start.Y);
        var stepY = start.Y < end.Y ? 1 : -1;
        var error = deltaX + deltaY;
        var changed = false;

        while (true)
        {
            if (TryGetCanvasPixel(new Point(x, y), canvasBounds, out var pixelX, out var pixelY))
                changed |= ApplyTool(pixelX, pixelY);

            if (x == end.X && y == end.Y)
                break;

            var doubledError = 2 * error;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }

        if (changed)
        {
            _canvas.SetData(_canvasPixels);
            _strokeChanged = true;
        }
    }

    private bool ApplyTool(int x, int y)
    {
        var changed = false;
        var size = _activeTool == EditorTool.Brush ? 1 : 2;
        var color = _activeTool == EditorTool.Brush ? Color.Black : Color.Transparent;

        for (var offsetY = 0; offsetY < size && y + offsetY < CanvasSize; offsetY++)
        {
            for (var offsetX = 0; offsetX < size && x + offsetX < CanvasSize; offsetX++)
            {
                var index = (y + offsetY) * CanvasSize + x + offsetX;
                if (_canvasPixels[index] == color)
                    continue;

                _canvasPixels[index] = color;
                changed = true;
            }
        }

        return changed;
    }

    private bool TryGetCanvasPixel(Point position, Rectangle canvasBounds, out int x, out int y)
    {
        if (!GetWorkspaceBounds(GraphicsDevice.Viewport).Contains(position) ||
            !canvasBounds.Contains(position))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = (position.X - canvasBounds.X) * CanvasSize / canvasBounds.Width;
        y = (position.Y - canvasBounds.Y) * CanvasSize / canvasBounds.Height;
        return true;
    }

    private bool IsInsideCanvas(Point position)
    {
        return GetWorkspaceBounds(GraphicsDevice.Viewport).Contains(position) &&
               GetCanvasBounds(GraphicsDevice.Viewport).Contains(position);
    }

    private void HandleEditorClick(Point position)
    {
        if (new Rectangle(8, 72, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Brush;
            return;
        }

        if (new Rectangle(8, 136, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Erase;
            return;
        }

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

    private void HandleScroll(MouseState mouse)
    {
        var horizontalDelta = mouse.HorizontalScrollWheelValue - _previousMouse.HorizontalScrollWheelValue;
        var verticalDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;

        _canvasPan.X -= horizontalDelta * 0.5f;
        _canvasPan.Y += verticalDelta * 0.5f;
    }

    private void HandleTrackpadMagnification(float zoomFactor)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        var mousePosition = Mouse.GetState().Position;
        var anchorX = (mousePosition.X - canvasBounds.X) / (float)canvasBounds.Width;
        var anchorY = (mousePosition.Y - canvasBounds.Y) / (float)canvasBounds.Height;
        _zoom = Math.Clamp(_zoom * zoomFactor, MinimumZoom, MaximumZoom);

        var zoomedBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        _canvasPan.X += mousePosition.X - (zoomedBounds.X + anchorX * zoomedBounds.Width);
        _canvasPan.Y += mousePosition.Y - (zoomedBounds.Y + anchorY * zoomedBounds.Height);
    }

    private static Rectangle GetWorkspaceBounds(Viewport viewport)
    {
        return new Rectangle(
            SidebarWidth,
            ToolbarHeight,
            Math.Max(0, viewport.Width - SidebarWidth),
            Math.Max(0, viewport.Height - ToolbarHeight));
    }

    private Rectangle GetCanvasBounds(Viewport viewport)
    {
        var workspace = GetWorkspaceBounds(viewport);
        var fitScale = Math.Max(1, Math.Min(
            (workspace.Width - 64) / CanvasSize,
            (workspace.Height - 64) / CanvasSize));
        var pixelScale = Math.Max(1, (int)MathF.Round(fitScale * _zoom));
        var size = CanvasSize * pixelScale;
        return new Rectangle(
            workspace.X + (workspace.Width - size) / 2 + (int)MathF.Round(_canvasPan.X),
            workspace.Y + (workspace.Height - size) / 2 + (int)MathF.Round(_canvasPan.Y),
            size,
            size);
    }

    private void DrawToolButton(Rectangle bounds, string label, EditorTool tool, MouseState mouse)
    {
        var fill = _activeTool == tool
            ? new Color(220, 232, 248)
            : bounds.Contains(mouse.Position) ? new Color(242, 242, 242) : Color.White;
        DrawRectangle(bounds, fill);
        DrawOutline(bounds, _activeTool == tool ? new Color(70, 110, 160) : Color.Black, 2);

        var textSize = _font.MeasureString(label);
        var textPosition = new Vector2(
            bounds.Center.X - textSize.X / 2,
            bounds.Center.Y - textSize.Y / 2);
        _spriteBatch.DrawString(_font, label, textPosition, Color.Black);
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
