using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Pixology;

public sealed class PixologyGame : Game
{
    private const int ButtonWidth = 220;
    private const int ButtonHeight = 64;
    private const int ButtonGap = 24;
    private const int InitialCanvasSize = 32;
    private const int CropSnapSize = 16;
    private const int ToolbarHeight = 56;
    private const int SidebarWidth = 104;
    private const int RightSidebarWidth = 224;
    private const float StartScreenTextScale = 3f;
    private const float StartScreenButtonTextScale = 2f;
    private const float MinimumZoom = 0.25f;
    private const float MaximumZoom = 8f;

    private enum EditorTool
    {
        Brush,
        Erase,
        PaintBucket,
        Crop,
        Eyedropper
    }

    private enum CropDragMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private sealed record CanvasSnapshot(int Width, int Height, Color[] Pixels);
    private sealed record OpenFileEntry(string Name, string Path, bool IsDirectory);

    private enum ColorPickerDrag
    {
        None,
        SaturationValue,
        Hue
    }

    private readonly GraphicsDeviceManager _graphics;
    private readonly Stack<CanvasSnapshot> _undoHistory = new();
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private SpriteFont _titleFont = null!;
    private Texture2D _pixel = null!;
    private Texture2D _canvas = null!;
    private Texture2D _checkerboard = null!;
    private Texture2D _saturationValueTexture = null!;
    private Texture2D _hueTexture = null!;
    private RasterizerState _canvasRasterizer = null!;
    private Color[] _canvasPixels = null!;
    private int _canvasWidth = InitialCanvasSize;
    private int _canvasHeight = InitialCanvasSize;
    private bool _isEditing;
    private bool _showGrid;
    private bool _bucketContiguous = true;
    private bool _isDrawingStroke;
    private bool _strokeChanged;
    private bool _isPanning;
    private bool _isCropping;
    private bool _cropSelectionInitialized;
    private bool _hexSelected;
    private bool _showOpenFileDialog;
    private EditorTool _activeTool;
    private ColorPickerDrag _colorPickerDrag;
    private float _selectedHue;
    private float _selectedSaturation = 1f;
    private float _selectedValue = 1f;
    private Color _selectedColor = Color.Red;
    private Color[] _strokeSnapshot = Array.Empty<Color>();
    private Point _cropDragStart;
    private Rectangle _cropSelection;
    private Rectangle _cropDragSelection;
    private CropDragMode _cropDragMode;
    private readonly List<OpenFileEntry> _openFileEntries = new();
    private string _openDirectory = string.Empty;
    private string _openFileMessage = string.Empty;
    private int _selectedOpenFileEntry = -1;
    private int _openFileListOffset;
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
        _titleFont = Content.Load<SpriteFont>("PixelFontTitle");
        MakeFontPixelPerfect(_font);
        MakeFontPixelPerfect(_titleFont);
        _canvasRasterizer = new RasterizerState { ScissorTestEnable = true };
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _canvasPixels = new Color[_canvasWidth * _canvasHeight];
        RebuildCanvasTextures();
        _saturationValueTexture = new Texture2D(GraphicsDevice, 144, 144);
        _hueTexture = new Texture2D(GraphicsDevice, 20, 144);
        UpdateSaturationValueTexture();
        UpdateHueTexture();
        if (OperatingSystem.IsMacOS())
        {
            _trackpadPinch = new MacTrackpadPinch(HandleTrackpadMagnification);
            Exiting += (_, _) => _trackpadPinch.Dispose();
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (!_showOpenFileDialog && keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape) &&
            _graphics.IsFullScreen)
        {
            _graphics.IsFullScreen = false;
            _graphics.ApplyChanges();
        }

        _trackpadPinch?.TryAttach();

        if (_isEditing)
        {
            var commandOrControlDown =
                keyboard.IsKeyDown(Keys.LeftControl) ||
                keyboard.IsKeyDown(Keys.RightControl) ||
                keyboard.IsKeyDown(Keys.LeftWindows) ||
                keyboard.IsKeyDown(Keys.RightWindows);

            if (!commandOrControlDown && keyboard.IsKeyDown(Keys.B))
                _activeTool = EditorTool.Brush;
            else if (!commandOrControlDown && keyboard.IsKeyDown(Keys.E))
                _activeTool = EditorTool.Erase;
            else if (!commandOrControlDown && keyboard.IsKeyDown(Keys.P))
                _activeTool = EditorTool.PaintBucket;
            else if (!commandOrControlDown && keyboard.IsKeyDown(Keys.C) &&
                     !_previousKeyboard.IsKeyDown(Keys.C))
                ActivateCropTool();
            else if (!commandOrControlDown && keyboard.IsKeyDown(Keys.I))
                _activeTool = EditorTool.Eyedropper;

            if (keyboard.IsKeyDown(Keys.G) && !_previousKeyboard.IsKeyDown(Keys.G))
                _showGrid = !_showGrid;

            HandleHexClipboardShortcuts(keyboard, commandOrControlDown);
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

        if (_showOpenFileDialog)
        {
            UpdateOpenFileDialog(mouse, keyboard, justPressedLeft);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

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
            if (_activeTool == EditorTool.Crop && _cropSelectionInitialized &&
                GetWorkspaceBounds(GraphicsDevice.Viewport).Contains(mouse.Position))
                BeginCrop(mouse.Position);
            else if (_activeTool == EditorTool.Eyedropper)
                SampleCanvasColor(mouse.Position);
            else if (_activeTool != EditorTool.Crop && IsInsideCanvas(mouse.Position))
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

        if (_isEditing && _isDrawingStroke && leftPressed && !_isPanning &&
            (_activeTool != EditorTool.PaintBucket || justPressedLeft))
        {
            var start = justPressedLeft ? mouse.Position : _previousMouse.Position;
            DrawOnCanvas(start, mouse.Position);
        }

        if (_isCropping && leftPressed)
            UpdateCrop(mouse.Position);

        if (_isEditing && _colorPickerDrag != ColorPickerDrag.None && leftPressed)
            UpdateColorPicker(mouse.Position);
        if (!leftPressed)
            _colorPickerDrag = ColorPickerDrag.None;

        if (_isDrawingStroke && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishStroke();
        if (_isCropping && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishCrop();

        if (_isPanning && !middlePressed && !(leftPressed && spacePressed))
            _isPanning = false;

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_isEditing ? Color.White : PixologyPalette.TitleBackground);

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
            if (_showOpenFileDialog)
                DrawOpenFileDialog(mouse);
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
        var titleSize = _titleFont.MeasureString(title) * StartScreenTextScale;
        var titlePosition = new Vector2(
            MathF.Round((viewport.Width - titleSize.X) / 2),
            MathF.Round(firstButton.Y - titleSize.Y - 28));
        DrawTwoToneTitle(title, titlePosition, titleSize);

        DrawButton(firstButton, "New", mouse, StartScreenButtonTextScale);
        DrawButton(secondButton, "Open File", mouse, StartScreenButtonTextScale);
    }

    private void DrawOpenFileDialog(MouseState mouse)
    {
        var dialog = GetOpenFileDialogBounds();
        DrawRectangle(dialog, new Color(246, 246, 246));
        DrawOutline(dialog, Color.Black, 2);
        DrawPixelText("Select an image", new Vector2(dialog.X + 16, dialog.Y + 12), Color.Black);
        DrawPixelText(
            FitText(_openDirectory, dialog.Width - 32),
            new Vector2(dialog.X + 16, dialog.Y + 40),
            Color.Black);

        var upButton = new Rectangle(dialog.X + 16, dialog.Y + 66, 76, 30);
        var homeButton = new Rectangle(upButton.Right + 8, upButton.Y, 84, 30);
        DrawButton(upButton, "Up", mouse);
        DrawButton(homeButton, "Home", mouse);

        var listBounds = GetOpenFileListBounds(dialog);
        DrawRectangle(listBounds, Color.White);
        DrawOutline(listBounds, Color.Black, 1);
        const int rowHeight = 24;
        var visibleCount = Math.Max(0, listBounds.Height / rowHeight);
        for (var row = 0; row < visibleCount; row++)
        {
            var entryIndex = _openFileListOffset + row;
            if (entryIndex >= _openFileEntries.Count)
                break;

            var rowBounds = new Rectangle(
                listBounds.X + 1,
                listBounds.Y + 1 + row * rowHeight,
                listBounds.Width - 2,
                rowHeight);
            if (entryIndex == _selectedOpenFileEntry)
                DrawRectangle(rowBounds, new Color(190, 215, 245));

            var entry = _openFileEntries[entryIndex];
            var label = entry.IsDirectory ? $"[Folder] {entry.Name}" : entry.Name;
            DrawPixelText(FitText(label, rowBounds.Width - 12), new Vector2(rowBounds.X + 6, rowBounds.Y + 3), Color.Black);
        }

        if (_openFileMessage.Length > 0)
            DrawPixelText(FitText(_openFileMessage, dialog.Width - 32), new Vector2(dialog.X + 16, listBounds.Bottom + 3), Color.DarkRed);

        var buttonY = dialog.Bottom - 44;
        var openButton = new Rectangle(dialog.Right - 216, buttonY, 92, 32);
        var cancelButton = new Rectangle(dialog.Right - 112, buttonY, 96, 32);
        DrawButton(openButton, "Open", mouse);
        DrawButton(cancelButton, "Cancel", mouse);
    }

    private void UpdateOpenFileDialog(MouseState mouse, KeyboardState keyboard, bool justPressedLeft)
    {
        if (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape))
        {
            _showOpenFileDialog = false;
            return;
        }

        if (keyboard.IsKeyDown(Keys.Back) && !_previousKeyboard.IsKeyDown(Keys.Back))
            NavigateToParentDirectory();

        if (keyboard.IsKeyDown(Keys.Up) && !_previousKeyboard.IsKeyDown(Keys.Up))
            SelectOpenFileEntry(Math.Max(0, _selectedOpenFileEntry - 1));
        else if (keyboard.IsKeyDown(Keys.Down) && !_previousKeyboard.IsKeyDown(Keys.Down))
            SelectOpenFileEntry(Math.Min(_openFileEntries.Count - 1, _selectedOpenFileEntry + 1));
        else if (keyboard.IsKeyDown(Keys.Enter) && !_previousKeyboard.IsKeyDown(Keys.Enter))
            OpenSelectedFileEntry();

        if (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue)
        {
            var scrollDirection = Math.Sign(_previousMouse.ScrollWheelValue - mouse.ScrollWheelValue);
            SetOpenFileListOffset(_openFileListOffset + scrollDirection);
        }

        if (!justPressedLeft)
            return;

        var dialog = GetOpenFileDialogBounds();
        var upButton = new Rectangle(dialog.X + 16, dialog.Y + 66, 76, 30);
        var homeButton = new Rectangle(upButton.Right + 8, upButton.Y, 84, 30);
        if (upButton.Contains(mouse.Position))
        {
            NavigateToParentDirectory();
            return;
        }

        if (homeButton.Contains(mouse.Position))
        {
            NavigateToHomeDirectory();
            return;
        }

        var listBounds = GetOpenFileListBounds(dialog);
        if (listBounds.Contains(mouse.Position))
        {
            const int rowHeight = 24;
            var row = (mouse.Y - listBounds.Y - 1) / rowHeight;
            var entryIndex = _openFileListOffset + row;
            if (row >= 0 && entryIndex < _openFileEntries.Count)
                SelectOpenFileEntry(entryIndex);
            return;
        }

        var buttonY = dialog.Bottom - 44;
        if (new Rectangle(dialog.Right - 216, buttonY, 92, 32).Contains(mouse.Position))
            OpenSelectedFileEntry();
        else if (new Rectangle(dialog.Right - 112, buttonY, 96, 32).Contains(mouse.Position))
            _showOpenFileDialog = false;
    }

    private void ShowOpenFileDialog()
    {
        var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        _openDirectory = Directory.Exists(picturesDirectory)
            ? picturesDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _selectedOpenFileEntry = -1;
        _openFileListOffset = 0;
        _openFileMessage = string.Empty;
        _showOpenFileDialog = true;
        RefreshOpenFileEntries();
    }

    private void RefreshOpenFileEntries()
    {
        _openFileEntries.Clear();
        _selectedOpenFileEntry = -1;
        _openFileListOffset = 0;
        try
        {
            var directories = Directory.GetDirectories(_openDirectory)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            _openFileEntries.AddRange(directories.Select(path =>
                new OpenFileEntry(Path.GetFileName(path), path, true)));

            var files = Directory.GetFiles(_openDirectory)
                .Where(IsSupportedImageFile)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            _openFileEntries.AddRange(files.Select(path =>
                new OpenFileEntry(Path.GetFileName(path), path, false)));

            _openFileMessage = _openFileEntries.Count == 0 ? "No supported images in this folder." : string.Empty;
        }
        catch (UnauthorizedAccessException exception)
        {
            _openFileMessage = $"Cannot access folder: {exception.Message}";
        }
        catch (IOException exception)
        {
            _openFileMessage = $"Cannot list folder: {exception.Message}";
        }
    }

    private static bool IsSupportedImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";
    }

    private void SelectOpenFileEntry(int index)
    {
        if (_openFileEntries.Count == 0)
        {
            _selectedOpenFileEntry = -1;
            return;
        }

        _selectedOpenFileEntry = Math.Clamp(index, 0, _openFileEntries.Count - 1);
        var visibleRows = Math.Max(1, GetOpenFileListBounds(GetOpenFileDialogBounds()).Height / 24);
        if (_selectedOpenFileEntry < _openFileListOffset)
            _openFileListOffset = _selectedOpenFileEntry;
        else if (_selectedOpenFileEntry >= _openFileListOffset + visibleRows)
            _openFileListOffset = _selectedOpenFileEntry - visibleRows + 1;
    }

    private void SetOpenFileListOffset(int offset)
    {
        var visibleRows = Math.Max(1, GetOpenFileListBounds(GetOpenFileDialogBounds()).Height / 24);
        _openFileListOffset = Math.Clamp(offset, 0, Math.Max(0, _openFileEntries.Count - visibleRows));
    }

    private void OpenSelectedFileEntry()
    {
        if (_selectedOpenFileEntry < 0 || _selectedOpenFileEntry >= _openFileEntries.Count)
        {
            _openFileMessage = "Select an image or folder first.";
            return;
        }

        var entry = _openFileEntries[_selectedOpenFileEntry];
        if (entry.IsDirectory)
        {
            _openDirectory = entry.Path;
            RefreshOpenFileEntries();
            return;
        }

        LoadImageFile(entry.Path);
    }

    private void NavigateToParentDirectory()
    {
        var parent = Directory.GetParent(_openDirectory);
        if (parent is null)
            return;

        _openDirectory = parent.FullName;
        RefreshOpenFileEntries();
    }

    private void NavigateToHomeDirectory()
    {
        _openDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshOpenFileEntries();
    }

    private void LoadImageFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Texture2D.FromStream(GraphicsDevice, stream);
            if (image.Width <= 0 || image.Height <= 0 || image.Width > 4096 || image.Height > 4096)
            {
                _openFileMessage = "Image dimensions must be between 1 and 4096 pixels.";
                return;
            }

            var pixels = new Color[image.Width * image.Height];
            image.GetData(pixels);
            _canvasWidth = image.Width;
            _canvasHeight = image.Height;
            _canvasPixels = pixels;
            RebuildCanvasTextures();
            _undoHistory.Clear();
            _activeTool = EditorTool.Brush;
            _cropSelectionInitialized = false;
            _showGrid = false;
            _canvasPan = Vector2.Zero;
            _zoom = 1f;
            _saveMessage = string.Empty;
            _isEditing = true;
            _showOpenFileDialog = false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _openFileMessage = $"Cannot open image: {exception.Message}";
        }
        catch (IOException exception)
        {
            _openFileMessage = $"Cannot open image: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            _openFileMessage = $"Unsupported or invalid image: {exception.Message}";
        }
        catch (ArgumentException exception)
        {
            _openFileMessage = $"Unsupported or invalid image: {exception.Message}";
        }
    }

    private Rectangle GetOpenFileDialogBounds()
    {
        var viewport = GraphicsDevice.Viewport;
        var width = Math.Min(600, Math.Max(360, viewport.Width - 32));
        var height = Math.Min(500, Math.Max(360, viewport.Height - 32));
        return new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
    }

    private static Rectangle GetOpenFileListBounds(Rectangle dialog)
    {
        var buttonY = dialog.Bottom - 44;
        return new Rectangle(dialog.X + 16, dialog.Y + 106, dialog.Width - 32, Math.Max(80, buttonY - (dialog.Y + 134)));
    }

    private string FitText(string text, int maxWidth)
    {
        if (_font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var length = text.Length;
        while (length > 0 && _font.MeasureString(text[..length] + ellipsis).X > maxWidth)
            length--;
        return length == 0 ? ellipsis : text[..length] + ellipsis;
    }

    private void DrawTwoToneTitle(string title, Vector2 position, Vector2 size)
    {
        var titleBounds = new Rectangle(
            (int)position.X,
            (int)position.Y,
            (int)MathF.Ceiling(size.X),
            (int)MathF.Ceiling(size.Y));
        var topBounds = titleBounds;
        topBounds.Height = (titleBounds.Height + 1) / 2;
        var bottomBounds = new Rectangle(
            titleBounds.X,
            titleBounds.Y + topBounds.Height,
            titleBounds.Width,
            titleBounds.Height - topBounds.Height);
        var viewportBounds = GraphicsDevice.Viewport.Bounds;
        var previousScissor = GraphicsDevice.ScissorRectangle;

        _spriteBatch.End();
        GraphicsDevice.ScissorRectangle = Rectangle.Intersect(viewportBounds, topBounds);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _canvasRasterizer);
        DrawPixelText(_titleFont, title, position, PixologyPalette.TitleTopPink, StartScreenTextScale);
        _spriteBatch.End();

        GraphicsDevice.ScissorRectangle = Rectangle.Intersect(viewportBounds, bottomBounds);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _canvasRasterizer);
        DrawPixelText(_titleFont, title, position, PixologyPalette.TitleBottomPink, StartScreenTextScale);
        _spriteBatch.End();

        GraphicsDevice.ScissorRectangle = previousScissor;
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }

    private static void MakeFontPixelPerfect(SpriteFont font)
    {
        var texture = font.Texture;
        var pixels = new byte[texture.Width * texture.Height];
        texture.GetData(pixels);
        for (var index = 0; index < pixels.Length; index++)
            pixels[index] = pixels[index] >= 128 ? byte.MaxValue : byte.MinValue;

        texture.SetData(pixels);
    }

    private void DrawEditor(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        DrawRectangle(new Rectangle(0, 0, viewport.Width, ToolbarHeight), new Color(246, 246, 246));
        DrawRectangle(new Rectangle(0, ToolbarHeight - 1, viewport.Width, 1), new Color(210, 210, 210));
        DrawRectangle(new Rectangle(0, ToolbarHeight, SidebarWidth, viewport.Height - ToolbarHeight), new Color(246, 246, 246));
        DrawRectangle(new Rectangle(SidebarWidth - 1, ToolbarHeight, 1, viewport.Height - ToolbarHeight), new Color(210, 210, 210));
        var rightSidebar = GetRightSidebarBounds(viewport);
        DrawRectangle(rightSidebar, new Color(246, 246, 246));
        DrawRectangle(new Rectangle(rightSidebar.X, rightSidebar.Y, 1, rightSidebar.Height), new Color(210, 210, 210));

        var saveButton = new Rectangle(12, 8, 92, 40);
        DrawButton(saveButton, "Save", mouse);

        DrawToolButton(new Rectangle(8, 72, SidebarWidth - 16, 56), "Brush", EditorTool.Brush, mouse);
        DrawToolButton(new Rectangle(8, 136, SidebarWidth - 16, 56), "Erase", EditorTool.Erase, mouse);
        DrawToolButton(new Rectangle(8, 200, SidebarWidth - 16, 56), "Bucket", EditorTool.PaintBucket, mouse);
        DrawToolButton(new Rectangle(8, 264, SidebarWidth - 16, 56), "Crop (C)", EditorTool.Crop, mouse);
        DrawToolButton(new Rectangle(8, 328, SidebarWidth - 16, 56), "Dropper", EditorTool.Eyedropper, mouse);

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

        DrawPixelText("Show Grid", new Vector2(154, 17), Color.Black);
        if (_activeTool == EditorTool.PaintBucket)
        {
            var contiguousCheckbox = new Rectangle(278, 17, 22, 22);
            DrawRectangle(contiguousCheckbox, Color.White);
            DrawOutline(contiguousCheckbox, Color.Black, 2);
            if (_bucketContiguous)
            {
                DrawRectangle(new Rectangle(contiguousCheckbox.X + 4, contiguousCheckbox.Y + 10, 5, 4), Color.Black);
                DrawRectangle(new Rectangle(contiguousCheckbox.X + 8, contiguousCheckbox.Y + 13, 4, 6), Color.Black);
                DrawRectangle(new Rectangle(contiguousCheckbox.X + 11, contiguousCheckbox.Y + 8, 7, 4), Color.Black);
                DrawRectangle(new Rectangle(contiguousCheckbox.X + 15, contiguousCheckbox.Y + 5, 4, 5), Color.Black);
            }

            DrawPixelText("Contiguous", new Vector2(308, 17), Color.Black);
        }
        else if (_activeTool == EditorTool.Crop)
        {
            DrawPixelText("Drag crop edges (16px snap)", new Vector2(440, 17), Color.Black);
        }
        else if (_activeTool == EditorTool.Eyedropper)
        {
            DrawPixelText("Click a pixel to pick its color", new Vector2(440, 17), Color.Black);
        }

        DrawColorPicker(rightSidebar);

        if (_saveMessage.Length > 0)
        {
            var messageSize = _font.MeasureString(_saveMessage);
            DrawPixelText(
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
        if (_activeTool == EditorTool.Crop)
            DrawCropSelection(canvasBounds, mousePosition);
        else
            DrawBrushCursor(canvasBounds, mousePosition);
    }

    private void DrawBrushCursor(Rectangle canvasBounds, Point mousePosition)
    {
        if (!TryGetCanvasPixel(mousePosition, canvasBounds, out var pixelX, out var pixelY))
            return;

        var pixelScale = canvasBounds.Width / _canvasWidth;
        var toolSize = _activeTool == EditorTool.Erase ? 2 : 1;
        var cursorBounds = new Rectangle(
            canvasBounds.X + pixelX * pixelScale,
            canvasBounds.Y + pixelY * pixelScale,
            Math.Min(toolSize, _canvasWidth - pixelX) * pixelScale,
            Math.Min(toolSize, _canvasHeight - pixelY) * pixelScale);

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
        var gridColor = new Color(0, 0, 0, 110);
        var pixelScale = canvasBounds.Width / _canvasWidth;

        for (var x = CropSnapSize; x < _canvasWidth; x += CropSnapSize)
            DrawRectangle(new Rectangle(canvasBounds.X + x * pixelScale - 1, canvasBounds.Y, 2, canvasBounds.Height), gridColor);
        for (var y = CropSnapSize; y < _canvasHeight; y += CropSnapSize)
            DrawRectangle(new Rectangle(canvasBounds.X, canvasBounds.Y + y * pixelScale - 1, canvasBounds.Width, 2), gridColor);
        DrawOutline(canvasBounds, gridColor, 2);
    }

    private void DrawCropSelection(Rectangle canvasBounds, Point mousePosition)
    {
        if (!_cropSelectionInitialized)
            return;

        var pixelScale = canvasBounds.Width / (float)_canvasWidth;
        var bounds = new Rectangle(
            (int)MathF.Round(canvasBounds.X + _cropSelection.X * pixelScale),
            (int)MathF.Round(canvasBounds.Y + _cropSelection.Y * pixelScale),
            (int)MathF.Round(_cropSelection.Width * pixelScale),
            (int)MathF.Round(_cropSelection.Height * pixelScale));
        DrawRectangle(bounds, new Color(80, 140, 220, 35));
        DrawOutline(bounds, new Color(30, 90, 180), 2);
        const int handleSize = 8;
        var halfHandle = handleSize / 2;
        var handlePoints = new[]
        {
            new Point(bounds.Left, bounds.Top),
            new Point(bounds.Center.X, bounds.Top),
            new Point(bounds.Right, bounds.Top),
            new Point(bounds.Left, bounds.Center.Y),
            new Point(bounds.Right, bounds.Center.Y),
            new Point(bounds.Left, bounds.Bottom),
            new Point(bounds.Center.X, bounds.Bottom),
            new Point(bounds.Right, bounds.Bottom)
        };
        foreach (var point in handlePoints)
        {
            var handle = new Rectangle(
                point.X - halfHandle,
                point.Y - halfHandle,
                handleSize,
                handleSize);
            DrawRectangle(handle, Color.White);
            DrawOutline(handle, new Color(30, 90, 180), 1);
        }

        var sizeLabel = $"{_cropSelection.Width} x {_cropSelection.Height}";
        var labelSize = _font.MeasureString(sizeLabel);
        var workspace = GetWorkspaceBounds(GraphicsDevice.Viewport);
        var labelPosition = new Vector2(
            Math.Clamp(mousePosition.X + 16, workspace.Left, workspace.Right - (int)labelSize.X),
            Math.Clamp(mousePosition.Y + 16, workspace.Top, workspace.Bottom - (int)labelSize.Y));
        DrawPixelText(sizeLabel, labelPosition, Color.Black);
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
            return;
        }

        var secondButton = firstButton;
        secondButton.X += ButtonWidth + ButtonGap;
        if (secondButton.Contains(position))
            ShowOpenFileDialog();
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
            _undoHistory.Push(new CanvasSnapshot(_canvasWidth, _canvasHeight, _strokeSnapshot));

        _strokeSnapshot = Array.Empty<Color>();
        _strokeChanged = false;
    }

    private void Undo()
    {
        if (_undoHistory.Count == 0)
            return;

        var snapshot = _undoHistory.Pop();
        _canvasWidth = snapshot.Width;
        _canvasHeight = snapshot.Height;
        _canvasPixels = snapshot.Pixels;
        RebuildCanvasTextures();
    }

    private void BeginCrop(Point position)
    {
        var selectionBounds = GetCropSelectionBounds();
        _cropDragMode = GetCropDragMode(position, selectionBounds);
        if (_cropDragMode == CropDragMode.None || !TryGetCropGridPoint(position, out _cropDragStart))
            return;

        _isCropping = true;
        _cropDragSelection = _cropSelection;
    }

    private void UpdateCrop(Point position)
    {
        if (TryGetCropGridPoint(position, out var cropPoint))
            _cropSelection = ResizeCropSelection(cropPoint);
    }

    private Rectangle GetCropSelectionBounds()
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        var pixelScale = canvasBounds.Width / (float)_canvasWidth;
        return new Rectangle(
            (int)MathF.Round(canvasBounds.X + _cropSelection.X * pixelScale),
            (int)MathF.Round(canvasBounds.Y + _cropSelection.Y * pixelScale),
            (int)MathF.Round(_cropSelection.Width * pixelScale),
            (int)MathF.Round(_cropSelection.Height * pixelScale));
    }

    private static CropDragMode GetCropDragMode(Point position, Rectangle bounds)
    {
        const int handleRadius = 12;
        var nearLeft = Math.Abs(position.X - bounds.Left) <= handleRadius;
        var nearRight = Math.Abs(position.X - bounds.Right) <= handleRadius;
        var nearTop = Math.Abs(position.Y - bounds.Top) <= handleRadius;
        var nearBottom = Math.Abs(position.Y - bounds.Bottom) <= handleRadius;
        var withinHorizontalBounds = position.X >= bounds.Left - handleRadius &&
                                     position.X <= bounds.Right + handleRadius;
        var withinVerticalBounds = position.Y >= bounds.Top - handleRadius &&
                                   position.Y <= bounds.Bottom + handleRadius;

        if (nearLeft && nearTop)
            return CropDragMode.TopLeft;
        if (nearRight && nearTop)
            return CropDragMode.TopRight;
        if (nearLeft && nearBottom)
            return CropDragMode.BottomLeft;
        if (nearRight && nearBottom)
            return CropDragMode.BottomRight;
        if (nearLeft && withinVerticalBounds)
            return CropDragMode.Left;
        if (nearRight && withinVerticalBounds)
            return CropDragMode.Right;
        if (nearTop && withinHorizontalBounds)
            return CropDragMode.Top;
        if (nearBottom && withinHorizontalBounds)
            return CropDragMode.Bottom;
        if (bounds.Contains(position))
            return CropDragMode.Move;
        return CropDragMode.None;
    }

    private Rectangle ResizeCropSelection(Point cropPoint)
    {
        var deltaX = cropPoint.X - _cropDragStart.X;
        var deltaY = cropPoint.Y - _cropDragStart.Y;
        var left = _cropDragSelection.Left;
        var top = _cropDragSelection.Top;
        var right = _cropDragSelection.Right;
        var bottom = _cropDragSelection.Bottom;

        switch (_cropDragMode)
        {
            case CropDragMode.Move:
                left += deltaX;
                right += deltaX;
                top += deltaY;
                bottom += deltaY;
                break;
            case CropDragMode.Left:
            case CropDragMode.TopLeft:
            case CropDragMode.BottomLeft:
                left = Math.Min(cropPoint.X, right - CropSnapSize);
                break;
        }

        if (_cropDragMode is CropDragMode.Right or CropDragMode.TopRight or CropDragMode.BottomRight)
            right = Math.Max(cropPoint.X, left + CropSnapSize);

        if (_cropDragMode is CropDragMode.Top or CropDragMode.TopLeft or CropDragMode.TopRight)
            top = Math.Min(cropPoint.Y, bottom - CropSnapSize);

        if (_cropDragMode is CropDragMode.Bottom or CropDragMode.BottomLeft or CropDragMode.BottomRight)
            bottom = Math.Max(cropPoint.Y, top + CropSnapSize);

        if (_cropDragMode == CropDragMode.Move)
        {
            left = Math.Clamp(left, -4096, 4096 - _cropDragSelection.Width);
            top = Math.Clamp(top, -4096, 4096 - _cropDragSelection.Height);
            right = left + _cropDragSelection.Width;
            bottom = top + _cropDragSelection.Height;
        }

        left = Math.Max(left, right - 4096);
        right = Math.Min(right, left + 4096);
        top = Math.Max(top, bottom - 4096);
        bottom = Math.Min(bottom, top + 4096);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private bool TryGetCropGridPoint(Point position, out Point cropPoint)
    {
        var workspace = GetWorkspaceBounds(GraphicsDevice.Viewport);
        position.X = Math.Clamp(position.X, workspace.Left, workspace.Right - 1);
        position.Y = Math.Clamp(position.Y, workspace.Top, workspace.Bottom - 1);
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        var pixelScale = canvasBounds.Width / (float)_canvasWidth;
        var x = (int)MathF.Round((position.X - canvasBounds.X) / pixelScale / CropSnapSize) * CropSnapSize;
        var y = (int)MathF.Round((position.Y - canvasBounds.Y) / pixelScale / CropSnapSize) * CropSnapSize;
        cropPoint = new Point(x, y);
        return true;
    }

    private void FinishCrop()
    {
        _isCropping = false;
        if (_cropSelection.Width <= 0 || _cropSelection.Height <= 0 ||
            (_cropSelection.X == 0 && _cropSelection.Y == 0 &&
             _cropSelection.Width == _canvasWidth && _cropSelection.Height == _canvasHeight))
            return;

        var resizedPixels = new Color[_cropSelection.Width * _cropSelection.Height];
        for (var y = 0; y < _cropSelection.Height; y++)
        {
            var sourceY = _cropSelection.Y + y;
            if (sourceY < 0 || sourceY >= _canvasHeight)
                continue;

            for (var x = 0; x < _cropSelection.Width; x++)
            {
                var sourceX = _cropSelection.X + x;
                if (sourceX >= 0 && sourceX < _canvasWidth)
                    resizedPixels[y * _cropSelection.Width + x] = _canvasPixels[sourceY * _canvasWidth + sourceX];
            }
        }

        _undoHistory.Push(new CanvasSnapshot(_canvasWidth, _canvasHeight, (Color[])_canvasPixels.Clone()));
        _canvasWidth = _cropSelection.Width;
        _canvasHeight = _cropSelection.Height;
        _canvasPixels = resizedPixels;
        RebuildCanvasTextures();
        _cropSelection = new Rectangle(0, 0, _canvasWidth, _canvasHeight);
    }

    private void ActivateCropTool()
    {
        _activeTool = EditorTool.Crop;
        _cropSelection = new Rectangle(0, 0, _canvasWidth, _canvasHeight);
        _cropSelectionInitialized = true;
        _isCropping = false;
    }

    private void SampleCanvasColor(Point position)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        if (!TryGetCanvasPixel(position, canvasBounds, out var pixelX, out var pixelY))
            return;

        _selectedColor = _canvasPixels[pixelY * _canvasWidth + pixelX];
        UpdateHsvFromSelectedColor();
    }

    private void RebuildCanvasTextures()
    {
        _canvas?.Dispose();
        _checkerboard?.Dispose();
        _canvas = new Texture2D(GraphicsDevice, _canvasWidth, _canvasHeight);
        _canvas.SetData(_canvasPixels);
        _checkerboard = new Texture2D(GraphicsDevice, _canvasWidth, _canvasHeight);

        var checkerboardPixels = new Color[_canvasWidth * _canvasHeight];
        for (var y = 0; y < _canvasHeight; y++)
        {
            for (var x = 0; x < _canvasWidth; x++)
            {
                var isLight = ((x / 8) + (y / 8)) % 2 == 0;
                checkerboardPixels[y * _canvasWidth + x] = isLight
                    ? new Color(238, 238, 238)
                    : Color.White;
            }
        }

        _checkerboard.SetData(checkerboardPixels);
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
        if (_activeTool == EditorTool.PaintBucket)
            return FillArea(x, y);

        var changed = false;
        var size = _activeTool == EditorTool.Erase ? 2 : 1;
        var color = _activeTool == EditorTool.Erase ? Color.Transparent : _selectedColor;

        for (var offsetY = 0; offsetY < size && y + offsetY < _canvasHeight; offsetY++)
        {
            for (var offsetX = 0; offsetX < size && x + offsetX < _canvasWidth; offsetX++)
            {
                var index = (y + offsetY) * _canvasWidth + x + offsetX;
                if (_canvasPixels[index] == color)
                    continue;

                _canvasPixels[index] = color;
                changed = true;
            }
        }

        return changed;
    }

    private bool FillArea(int x, int y)
    {
        var startIndex = y * _canvasWidth + x;
        var targetColor = _canvasPixels[startIndex];
        if (targetColor == _selectedColor)
            return false;

        if (!_bucketContiguous)
        {
            var changed = false;
            for (var index = 0; index < _canvasPixels.Length; index++)
            {
                if (_canvasPixels[index] != targetColor)
                    continue;

                _canvasPixels[index] = _selectedColor;
                changed = true;
            }

            return changed;
        }

        var pending = new Queue<int>();
        pending.Enqueue(startIndex);
        _canvasPixels[startIndex] = _selectedColor;

        while (pending.Count > 0)
        {
            var index = pending.Dequeue();
            var pixelX = index % _canvasWidth;
            var pixelY = index / _canvasWidth;

            if (pixelX > 0)
                AddMatchingPixel(index - 1, targetColor, pending);
            if (pixelX < _canvasWidth - 1)
                AddMatchingPixel(index + 1, targetColor, pending);
            if (pixelY > 0)
                AddMatchingPixel(index - _canvasWidth, targetColor, pending);
            if (pixelY < _canvasHeight - 1)
                AddMatchingPixel(index + _canvasWidth, targetColor, pending);
        }

        return true;
    }

    private void AddMatchingPixel(int index, Color targetColor, Queue<int> pending)
    {
        if (_canvasPixels[index] != targetColor)
            return;

        _canvasPixels[index] = _selectedColor;
        pending.Enqueue(index);
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

        x = (position.X - canvasBounds.X) * _canvasWidth / canvasBounds.Width;
        y = (position.Y - canvasBounds.Y) * _canvasHeight / canvasBounds.Height;
        return true;
    }

    private bool IsInsideCanvas(Point position)
    {
        return GetWorkspaceBounds(GraphicsDevice.Viewport).Contains(position) &&
               GetCanvasBounds(GraphicsDevice.Viewport).Contains(position);
    }

    private void HandleEditorClick(Point position)
    {
        var pickerBounds = GetColorPickerBounds(GraphicsDevice.Viewport);
        if (pickerBounds.HexBounds.Contains(position))
        {
            _hexSelected = true;
            return;
        }

        _hexSelected = false;
        if (pickerBounds.SaturationValue.Contains(position))
        {
            _colorPickerDrag = ColorPickerDrag.SaturationValue;
            UpdateColorPicker(position);
            return;
        }

        if (pickerBounds.Hue.Contains(position))
        {
            _colorPickerDrag = ColorPickerDrag.Hue;
            UpdateColorPicker(position);
            return;
        }

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

        if (new Rectangle(8, 200, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.PaintBucket;
            return;
        }

        if (new Rectangle(8, 264, SidebarWidth - 16, 56).Contains(position))
        {
            ActivateCropTool();
            return;
        }

        if (new Rectangle(8, 328, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Eyedropper;
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
            return;
        }

        if (_activeTool == EditorTool.PaintBucket &&
            (new Rectangle(278, 17, 22, 22).Contains(position) ||
             new Rectangle(308, 14, 120, 28).Contains(position)))
        {
            _bucketContiguous = !_bucketContiguous;
        }
    }

    private void HandleHexClipboardShortcuts(KeyboardState keyboard, bool commandOrControlDown)
    {
        if (!_hexSelected || !commandOrControlDown)
            return;

        if (keyboard.IsKeyDown(Keys.C) && !_previousKeyboard.IsKeyDown(Keys.C))
        {
            SetSystemClipboardText(GetSelectedHexColor());
        }
        else if (keyboard.IsKeyDown(Keys.V) && !_previousKeyboard.IsKeyDown(Keys.V))
        {
            if (!TryGetSystemClipboardText(out var clipboardText))
                return;

            var hex = clipboardText.Trim();
            if (hex.StartsWith('#'))
                hex = hex[1..];

            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                _saveMessage = "Clipboard text is not a #RRGGBB color";
                return;
            }

            _selectedColor = new Color(
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb);
            UpdateHsvFromSelectedColor();
            _saveMessage = $"Selected {GetSelectedHexColor()}";
        }
    }

    private string GetSelectedHexColor()
    {
        return $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
    }

    private void UpdateHsvFromSelectedColor()
    {
        var red = _selectedColor.R / 255f;
        var green = _selectedColor.G / 255f;
        var blue = _selectedColor.B / 255f;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;

        _selectedHue = delta == 0f
            ? 0f
            : maximum == red
                ? ((green - blue) / delta % 6f) / 6f
                : maximum == green
                    ? ((blue - red) / delta + 2f) / 6f
                    : ((red - green) / delta + 4f) / 6f;
        if (_selectedHue < 0f)
            _selectedHue += 1f;
        _selectedSaturation = maximum == 0f ? 0f : delta / maximum;
        _selectedValue = maximum;
        UpdateSaturationValueTexture();
    }

    private void SetSystemClipboardText(string text)
    {
        try
        {
            var result = OperatingSystem.IsMacOS()
                ? SdlClipboard.SetClipboardTextMac(text)
                : OperatingSystem.IsWindows()
                    ? SdlClipboard.SetClipboardTextWindows(text)
                    : SdlClipboard.SetClipboardTextLinux(text);
            _saveMessage = result == 0
                ? $"Copied {text}"
                : $"Could not copy color: {SdlClipboard.GetError()}";
        }
        catch (DllNotFoundException exception)
        {
            _saveMessage = $"Clipboard unavailable: {exception.Message}";
        }
        catch (EntryPointNotFoundException exception)
        {
            _saveMessage = $"Clipboard unavailable: {exception.Message}";
        }
    }

    private bool TryGetSystemClipboardText(out string text)
    {
        try
        {
            var clipboard = OperatingSystem.IsMacOS()
                ? SdlClipboard.GetClipboardTextMac()
                : OperatingSystem.IsWindows()
                    ? SdlClipboard.GetClipboardTextWindows()
                    : SdlClipboard.GetClipboardTextLinux();
            if (clipboard == IntPtr.Zero)
            {
                _saveMessage = $"Could not paste color: {SdlClipboard.GetError()}";
                text = string.Empty;
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUTF8(clipboard) ?? string.Empty;
                return true;
            }
            finally
            {
                if (OperatingSystem.IsMacOS())
                    SdlClipboard.FreeMac(clipboard);
                else if (OperatingSystem.IsWindows())
                    SdlClipboard.FreeWindows(clipboard);
                else
                    SdlClipboard.FreeLinux(clipboard);
            }
        }
        catch (DllNotFoundException exception)
        {
            _saveMessage = $"Clipboard unavailable: {exception.Message}";
            text = string.Empty;
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            _saveMessage = $"Clipboard unavailable: {exception.Message}";
            text = string.Empty;
            return false;
        }
    }

    private static class SdlClipboard
    {
        [DllImport("libSDL2-2.0.0.dylib", EntryPoint = "SDL_SetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SetClipboardTextMac([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport("SDL2.dll", EntryPoint = "SDL_SetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SetClipboardTextWindows([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport("libSDL2-2.0.so.0", EntryPoint = "SDL_SetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SetClipboardTextLinux([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport("libSDL2-2.0.0.dylib", EntryPoint = "SDL_GetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetClipboardTextMac();

        [DllImport("SDL2.dll", EntryPoint = "SDL_GetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetClipboardTextWindows();

        [DllImport("libSDL2-2.0.so.0", EntryPoint = "SDL_GetClipboardText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetClipboardTextLinux();

        [DllImport("libSDL2-2.0.0.dylib", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeMac(IntPtr memory);

        [DllImport("SDL2.dll", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeWindows(IntPtr memory);

        [DllImport("libSDL2-2.0.so.0", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeLinux(IntPtr memory);

        [DllImport("libSDL2-2.0.0.dylib", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetErrorMac();

        [DllImport("SDL2.dll", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetErrorWindows();

        [DllImport("libSDL2-2.0.so.0", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetErrorLinux();

        internal static string GetError()
        {
            var error = OperatingSystem.IsMacOS()
                ? GetErrorMac()
                : OperatingSystem.IsWindows()
                    ? GetErrorWindows()
                    : GetErrorLinux();
            return Marshal.PtrToStringUTF8(error) ?? "unknown SDL clipboard error";
        }
    }

    private void SaveCanvas()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var filePath = Path.Combine(desktopPath, "image.png");
        using var stream = File.Create(filePath);
        _canvas.SaveAsPng(stream, _canvasWidth, _canvasHeight);
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
            Math.Max(0, viewport.Width - SidebarWidth - RightSidebarWidth),
            Math.Max(0, viewport.Height - ToolbarHeight));
    }

    private static Rectangle GetRightSidebarBounds(Viewport viewport)
    {
        return new Rectangle(
            Math.Max(SidebarWidth, viewport.Width - RightSidebarWidth),
            ToolbarHeight,
            Math.Max(0, Math.Min(RightSidebarWidth, viewport.Width - SidebarWidth)),
            Math.Max(0, viewport.Height - ToolbarHeight));
    }

    private static (Rectangle SaturationValue, Rectangle Hue, Rectangle HexBounds, Vector2 HexPosition) GetColorPickerBounds(Viewport viewport)
    {
        var sidebar = GetRightSidebarBounds(viewport);
        var square = new Rectangle(sidebar.X + 16, sidebar.Y + 24, 144, 144);
        var hue = new Rectangle(square.Right + 8, square.Y, 20, square.Height);
        var hexBounds = new Rectangle(sidebar.X + 12, square.Bottom + 12, 132, 36);
        var hexPosition = new Vector2(hexBounds.X + 6, hexBounds.Y + 6);
        return (square, hue, hexBounds, hexPosition);
    }

    private void DrawColorPicker(Rectangle sidebar)
    {
        var bounds = GetColorPickerBounds(GraphicsDevice.Viewport);
        DrawPixelText("Color", new Vector2(sidebar.X + 16, sidebar.Y + 4), Color.Black);
        _spriteBatch.Draw(_saturationValueTexture, bounds.SaturationValue, Color.White);
        _spriteBatch.Draw(_hueTexture, bounds.Hue, Color.White);

        var hueMarkerY = bounds.Hue.Y + (int)MathF.Round(_selectedHue * (bounds.Hue.Height - 1));
        DrawOutline(new Rectangle(bounds.Hue.X - 2, hueMarkerY - 2, bounds.Hue.Width + 4, 5), Color.White, 1);
        DrawOutline(bounds.SaturationValue, Color.Black, 1);
        DrawOutline(bounds.Hue, Color.Black, 1);

        var selectedX = bounds.SaturationValue.X +
                        (int)MathF.Round(_selectedSaturation * (bounds.SaturationValue.Width - 1));
        var selectedY = bounds.SaturationValue.Y +
                        (int)MathF.Round((1f - _selectedValue) * (bounds.SaturationValue.Height - 1));
        DrawOutline(new Rectangle(selectedX - 5, selectedY - 5, 11, 11), Color.White, 2);
        DrawOutline(new Rectangle(selectedX - 2, selectedY - 2, 5, 5), Color.Black, 1);

        var hexColor = GetSelectedHexColor();
        DrawRectangle(bounds.HexBounds, _hexSelected ? new Color(50, 100, 170) : Color.White);
        DrawOutline(bounds.HexBounds, Color.Black, 1);
        DrawPixelText(hexColor, bounds.HexPosition, _hexSelected ? Color.White : Color.Black);
    }

    private void UpdateColorPicker(Point position)
    {
        var bounds = GetColorPickerBounds(GraphicsDevice.Viewport);
        if (_colorPickerDrag == ColorPickerDrag.SaturationValue)
        {
            _selectedSaturation = Math.Clamp(
                (position.X - bounds.SaturationValue.X) / (float)(bounds.SaturationValue.Width - 1),
                0f,
                1f);
            _selectedValue = 1f - Math.Clamp(
                (position.Y - bounds.SaturationValue.Y) / (float)(bounds.SaturationValue.Height - 1),
                0f,
                1f);
        }
        else if (_colorPickerDrag == ColorPickerDrag.Hue)
        {
            _selectedHue = Math.Clamp(
                (position.Y - bounds.Hue.Y) / (float)(bounds.Hue.Height - 1),
                0f,
                1f);
            UpdateSaturationValueTexture();
        }

        _selectedColor = HsvToColor(_selectedHue, _selectedSaturation, _selectedValue);
    }

    private void UpdateSaturationValueTexture()
    {
        var pixels = new Color[_saturationValueTexture.Width * _saturationValueTexture.Height];
        for (var y = 0; y < _saturationValueTexture.Height; y++)
        {
            var value = 1f - y / (float)(_saturationValueTexture.Height - 1);
            for (var x = 0; x < _saturationValueTexture.Width; x++)
            {
                var saturation = x / (float)(_saturationValueTexture.Width - 1);
                pixels[y * _saturationValueTexture.Width + x] = HsvToColor(_selectedHue, saturation, value);
            }
        }

        _saturationValueTexture.SetData(pixels);
    }

    private void UpdateHueTexture()
    {
        var pixels = new Color[_hueTexture.Width * _hueTexture.Height];
        for (var y = 0; y < _hueTexture.Height; y++)
        {
            var color = HsvToColor(y / (float)(_hueTexture.Height - 1), 1f, 1f);
            for (var x = 0; x < _hueTexture.Width; x++)
                pixels[y * _hueTexture.Width + x] = color;
        }

        _hueTexture.SetData(pixels);
    }

    private static Color HsvToColor(float hue, float saturation, float value)
    {
        var sector = hue * 6f;
        var chroma = value * saturation;
        var secondary = chroma * (1f - MathF.Abs(sector % 2f - 1f));
        var match = value - chroma;

        var (red, green, blue) = (int)sector switch
        {
            0 => (chroma, secondary, 0f),
            1 => (secondary, chroma, 0f),
            2 => (0f, chroma, secondary),
            3 => (0f, secondary, chroma),
            4 => (secondary, 0f, chroma),
            _ => (chroma, 0f, secondary)
        };

        return new Color(
            (byte)Math.Clamp((red + match) * 255f, 0f, 255f),
            (byte)Math.Clamp((green + match) * 255f, 0f, 255f),
            (byte)Math.Clamp((blue + match) * 255f, 0f, 255f));
    }

    private Rectangle GetCanvasBounds(Viewport viewport)
    {
        var workspace = GetWorkspaceBounds(viewport);
        var fitScale = Math.Max(1, Math.Min(
            (workspace.Width - 64) / _canvasWidth,
            (workspace.Height - 64) / _canvasHeight));
        if (_activeTool == EditorTool.Crop)
            fitScale = Math.Max(1, fitScale / 2);
        var pixelScale = Math.Max(1, (int)MathF.Round(fitScale * _zoom));
        var width = _canvasWidth * pixelScale;
        var height = _canvasHeight * pixelScale;
        return new Rectangle(
            workspace.X + (workspace.Width - width) / 2 + (int)MathF.Round(_canvasPan.X),
            workspace.Y + (workspace.Height - height) / 2 + (int)MathF.Round(_canvasPan.Y),
            width,
            height);
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
        DrawPixelText(label, textPosition, Color.Black);
    }

    private void DrawButton(Rectangle bounds, string label, MouseState mouse, float textScale = 1f)
    {
        var hovered = bounds.Contains(mouse.Position);
        var fill = hovered ? new Color(242, 242, 242) : Color.White;
        const int borderWidth = 2;

        DrawRectangle(bounds, fill);
        DrawOutline(bounds, Color.Black, borderWidth);

        var textSize = _font.MeasureString(label) * textScale;
        var textPosition = new Vector2(
            bounds.Center.X - textSize.X / 2,
            bounds.Center.Y - textSize.Y / 2);
        DrawPixelText(_font, label, textPosition, Color.Black, textScale);
    }

    private void DrawPixelText(string text, Vector2 position, Color color)
    {
        DrawPixelText(_font, text, position, color);
    }

    private void DrawPixelText(SpriteFont font, string text, Vector2 position, Color color, float scale = 1f)
    {
        var pixelPosition = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        _spriteBatch.DrawString(
            font,
            text,
            pixelPosition,
            color,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);
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
