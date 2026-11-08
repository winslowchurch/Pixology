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
    private const int LayerContextMenuWidth = 176;
    private const int LayerContextMenuItemHeight = 34;
    private const int TabContextMenuWidth = 160;
    private const int TabContextMenuHeight = 38;
    private const int InitialCanvasSize = 32;
    private const int CropSnapSize = 16;
    private const int ToolbarHeight = 88;
    private const int TabStripHeight = 32;
    private const int SidebarWidth = 104;
    private const int RightSidebarWidth = 224;
    private const float MinimumZoom = 0.25f;
    private const float MaximumZoom = 8f;

    private enum EditorTool
    {
        Brush,
        Erase,
        PaintBucket,
        Crop,
        Eyedropper,
        Select
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

    private enum LayerContextMenuAction
    {
        MergeUp,
        MergeDown,
        Delete
    }

    private sealed class Layer
    {
        public required string Name { get; set; }
        public required Color[] Pixels { get; set; }
        public float Opacity { get; set; } = 1f;
    }

    private sealed class Document
    {
        public string Title = string.Empty;
        public string FilePath = string.Empty;
        public int Width;
        public int Height;
        public List<Layer> Layers = new();
        public Stack<CanvasSnapshot> UndoHistory = new();
        public int ActiveLayerIndex;
        public int LayerListOffset;
        public Rectangle SelectionBounds;
        public bool ShowGrid;
        public Rectangle CropSelection;
        public bool CropSelectionInitialized;
        public Vector2 CanvasPan;
        public float Zoom = 1f;
        public string SaveFileName = "image";
        public string SaveMessage = string.Empty;
    }

    private sealed record LayerSnapshot(string Name, float Opacity, Color[] Pixels);
    private sealed record LayerContextMenuItem(string Label, LayerContextMenuAction Action, bool Enabled);
    private sealed record CanvasSnapshot(
        int Width,
        int Height,
        LayerSnapshot[] Layers,
        int ActiveLayerIndex,
        int ChangedLayerIndex,
        Color[] ChangedLayerPixels,
        Rectangle SelectionBounds);
    private sealed record OpenFileEntry(string Name, string Path, bool IsDirectory);

    private enum ColorPickerDrag
    {
        None,
        SaturationValue,
        Hue
    }

    private readonly GraphicsDeviceManager _graphics;
    private Stack<CanvasSnapshot> _undoHistory = new();
    private List<Layer> _layers = new();
    private readonly List<Document> _documents = new();
    private int _activeDocumentIndex = -1;
    private int _firstVisibleTabIndex;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;
    private Texture2D _toolIcons = null!;
    private Texture2D _canvas = null!;
    private Texture2D _checkerboard = null!;
    private Texture2D _saturationValueTexture = null!;
    private Texture2D _hueTexture = null!;
    private RasterizerState _canvasRasterizer = null!;
    private Color[] _canvasPixels = null!;
    private Color[] _compositePixels = null!;
    private int _canvasWidth = InitialCanvasSize;
    private int _canvasHeight = InitialCanvasSize;
    private bool _isEditing;
    private bool _showGrid;
    private bool _bucketContiguous = true;
    private bool _magicErase;
    private bool _isDrawingStroke;
    private bool _strokeChanged;
    private bool _isPanning;
    private bool _isCropping;
    private bool _cropSelectionInitialized;
    private bool _hexSelected;
    private bool _hexTextSelectionActive;
    private string _hexEditText = string.Empty;
    private bool _showOpenFileDialog;
    private bool _openFileCreatesTab;
    private EditorTool _activeTool;
    private ColorPickerDrag _colorPickerDrag;
    private float _selectedHue;
    private float _selectedSaturation = 1f;
    private float _selectedValue = 1f;
    private Color _selectedColor = Color.Red;
    private CanvasSnapshot _strokeDocumentSnapshot = null!;
    private CanvasSnapshot _opacityDocumentSnapshot = null!;
    private bool _isAdjustingLayerOpacity;
    private bool _layerOpacityChanged;
    private int _activeLayerIndex;
    private int _layerListOffset;
    private Point _cropDragStart;
    private Rectangle _cropSelection;
    private Rectangle _cropDragSelection;
    private CropDragMode _cropDragMode;
    private bool _isMovingSelection;
    private bool _isCreatingSelection;
    private bool _selectionMoveChanged;
    private Point _selectionDragStart;
    private Point _selectionBoxStart;
    private Rectangle _selectionMoveBounds;
    private Point _selectionMoveOffset;
    private Rectangle _selectionBounds;
    private CanvasSnapshot _selectionMoveSnapshot = null!;
    private Color[] _copiedSelectionPixels = null!;
    private Rectangle _copiedSelectionBounds;
    private readonly List<OpenFileEntry> _openFileEntries = new();
    private string _openDirectory = string.Empty;
    private string _openFileMessage = string.Empty;
    private int _selectedOpenFileEntry = -1;
    private int _openFileListOffset;
    private int _lastFileClickEntry = -1;
    private double _lastFileClickTime;
    private Point _layerContextMenuPosition;
    private int _contextMenuLayerIndex = -1;
    private bool _isLayerContextMenuOpen;
    private bool _isTabContextMenuOpen;
    private int _contextMenuDocumentIndex = -1;
    private Point _tabContextMenuPosition;
    private bool _isDraggingLayer;
    private int _draggedLayerIndex = -1;
    private int _layerDropTargetIndex = -1;
    private Point _layerDragStartPosition;
    private bool _layerDragMoved;
    private Vector2 _canvasPan;
    private float _zoom = 1f;
    private MacTrackpadPinch _trackpadPinch = null!;
    private string _saveMessage = string.Empty;
    private string _canvasSaveFileName = "image";
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    public PixologyGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 640
        };

        Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        IsMouseVisible = true;
        Window.Title = "Pixology";
        Window.AllowUserResizing = true;
        Window.TextInput += HandleTextInput;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("AliceFont");
        _canvasRasterizer = new RasterizerState { ScissorTestEnable = true };
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _toolIcons = Content.Load<Texture2D>("ui");
        _saturationValueTexture = new Texture2D(GraphicsDevice, 144, 144);
        _hueTexture = new Texture2D(GraphicsDevice, 20, 144);
        UpdateSaturationValueTexture();
        UpdateHueTexture();
        CreateNewDocument();
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
            (_graphics.IsFullScreen || GraphicsDevice.PresentationParameters.IsFullScreen || Window.IsBorderless))
        {
            _graphics.IsFullScreen = false;
            Window.IsBorderless = false;
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

            var controlDown = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            if (controlDown && keyboard.IsKeyDown(Keys.Tab) && !_previousKeyboard.IsKeyDown(Keys.Tab) &&
                _documents.Count > 1)
            {
                var backwards = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                var nextIndex = (_activeDocumentIndex + (backwards ? _documents.Count - 1 : 1)) % _documents.Count;
                ActivateDocument(nextIndex);
            }

            if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.B))
                _activeTool = EditorTool.Brush;
            else if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.E))
                _activeTool = EditorTool.Erase;
            else if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.G))
                _activeTool = EditorTool.PaintBucket;
            else if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.C) &&
                     !_previousKeyboard.IsKeyDown(Keys.C))
                ActivateCropTool();
            else if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.I))
                _activeTool = EditorTool.Eyedropper;
            else if (!_hexSelected && !commandOrControlDown && keyboard.IsKeyDown(Keys.M) &&
                     !_previousKeyboard.IsKeyDown(Keys.M))
                ActivateSelectionTool();

            if (_hexSelected && keyboard.IsKeyDown(Keys.Back) &&
                !_previousKeyboard.IsKeyDown(Keys.Back))
                EditHexText(string.Empty, backspace: true);
            if (_hexSelected && keyboard.IsKeyDown(Keys.Enter) &&
                !_previousKeyboard.IsKeyDown(Keys.Enter))
            {
                _hexSelected = false;
                _hexTextSelectionActive = false;
            }

            HandleHexClipboardShortcuts(keyboard, commandOrControlDown);
            if (!_hexSelected && commandOrControlDown && keyboard.IsKeyDown(Keys.C) &&
                !_previousKeyboard.IsKeyDown(Keys.C))
                CopySelection();
            if (!_hexSelected && commandOrControlDown && keyboard.IsKeyDown(Keys.V) &&
                !_previousKeyboard.IsKeyDown(Keys.V))
                PasteSelection();

            if (!_hexSelected && commandOrControlDown && keyboard.IsKeyDown(Keys.D) &&
                !_previousKeyboard.IsKeyDown(Keys.D))
                ClearSelection();

            if (!_hexSelected && !commandOrControlDown &&
                (keyboard.IsKeyDown(Keys.Delete) || keyboard.IsKeyDown(Keys.Back)) &&
                !_previousKeyboard.IsKeyDown(Keys.Delete) && !_previousKeyboard.IsKeyDown(Keys.Back))
                DeleteSelectionContents();

            if (commandOrControlDown &&
                keyboard.IsKeyDown(Keys.Z) &&
                !_previousKeyboard.IsKeyDown(Keys.Z))
            {
                if (_isDrawingStroke)
                    FinishStroke();
                if (_isMovingSelection)
                    FinishSelectionMove();
                Undo();
            }
        }

        var mouse = Mouse.GetState();
        UpdateMouseCursor(mouse.Position);
        var leftPressed = mouse.LeftButton == ButtonState.Pressed;
        var middlePressed = mouse.MiddleButton == ButtonState.Pressed;
        var rightPressed = mouse.RightButton == ButtonState.Pressed;
        var spacePressed = keyboard.IsKeyDown(Keys.Space);
        var justPressedLeft = leftPressed && _previousMouse.LeftButton == ButtonState.Released;
        var justPressedRight = rightPressed && _previousMouse.RightButton == ButtonState.Released;
        var justPressedMiddle = middlePressed && _previousMouse.MiddleButton == ButtonState.Released;

        if (_showOpenFileDialog)
        {
            UpdateOpenFileDialog(mouse, keyboard, justPressedLeft, gameTime.TotalGameTime.TotalSeconds);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (_isEditing && justPressedLeft && _isTabContextMenuOpen && HandleTabContextMenuClick(mouse.Position))
        {
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (_isEditing && justPressedLeft && mouse.Y < TabStripHeight && HandleTabStripClick(mouse.Position))
        {
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (_isEditing && justPressedRight)
        {
            if (HandleTabContextMenuRightClick(mouse.Position))
                _isLayerContextMenuOpen = false;
            else
            {
                _isTabContextMenuOpen = false;
                HandleLayerPanelRightClick(mouse.Position);
            }
        }

        if (_isAdjustingLayerOpacity && leftPressed)
            UpdateLayerOpacity(mouse.Position);
        if (_isAdjustingLayerOpacity && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
        {
            _isAdjustingLayerOpacity = false;
            if (_layerOpacityChanged)
                _undoHistory.Push(_opacityDocumentSnapshot);
            _opacityDocumentSnapshot = null!;
            _layerOpacityChanged = false;
            RefreshCompositeTexture();
        }

        if (_isEditing && mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue &&
            GetLayerListBounds(GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport))).Contains(mouse.Position))
        {
            var scrollDirection = Math.Sign(_previousMouse.ScrollWheelValue - mouse.ScrollWheelValue);
            SetLayerListOffset(_layerListOffset + scrollDirection);
        }
        else if (_isEditing && (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue ||
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
            else if (_activeTool == EditorTool.Select)
                BeginSelectionInteraction(
                    mouse.Position,
                    keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            else if (_activeTool != EditorTool.Crop && IsInsideCanvas(mouse.Position))
                BeginStroke();
        }
        if (_isEditing && _isPanning &&
            (middlePressed || (leftPressed && spacePressed)) &&
            !justPressedMiddle &&
            !(justPressedLeft && spacePressed))
        {
            _canvasPan.X += mouse.X - _previousMouse.X;
            _canvasPan.Y += mouse.Y - _previousMouse.Y;
        }

        var singleClickTool = _activeTool == EditorTool.PaintBucket ||
                              (_activeTool == EditorTool.Erase && _magicErase);
        if (_isEditing && _isDrawingStroke && leftPressed && !_isPanning &&
            (!singleClickTool || justPressedLeft))
        {
            var start = justPressedLeft ? mouse.Position : _previousMouse.Position;
            DrawOnCanvas(start, mouse.Position);
        }

        if (_isCropping && leftPressed)
            UpdateCrop(mouse.Position);
        if (_isMovingSelection && leftPressed)
            UpdateSelectionMove(mouse.Position);
        if (_isCreatingSelection && leftPressed)
            UpdateSelectionBox(mouse.Position);
        if (_isDraggingLayer && leftPressed)
            UpdateLayerDrag(mouse.Position);

        if (_isEditing && _colorPickerDrag != ColorPickerDrag.None && leftPressed)
            UpdateColorPicker(mouse.Position);
        if (!leftPressed)
            _colorPickerDrag = ColorPickerDrag.None;

        if (_isDrawingStroke && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishStroke();
        if (_isCropping && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishCrop();
        if (_isMovingSelection && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishSelectionMove();
        if (_isCreatingSelection && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            _isCreatingSelection = false;
        if (_isDraggingLayer && _previousMouse.LeftButton == ButtonState.Pressed && !leftPressed)
            FinishLayerDrag();

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
            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            DrawEditor(mouse);
            _spriteBatch.End();

            GraphicsDevice.ScissorRectangle = GetWorkspaceBounds(GraphicsDevice.Viewport);
            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                rasterizerState: _canvasRasterizer);
            DrawCanvasArea(mouse.Position);
            _spriteBatch.End();
            GraphicsDevice.ScissorRectangle = GraphicsDevice.Viewport.Bounds;
            if (_showOpenFileDialog)
            {
                _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
                DrawOpenFileDialog(mouse);
                _spriteBatch.End();
            }
        }
        base.Draw(gameTime);
    }

    private void UpdateMouseCursor(Point position)
    {
        var cursor = MouseCursor.Arrow;
        if (_showOpenFileDialog)
        {
            var dialog = GetOpenFileDialogBounds();
            var upButton = new Rectangle(dialog.X + 16, dialog.Y + 66, 76, 30);
            var homeButton = new Rectangle(upButton.Right + 8, upButton.Y, 84, 30);
            var (openButton, cancelButton) = GetOpenFileDialogButtons(dialog);
            var listBounds = GetOpenFileListBounds(dialog);
            if (upButton.Contains(position) || homeButton.Contains(position) ||
                openButton.Contains(position) || cancelButton.Contains(position))
                cursor = MouseCursor.Hand;
            else if (listBounds.Contains(position))
            {
                var row = (position.Y - listBounds.Y - 1) / 24;
                var entry = _openFileListOffset + row;
                if (row >= 0 && entry < _openFileEntries.Count)
                    cursor = MouseCursor.Hand;
            }
        }
        else if (_isEditing)
        {
            var picker = GetColorPickerBounds(GraphicsDevice.Viewport);
            if (_hexSelected && picker.HexBounds.Contains(position))
                cursor = MouseCursor.IBeam;
            else
            {
                var sidebar = GetRightSidebarBounds(GraphicsDevice.Viewport);
                var layers = GetLayersPanelBounds(sidebar);
                var tabLayout = GetTabStripLayout();
                var onTab = tabLayout.NewButton.Contains(position) || tabLayout.OpenButton.Contains(position) ||
                            tabLayout.Tabs.Any(bounds => bounds.Contains(position)) ||
                            (_isTabContextMenuOpen && GetTabContextMenuBounds().Contains(position));
                var toolButtons = Enumerable.Range(0, 6)
                    .Select(index => new Rectangle(8, 104 + index * 64, SidebarWidth - 16, 56));
                var toolbarControl = new Rectangle(124, 46, 160, 28).Contains(position) ||
                                     new Rectangle(312, 46, 172, 28).Contains(position);
                var layerRows = GetLayerListBounds(layers).Contains(position);
                var menu = _isLayerContextMenuOpen &&
                           GetLayerContextMenuBounds(_layerContextMenuPosition,
                               GetLayerContextMenuItems(_contextMenuLayerIndex).Length).Contains(position);
                if (onTab || new Rectangle(12, 40, 92, 40).Contains(position) ||
                    toolButtons.Any(bounds => bounds.Contains(position)) || toolbarControl ||
                    picker.HexBounds.Contains(position) || picker.SaturationValue.Contains(position) ||
                    picker.Hue.Contains(position) || GetLayerOpacityBounds(layers).Contains(position) ||
                    layerRows || GetAddLayerButtonBounds(layers).Contains(position) || menu)
                    cursor = MouseCursor.Hand;
            }
        }
        Mouse.SetCursor(cursor);
    }

    private void DrawOpenFileDialog(MouseState mouse)
    {
        var dialog = GetOpenFileDialogBounds();
        DrawRectangle(dialog, PixologyPalette.Panel);
        DrawOutline(dialog, PixologyPalette.Ink, 2);
        DrawPixelText("Select an image", new Vector2(dialog.X + 16, dialog.Y + 12), PixologyPalette.Ink);
        DrawPixelText(
            FitText(_openDirectory, dialog.Width - 32),
            new Vector2(dialog.X + 16, dialog.Y + 40),
            PixologyPalette.Ink);

        var upButton = new Rectangle(dialog.X + 16, dialog.Y + 66, 76, 30);
        var homeButton = new Rectangle(upButton.Right + 8, upButton.Y, 84, 30);
        DrawButton(upButton, "Up", mouse);
        DrawButton(homeButton, "Home", mouse);

        var listBounds = GetOpenFileListBounds(dialog);
        DrawRectangle(listBounds, Color.White);
        DrawOutline(listBounds, PixologyPalette.Ink, 1);
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
                DrawRectangle(rowBounds, PixologyPalette.Accent);

            var entry = _openFileEntries[entryIndex];
            var labelX = rowBounds.X + 8;
            if (entry.IsDirectory)
            {
                DrawFolderIcon(new Rectangle(rowBounds.X + 7, rowBounds.Y + 6, 18, 14));
                labelX = rowBounds.X + 32;
            }
            var label = FitText(entry.Name, rowBounds.Right - labelX - 6);
            DrawPixelText(label, new Vector2(labelX, rowBounds.Y + 3), PixologyPalette.Ink);
        }

        if (_openFileMessage.Length > 0)
            DrawPixelText(FitText(_openFileMessage, dialog.Width - 32), new Vector2(dialog.X + 16, listBounds.Bottom + 3), PixologyPalette.Ink);

        var (openButton, cancelButton) = GetOpenFileDialogButtons(dialog);
        DrawButton(openButton, "Open", mouse);
        DrawButton(cancelButton, "Cancel", mouse);
    }

    private void UpdateOpenFileDialog(
        MouseState mouse,
        KeyboardState keyboard,
        bool justPressedLeft,
        double currentTime)
    {
        if (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape))
        {
            _showOpenFileDialog = false;
            _openFileCreatesTab = false;
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

        var previousFileClickEntry = _lastFileClickEntry;
        var previousFileClickTime = _lastFileClickTime;
        _lastFileClickEntry = -1;
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
            {
                SelectOpenFileEntry(entryIndex);
                if (entryIndex == previousFileClickEntry && currentTime - previousFileClickTime <= 0.5)
                {
                    OpenSelectedFileEntry();
                }
                else
                {
                    _lastFileClickEntry = entryIndex;
                    _lastFileClickTime = currentTime;
                }
            }
            return;
        }

        var (openButton, cancelButton) = GetOpenFileDialogButtons(dialog);
        if (openButton.Contains(mouse.Position))
            OpenSelectedFileEntry();
        else if (cancelButton.Contains(mouse.Position))
        {
            _showOpenFileDialog = false;
            _openFileCreatesTab = false;
        }
    }

    private void ShowOpenFileDialog(bool createNewTab = false)
    {
        _openFileCreatesTab = createNewTab;
        _openFileMessage = string.Empty;
        var preferenceMessage = string.Empty;
        if (string.IsNullOrEmpty(_openDirectory))
        {
            _openDirectory = LoadLastOpenDirectory();
            preferenceMessage = _openFileMessage;
        }

        _selectedOpenFileEntry = -1;
        _openFileListOffset = 0;
        _showOpenFileDialog = true;
        RefreshOpenFileEntries();
        if (preferenceMessage.Length > 0)
            _openFileMessage = preferenceMessage;
    }

    private string LoadLastOpenDirectory()
    {
        try
        {
            var preferencePath = GetOpenDirectoryPreferencePath();
            if (File.Exists(preferencePath))
            {
                var savedDirectory = File.ReadAllText(preferencePath).Trim();
                if (Directory.Exists(savedDirectory))
                    return savedDirectory;
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            _openFileMessage = $"Cannot read last folder setting: {exception.Message}";
        }
        catch (IOException exception)
        {
            _openFileMessage = $"Cannot read last folder setting: {exception.Message}";
        }

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desktopDirectory)
            ? desktopDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string GetOpenDirectoryPreferencePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixology",
            "last-open-directory.txt");
    }

    private void SaveLastOpenDirectory()
    {
        try
        {
            var preferencePath = GetOpenDirectoryPreferencePath();
            Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);
            File.WriteAllText(preferencePath, _openDirectory);
        }
        catch (UnauthorizedAccessException exception)
        {
            _openFileMessage = $"Cannot save last folder setting: {exception.Message}";
        }
        catch (IOException exception)
        {
            _openFileMessage = $"Cannot save last folder setting: {exception.Message}";
        }
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
            SaveLastOpenDirectory();
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
        SaveLastOpenDirectory();
    }

    private void NavigateToHomeDirectory()
    {
        _openDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshOpenFileEntries();
        SaveLastOpenDirectory();
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
            var title = Path.GetFileNameWithoutExtension(path);
            var document = new Document
            {
                Title = title,
                FilePath = Path.GetFullPath(path),
                Width = image.Width,
                Height = image.Height,
                Layers = new List<Layer> { new() { Name = title, Pixels = pixels } },
                SaveFileName = title
            };
            if (_openFileCreatesTab || _activeDocumentIndex < 0)
                AddDocument(document);
            else
            {
                StoreActiveDocument();
                var replacedIndex = _activeDocumentIndex;
                _documents[replacedIndex] = document;
                _activeDocumentIndex = -1;
                ActivateDocument(replacedIndex);
            }
            if (!_openFileCreatesTab)
                _activeTool = EditorTool.Brush;
            _showOpenFileDialog = false;
            _openFileCreatesTab = false;
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

    private void CreateNewDocument()
    {
        var title = $"Untitled {_documents.Count + 1}";
        var pixels = new Color[InitialCanvasSize * InitialCanvasSize];
        AddDocument(new Document
        {
            Title = title,
            Width = InitialCanvasSize,
            Height = InitialCanvasSize,
            Layers = new List<Layer> { new() { Name = "Layer 1", Pixels = pixels } },
            SaveFileName = title
        });
    }

    private void AddDocument(Document document)
    {
        _documents.Add(document);
        ActivateDocument(_documents.Count - 1);
    }

    private void ActivateDocument(int index)
    {
        if (index < 0 || index >= _documents.Count || index == _activeDocumentIndex)
            return;

        if (_isDrawingStroke)
            FinishStroke();
        if (_isMovingSelection)
            FinishSelectionMove();
        if (_isAdjustingLayerOpacity)
        {
            _isAdjustingLayerOpacity = false;
            if (_layerOpacityChanged)
                _undoHistory.Push(_opacityDocumentSnapshot);
            _opacityDocumentSnapshot = null!;
            _layerOpacityChanged = false;
        }

        StoreActiveDocument();
        _activeDocumentIndex = index;
        var document = _documents[index];
        _canvasWidth = document.Width;
        _canvasHeight = document.Height;
        _layers = document.Layers;
        _undoHistory = document.UndoHistory;
        _activeLayerIndex = Math.Clamp(document.ActiveLayerIndex, 0, _layers.Count - 1);
        _layerListOffset = document.LayerListOffset;
        _selectionBounds = document.SelectionBounds;
        _showGrid = document.ShowGrid;
        _cropSelection = document.CropSelection;
        _cropSelectionInitialized = document.CropSelectionInitialized;
        _canvasPan = document.CanvasPan;
        _zoom = document.Zoom;
        _canvasSaveFileName = document.SaveFileName;
        _saveMessage = document.SaveMessage;
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
        _isEditing = true;
        _isDrawingStroke = false;
        _isMovingSelection = false;
        _isCreatingSelection = false;
        _isCropping = false;
        _isAdjustingLayerOpacity = false;
        _isDraggingLayer = false;
        _isLayerContextMenuOpen = false;
        _isTabContextMenuOpen = false;
        _colorPickerDrag = ColorPickerDrag.None;
        RebuildCanvasTextures();
    }

    private void StoreActiveDocument()
    {
        if (_activeDocumentIndex < 0 || _activeDocumentIndex >= _documents.Count)
            return;

        var document = _documents[_activeDocumentIndex];
        document.Width = _canvasWidth;
        document.Height = _canvasHeight;
        document.Layers = _layers;
        document.UndoHistory = _undoHistory;
        document.ActiveLayerIndex = _activeLayerIndex;
        document.LayerListOffset = _layerListOffset;
        document.SelectionBounds = _selectionBounds;
        document.ShowGrid = _showGrid;
        document.CropSelection = _cropSelection;
        document.CropSelectionInitialized = _cropSelectionInitialized;
        document.CanvasPan = _canvasPan;
        document.Zoom = _zoom;
        document.SaveFileName = _canvasSaveFileName;
        document.SaveMessage = _saveMessage;
    }

    private (int FirstIndex, Rectangle[] Tabs, Rectangle NewButton, Rectangle OpenButton) GetTabStripLayout()
    {
        var viewport = GraphicsDevice.Viewport;
        var right = Math.Max(12, viewport.Width - 140);
        var available = Math.Max(0, right - 12);
        var maximumVisible = Math.Max(1, (available + 4) / 76);
        var visibleCount = Math.Min(_documents.Count, maximumVisible);
        var firstIndex = Math.Clamp(_firstVisibleTabIndex, 0, Math.Max(0, _documents.Count - visibleCount));
        if (_activeDocumentIndex >= 0 && _activeDocumentIndex < firstIndex)
            firstIndex = _activeDocumentIndex;
        else if (_activeDocumentIndex >= firstIndex + visibleCount)
            firstIndex = _activeDocumentIndex - visibleCount + 1;
        _firstVisibleTabIndex = firstIndex;

        var tabWidth = visibleCount == 0
            ? 144
            : Math.Clamp((available - (visibleCount - 1) * 4) / visibleCount, 72, 144);
        var tabs = new Rectangle[visibleCount];
        for (var index = 0; index < visibleCount; index++)
            tabs[index] = new Rectangle(12 + index * (tabWidth + 4), 2, tabWidth, 28);

        return (
            firstIndex,
            tabs,
            new Rectangle(viewport.Width - 124, 2, 34, 28),
            new Rectangle(viewport.Width - 82, 2, 70, 28));
    }

    private Rectangle GetTabContextMenuBounds()
    {
        var width = Math.Min(TabContextMenuWidth, GraphicsDevice.Viewport.Width);
        return new Rectangle(_tabContextMenuPosition.X, _tabContextMenuPosition.Y, width, TabContextMenuHeight);
    }

    private bool HandleTabContextMenuRightClick(Point position)
    {
        var layout = GetTabStripLayout();
        for (var index = 0; index < layout.Tabs.Length; index++)
        {
            if (!layout.Tabs[index].Contains(position))
                continue;

            var viewport = GraphicsDevice.Viewport;
            var width = Math.Min(TabContextMenuWidth, viewport.Width);
            _contextMenuDocumentIndex = layout.FirstIndex + index;
            _tabContextMenuPosition = new Point(
                Math.Clamp(position.X, 0, Math.Max(0, viewport.Width - width)),
                Math.Clamp(position.Y, 0, Math.Max(0, viewport.Height - TabContextMenuHeight)));
            _isTabContextMenuOpen = true;
            return true;
        }

        _isTabContextMenuOpen = false;
        return false;
    }

    private bool HandleTabContextMenuClick(Point position)
    {
        if (!GetTabContextMenuBounds().Contains(position))
        {
            _isTabContextMenuOpen = false;
            return false;
        }

        var item = new Rectangle(
            _tabContextMenuPosition.X + 1,
            _tabContextMenuPosition.Y + 1,
            GetTabContextMenuBounds().Width - 2,
            TabContextMenuHeight - 2);
        if (item.Contains(position))
            CloseDocument(_contextMenuDocumentIndex);
        _isTabContextMenuOpen = false;
        return true;
    }

    private void DrawTabContextMenu(MouseState mouse)
    {
        var menu = GetTabContextMenuBounds();
        DrawRectangle(menu, PixologyPalette.Panel);
        DrawOutline(menu, PixologyPalette.Ink, 1);
        var item = new Rectangle(menu.X + 1, menu.Y + 1, menu.Width - 2, menu.Height - 2);
        if (item.Contains(mouse.Position))
            DrawRectangle(item, PixologyPalette.Accent);

        const string label = "Delete";
        var textSize = _font.MeasureString(label);
        DrawPixelText(
            label,
            new Vector2(item.X + 10, MathF.Round(item.Center.Y - textSize.Y / 2)),
            PixologyPalette.Ink);
    }

    private void CloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
            return;

        if (_isDrawingStroke)
            FinishStroke();
        if (_isMovingSelection)
            FinishSelectionMove();
        StoreActiveDocument();
        _documents.RemoveAt(index);
        _isTabContextMenuOpen = false;

        if (_documents.Count == 0)
        {
            _activeDocumentIndex = -1;
            CreateNewDocument();
            return;
        }

        if (index < _activeDocumentIndex)
        {
            _activeDocumentIndex--;
        }
        else if (index == _activeDocumentIndex)
        {
            _activeDocumentIndex = -1;
            ActivateDocument(Math.Min(index, _documents.Count - 1));
        }

        _firstVisibleTabIndex = Math.Clamp(_firstVisibleTabIndex, 0, Math.Max(0, _documents.Count - 1));
    }

    private static (Rectangle Open, Rectangle Cancel) GetOpenFileDialogButtons(Rectangle dialog)
    {
        const int openWidth = 92;
        const int cancelWidth = 120;
        const int gap = 12;
        const int rightPadding = 16;
        const int height = 32;
        var y = dialog.Bottom - 44;
        var cancel = new Rectangle(dialog.Right - rightPadding - cancelWidth, y, cancelWidth, height);
        var open = new Rectangle(cancel.X - gap - openWidth, y, openWidth, height);
        return (open, cancel);
    }

    private bool HandleTabStripClick(Point position)
    {
        var layout = GetTabStripLayout();
        if (layout.NewButton.Contains(position))
        {
            CreateNewDocument();
            return true;
        }

        if (layout.OpenButton.Contains(position))
        {
            ShowOpenFileDialog(createNewTab: true);
            return true;
        }

        for (var index = 0; index < layout.Tabs.Length; index++)
        {
            if (!layout.Tabs[index].Contains(position))
                continue;

            ActivateDocument(layout.FirstIndex + index);
            return true;
        }

        return false;
    }

    private void DrawTabs(MouseState mouse)
    {
        var layout = GetTabStripLayout();
        for (var index = 0; index < layout.Tabs.Length; index++)
        {
            var documentIndex = layout.FirstIndex + index;
            var bounds = layout.Tabs[index];
            var active = documentIndex == _activeDocumentIndex;
            var hovered = bounds.Contains(mouse.Position);
            DrawRectangle(bounds, active ? Color.White : hovered ? new Color(0xED, 0xF0, 0xFC) : PixologyPalette.Panel);
            DrawOutline(bounds, active || hovered ? PixologyPalette.Ink : PixologyPalette.Border, active ? 2 : 1);
            var title = FitText(_documents[documentIndex].Title, bounds.Width - 12);
            var textSize = _font.MeasureString(title);
            DrawPixelText(
                title,
                new Vector2(bounds.X + 6, MathF.Round(bounds.Center.Y - textSize.Y / 2)),
                PixologyPalette.Ink);
        }

        DrawButton(layout.NewButton, "+", mouse);
        DrawButton(layout.OpenButton, "Open", mouse);
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

    private void DrawEditor(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        DrawRectangle(new Rectangle(0, 0, viewport.Width, TabStripHeight), PixologyPalette.Accent);
        DrawRectangle(new Rectangle(0, TabStripHeight, viewport.Width, ToolbarHeight - TabStripHeight), PixologyPalette.Panel);
        DrawTabs(mouse);
        DrawRectangle(new Rectangle(0, TabStripHeight - 1, viewport.Width, 1), PixologyPalette.Border);
        DrawRectangle(new Rectangle(0, ToolbarHeight - 1, viewport.Width, 1), PixologyPalette.Border);
        DrawRectangle(new Rectangle(0, ToolbarHeight, SidebarWidth, viewport.Height - ToolbarHeight), PixologyPalette.Panel);
        DrawRectangle(new Rectangle(SidebarWidth - 1, ToolbarHeight, 1, viewport.Height - ToolbarHeight), PixologyPalette.Border);
        var rightSidebar = GetRightSidebarBounds(viewport);
        DrawRectangle(rightSidebar, PixologyPalette.Panel);
        DrawRectangle(new Rectangle(rightSidebar.X, rightSidebar.Y, 1, rightSidebar.Height), PixologyPalette.Border);

        var saveButton = new Rectangle(12, 40, 92, 40);
        DrawButton(saveButton, "Save", mouse);

        DrawToolButton(new Rectangle(8, 104, SidebarWidth - 16, 56), EditorTool.Select, mouse);
        DrawToolButton(new Rectangle(8, 168, SidebarWidth - 16, 56), EditorTool.Brush, mouse);
        DrawToolButton(new Rectangle(8, 232, SidebarWidth - 16, 56), EditorTool.Erase, mouse);
        DrawToolButton(new Rectangle(8, 296, SidebarWidth - 16, 56), EditorTool.PaintBucket, mouse);
        DrawToolButton(new Rectangle(8, 360, SidebarWidth - 16, 56), EditorTool.Crop, mouse);
        DrawToolButton(new Rectangle(8, 424, SidebarWidth - 16, 56), EditorTool.Eyedropper, mouse);

        var checkbox = new Rectangle(124, 49, 22, 22);
        DrawRectangle(checkbox, _showGrid ? PixologyPalette.CheckboxChecked : Color.White);
        DrawOutline(checkbox, PixologyPalette.Ink, 2);

        DrawVerticallyCenteredText("Show Grid", 158, checkbox.Center.Y);
        if (_activeTool == EditorTool.PaintBucket)
        {
            var contiguousCheckbox = new Rectangle(312, 49, 22, 22);
            DrawRectangle(contiguousCheckbox, _bucketContiguous ? PixologyPalette.CheckboxChecked : Color.White);
            DrawOutline(contiguousCheckbox, PixologyPalette.Ink, 2);

            DrawVerticallyCenteredText("Contiguous", 344, contiguousCheckbox.Center.Y);
        }
        else if (_activeTool == EditorTool.Erase)
        {
            var magicCheckbox = new Rectangle(312, 49, 22, 22);
            DrawRectangle(magicCheckbox, _magicErase ? PixologyPalette.CheckboxChecked : Color.White);
            DrawOutline(magicCheckbox, PixologyPalette.Ink, 2);

            DrawVerticallyCenteredText("Magic", 344, magicCheckbox.Center.Y);
        }
        DrawColorPicker(rightSidebar);
        DrawLayersPanel(rightSidebar, mouse);
        DrawEditorStatus();

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawToolIcons();
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        if (_isTabContextMenuOpen)
            DrawTabContextMenu(mouse);

    }

    private void DrawEditorStatus()
    {
        if (_saveMessage.Length == 0)
            return;

        var workspace = GetWorkspaceBounds(GraphicsDevice.Viewport);
        var message = FitText(_saveMessage, workspace.Width - 32);
        var messageBounds = new Rectangle(
            workspace.X + 8,
            workspace.Bottom - 30,
            (int)_font.MeasureString(message).X + 16,
            24);
        DrawRectangle(messageBounds, Color.White);
        DrawOutline(messageBounds, PixologyPalette.Border, 1);
        DrawPixelText(message, new Vector2(messageBounds.X + 8, messageBounds.Y + 4), PixologyPalette.Ink);
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
        else if (_activeTool == EditorTool.Select)
            DrawLayerSelection(canvasBounds);
        else
            DrawBrushCursor(canvasBounds, mousePosition);
    }

    private void DrawBrushCursor(Rectangle canvasBounds, Point mousePosition)
    {
        if (!TryGetCanvasPixel(mousePosition, canvasBounds, out var pixelX, out var pixelY))
            return;

        var pixelScale = canvasBounds.Width / _canvasWidth;
        var toolSize = _activeTool == EditorTool.Erase && !_magicErase ? 2 : 1;
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
            DrawOutline(innerBounds, PixologyPalette.Ink, 2);
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
        DrawPixelText(sizeLabel, labelPosition, PixologyPalette.Ink);
    }

    private void DrawLayerSelection(Rectangle canvasBounds)
    {
        if (_selectionBounds.IsEmpty)
            return;

        var pixelScale = Math.Max(1, canvasBounds.Width / _canvasWidth);
        var selectionBounds = new Rectangle(
            canvasBounds.X + _selectionBounds.X * pixelScale,
            canvasBounds.Y + _selectionBounds.Y * pixelScale,
            _selectionBounds.Width * pixelScale,
            _selectionBounds.Height * pixelScale);
        DrawOutline(selectionBounds, Color.White, 4);
        if (selectionBounds.Width > 4 && selectionBounds.Height > 4)
        {
            DrawOutline(
                new Rectangle(
                    selectionBounds.X + 2,
                    selectionBounds.Y + 2,
                    selectionBounds.Width - 4,
                    selectionBounds.Height - 4),
                PixologyPalette.Ink,
                1);
        }
    }

    private void BeginStroke()
    {
        _isDrawingStroke = true;
        _strokeChanged = false;
        _strokeDocumentSnapshot = CaptureActiveLayerSnapshot();
    }

    private void ActivateSelectionTool()
    {
        _activeTool = EditorTool.Select;
        UpdateSelectionBounds();
    }

    private void UpdateSelectionBounds()
    {
        var left = _canvasWidth;
        var top = _canvasHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < _canvasHeight; y++)
        {
            for (var x = 0; x < _canvasWidth; x++)
            {
                if (_canvasPixels[y * _canvasWidth + x].A == 0)
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        _selectionBounds = right < left || bottom < top
            ? Rectangle.Empty
            : new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private void ClearSelection()
    {
        if (_isMovingSelection)
            FinishSelectionMove();
        _isCreatingSelection = false;
        _selectionBounds = Rectangle.Empty;
    }

    private void DeleteSelectionContents()
    {
        if (_selectionBounds.IsEmpty)
            return;

        if (_isMovingSelection)
            FinishSelectionMove();
        _isCreatingSelection = false;

        var bounds = Rectangle.Intersect(
            new Rectangle(0, 0, _canvasWidth, _canvasHeight),
            _selectionBounds);
        if (bounds.IsEmpty)
            return;

        var snapshot = CaptureActiveLayerSnapshot();
        var changed = false;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var rowStart = y * _canvasWidth + bounds.Left;
            for (var x = 0; x < bounds.Width; x++)
            {
                var index = rowStart + x;
                if (_canvasPixels[index] == Color.Transparent)
                    continue;

                _canvasPixels[index] = Color.Transparent;
                changed = true;
            }
        }

        if (!changed)
            return;

        _undoHistory.Push(snapshot);
        RefreshCompositeTexture();
    }

    private void CopySelection()
    {
        if (_selectionBounds.IsEmpty)
            return;

        var bounds = Rectangle.Intersect(
            new Rectangle(0, 0, _canvasWidth, _canvasHeight),
            _selectionBounds);
        if (bounds.IsEmpty)
            return;

        _copiedSelectionPixels = new Color[bounds.Width * bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
        {
            Array.Copy(
                _compositePixels,
                (bounds.Y + y) * _canvasWidth + bounds.X,
                _copiedSelectionPixels,
                y * bounds.Width,
                bounds.Width);
        }

        _copiedSelectionBounds = bounds;
    }

    private void PasteSelection()
    {
        if (_copiedSelectionPixels is null || _copiedSelectionBounds.IsEmpty)
            return;

        if (_isMovingSelection)
            FinishSelectionMove();
        _isCreatingSelection = false;

        _undoHistory.Push(CaptureDocumentSnapshot());
        var pastedPixels = new Color[_canvasWidth * _canvasHeight];
        var destinationBounds = Rectangle.Intersect(
            new Rectangle(0, 0, _canvasWidth, _canvasHeight),
            _copiedSelectionBounds);
        for (var y = destinationBounds.Top; y < destinationBounds.Bottom; y++)
        {
            var sourceOffset = (y - _copiedSelectionBounds.Y) * _copiedSelectionBounds.Width;
            var destinationOffset = y * _canvasWidth + destinationBounds.Left;
            Array.Copy(
                _copiedSelectionPixels,
                sourceOffset,
                pastedPixels,
                destinationOffset,
                destinationBounds.Width);
        }

        _layers.Insert(_activeLayerIndex + 1, new Layer
        {
            Name = $"Layer {_layers.Count + 1}",
            Pixels = pastedPixels
        });
        _activeLayerIndex++;
        _canvasPixels = pastedPixels;
        _selectionBounds = destinationBounds;
        _activeTool = EditorTool.Select;
        _layerListOffset = 0;
        RefreshCompositeTexture();
    }

    private void BeginSelectionInteraction(Point position, bool forceNewSelection)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        if (!TryGetCanvasPixel(position, canvasBounds, out var pixelX, out var pixelY))
            return;

        if (forceNewSelection || _selectionBounds.IsEmpty || !_selectionBounds.Contains(pixelX, pixelY))
        {
            _isCreatingSelection = true;
            _selectionBoxStart = new Point(pixelX, pixelY);
            _selectionBounds = new Rectangle(pixelX, pixelY, 1, 1);
            return;
        }

        _isMovingSelection = true;
        _selectionMoveChanged = false;
        _selectionDragStart = position;
        _selectionMoveBounds = _selectionBounds;
        _selectionMoveOffset = Point.Zero;
        _selectionMoveSnapshot = CaptureActiveLayerSnapshot();
    }

    private void UpdateSelectionBox(Point position)
    {
        var startPosition = GetCanvasBounds(GraphicsDevice.Viewport);
        var startPoint = _selectionBoxStart;
        var currentPoint = GetCanvasPixelClamped(position, startPosition);
        _selectionBounds = new Rectangle(
            Math.Min(startPoint.X, currentPoint.X),
            Math.Min(startPoint.Y, currentPoint.Y),
            Math.Abs(startPoint.X - currentPoint.X) + 1,
            Math.Abs(startPoint.Y - currentPoint.Y) + 1);
    }

    private Point GetCanvasPixelClamped(Point position, Rectangle canvasBounds)
    {
        var workspace = GetWorkspaceBounds(GraphicsDevice.Viewport);
        position.X = Math.Clamp(position.X, Math.Max(workspace.Left, canvasBounds.Left), Math.Min(workspace.Right - 1, canvasBounds.Right - 1));
        position.Y = Math.Clamp(position.Y, Math.Max(workspace.Top, canvasBounds.Top), Math.Min(workspace.Bottom - 1, canvasBounds.Bottom - 1));
        return new Point(
            (position.X - canvasBounds.X) * _canvasWidth / canvasBounds.Width,
            (position.Y - canvasBounds.Y) * _canvasHeight / canvasBounds.Height);
    }

    private void UpdateSelectionMove(Point position)
    {
        var canvasBounds = GetCanvasBounds(GraphicsDevice.Viewport);
        var pixelScale = Math.Max(1, canvasBounds.Width / _canvasWidth);
        var offset = new Point(
            (int)MathF.Round((position.X - _selectionDragStart.X) / (float)pixelScale),
            (int)MathF.Round((position.Y - _selectionDragStart.Y) / (float)pixelScale));
        if (offset == _selectionMoveOffset)
            return;

        _selectionMoveOffset = offset;
        _selectionMoveChanged = offset.X != 0 || offset.Y != 0;
        var sourcePixels = _selectionMoveSnapshot.ChangedLayerPixels;
        Array.Copy(sourcePixels, _canvasPixels, sourcePixels.Length);
        for (var y = Math.Max(0, _selectionMoveBounds.Top); y < Math.Min(_canvasHeight, _selectionMoveBounds.Bottom); y++)
        {
            Array.Clear(_canvasPixels, y * _canvasWidth + Math.Max(0, _selectionMoveBounds.Left),
                Math.Max(0, Math.Min(_canvasWidth, _selectionMoveBounds.Right) - Math.Max(0, _selectionMoveBounds.Left)));
        }

        for (var y = _selectionMoveBounds.Top; y < _selectionMoveBounds.Bottom; y++)
        {
            var destinationY = y + offset.Y;
            if (destinationY < 0 || destinationY >= _canvasHeight)
                continue;

            for (var x = _selectionMoveBounds.Left; x < _selectionMoveBounds.Right; x++)
            {
                var destinationX = x + offset.X;
                if (destinationX < 0 || destinationX >= _canvasWidth)
                    continue;

                _canvasPixels[destinationY * _canvasWidth + destinationX] =
                    sourcePixels[y * _canvasWidth + x];
            }
        }

        var movedBounds = new Rectangle(
            _selectionMoveBounds.X + offset.X,
            _selectionMoveBounds.Y + offset.Y,
            _selectionMoveBounds.Width,
            _selectionMoveBounds.Height);
        _selectionBounds = Rectangle.Intersect(
            new Rectangle(0, 0, _canvasWidth, _canvasHeight),
            movedBounds);
        RefreshCompositeTexture();
    }

    private void FinishSelectionMove()
    {
        _isMovingSelection = false;
        if (_selectionMoveChanged)
            _undoHistory.Push(_selectionMoveSnapshot);

        _selectionMoveSnapshot = null!;
        _selectionMoveChanged = false;
    }

    private void FinishStroke()
    {
        _isDrawingStroke = false;
        if (_strokeChanged)
            _undoHistory.Push(_strokeDocumentSnapshot);

        _strokeDocumentSnapshot = null!;
        _strokeChanged = false;
    }

    private void Undo()
    {
        if (_undoHistory.Count == 0)
            return;

        RestoreDocumentSnapshot(_undoHistory.Pop());
    }

    private CanvasSnapshot CaptureDocumentSnapshot()
    {
        return new CanvasSnapshot(
            _canvasWidth,
            _canvasHeight,
            _layers.Select(layer => new LayerSnapshot(
                layer.Name,
                layer.Opacity,
                (Color[])layer.Pixels.Clone())).ToArray(),
            _activeLayerIndex,
            -1,
            Array.Empty<Color>(),
            _selectionBounds);
    }

    private CanvasSnapshot CaptureActiveLayerSnapshot()
    {
        return new CanvasSnapshot(
            _canvasWidth,
            _canvasHeight,
            Array.Empty<LayerSnapshot>(),
            _activeLayerIndex,
            _activeLayerIndex,
            (Color[])_canvasPixels.Clone(),
            _selectionBounds);
    }

    private void RestoreDocumentSnapshot(CanvasSnapshot snapshot)
    {
        if (snapshot.ChangedLayerIndex >= 0)
        {
            _layers[snapshot.ChangedLayerIndex].Pixels = snapshot.ChangedLayerPixels;
            _activeLayerIndex = snapshot.ChangedLayerIndex;
            _canvasPixels = _layers[_activeLayerIndex].Pixels;
            if (_activeTool == EditorTool.Select)
                _selectionBounds = snapshot.SelectionBounds;
            RefreshCompositeTexture();
            return;
        }

        _canvasWidth = snapshot.Width;
        _canvasHeight = snapshot.Height;
        _layers.Clear();
        _layers.AddRange(snapshot.Layers.Select(layer => new Layer
        {
            Name = layer.Name,
            Opacity = layer.Opacity,
            Pixels = layer.Pixels
        }));
        _activeLayerIndex = Math.Clamp(snapshot.ActiveLayerIndex, 0, _layers.Count - 1);
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
        if (_activeTool == EditorTool.Select)
            _selectionBounds = snapshot.SelectionBounds;
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

        var snapshot = CaptureDocumentSnapshot();
        var resizedLayers = new List<Layer>(_layers.Count);
        foreach (var layer in _layers)
        {
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
                        resizedPixels[y * _cropSelection.Width + x] = layer.Pixels[sourceY * _canvasWidth + sourceX];
                }
            }

            resizedLayers.Add(new Layer { Name = layer.Name, Opacity = layer.Opacity, Pixels = resizedPixels });
        }

        _undoHistory.Push(snapshot);
        _canvasWidth = _cropSelection.Width;
        _canvasHeight = _cropSelection.Height;
        _layers.Clear();
        _layers.AddRange(resizedLayers);
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
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

        _selectedColor = _compositePixels[pixelY * _canvasWidth + pixelX];
        UpdateHsvFromSelectedColor();
    }

    private void RebuildCanvasTextures()
    {
        _canvas?.Dispose();
        _checkerboard?.Dispose();
        _canvas = new Texture2D(GraphicsDevice, _canvasWidth, _canvasHeight);
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
        RefreshCompositeTexture();
    }

    private void RefreshCompositeTexture()
    {
        _compositePixels = new Color[_canvasWidth * _canvasHeight];
        foreach (var layer in _layers)
        {
            for (var index = 0; index < _compositePixels.Length; index++)
                _compositePixels[index] = CompositeOver(_compositePixels[index], layer.Pixels[index], layer.Opacity);
        }

        _canvas.SetData(_compositePixels);
    }

    private static Color CompositeOver(Color destination, Color source, float opacity)
    {
        var sourceAlpha = source.A / 255f * opacity;
        var destinationAlpha = destination.A / 255f;
        var outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
        if (outputAlpha <= 0f)
            return Color.Transparent;

        var red = (source.R * sourceAlpha + destination.R * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
        var green = (source.G * sourceAlpha + destination.G * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
        var blue = (source.B * sourceAlpha + destination.B * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
        return new Color(
            (byte)Math.Clamp(MathF.Round(red), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(green), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(blue), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(outputAlpha * 255f), 0f, 255f));
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
            RefreshCompositeTexture();
            _strokeChanged = true;
        }
    }

    private bool ApplyTool(int x, int y)
    {
        if (_activeTool == EditorTool.PaintBucket)
            return FillArea(x, y);
        if (_activeTool == EditorTool.Erase && _magicErase)
            return EraseMatchingPixels(x, y);

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

    private bool EraseMatchingPixels(int x, int y)
    {
        var targetColor = _canvasPixels[y * _canvasWidth + x];
        if (targetColor.A == 0)
            return false;

        var changed = false;
        for (var index = 0; index < _canvasPixels.Length; index++)
        {
            if (_canvasPixels[index] != targetColor)
                continue;

            _canvasPixels[index] = Color.Transparent;
            changed = true;
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
            _hexTextSelectionActive = true;
            _hexEditText = GetSelectedHexColor()[1..];
            return;
        }

        _hexSelected = false;
        _hexTextSelectionActive = false;
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

        if (HandleLayerPanelClick(position))
            return;

        if (new Rectangle(8, 104, SidebarWidth - 16, 56).Contains(position))
        {
            ActivateSelectionTool();
            return;
        }

        if (new Rectangle(8, 168, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Brush;
            return;
        }

        if (new Rectangle(8, 232, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Erase;
            return;
        }

        if (new Rectangle(8, 296, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.PaintBucket;
            return;
        }

        if (new Rectangle(8, 360, SidebarWidth - 16, 56).Contains(position))
        {
            ActivateCropTool();
            return;
        }

        if (new Rectangle(8, 424, SidebarWidth - 16, 56).Contains(position))
        {
            _activeTool = EditorTool.Eyedropper;
            return;
        }

        if (new Rectangle(12, 40, 92, 40).Contains(position))
        {
            SaveCanvas();
            return;
        }

        if (new Rectangle(124, 49, 22, 22).Contains(position) ||
            new Rectangle(154, 46, 130, 28).Contains(position))
        {
            _showGrid = !_showGrid;
            return;
        }

        if (_activeTool == EditorTool.PaintBucket &&
            (new Rectangle(312, 49, 22, 22).Contains(position) ||
             new Rectangle(344, 46, 140, 28).Contains(position)))
        {
            _bucketContiguous = !_bucketContiguous;
        }
        else if (_activeTool == EditorTool.Erase &&
                 (new Rectangle(312, 49, 22, 22).Contains(position) ||
                  new Rectangle(344, 46, 80, 28).Contains(position)))
        {
            _magicErase = !_magicErase;
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
            if (!TryApplyHexColor(hex))
            {
                _saveMessage = "Clipboard text is not a #RRGGBB color";
                return;
            }

            EditHexText(hex);
        }
    }

    private void HandleTextInput(object sender, TextInputEventArgs eventArgs)
    {
        if (!_hexSelected ||
            !char.IsAsciiHexDigit(eventArgs.Character) ||
            (_hexEditText.Length >= 6 && !_hexTextSelectionActive))
            return;

        EditHexText(char.ToUpperInvariant(eventArgs.Character).ToString());
    }

    private void EditHexText(string text, bool backspace = false)
    {
        if (_hexTextSelectionActive)
        {
            _hexEditText = string.Empty;
            _hexTextSelectionActive = false;
        }

        if (backspace)
        {
            if (_hexEditText.Length > 0)
                _hexEditText = _hexEditText[..^1];
            return;
        }

        if (_hexEditText.Length + text.Length > 6)
            return;

        _hexEditText += text;
        if (_hexEditText.Length == 6)
            TryApplyHexColor(_hexEditText);
    }

    private bool TryApplyHexColor(string hex)
    {
        if (hex.Length != 6 ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        _selectedColor = new Color(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);
        UpdateHsvFromSelectedColor();
        return true;
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
        var fileName = string.IsNullOrWhiteSpace(_canvasSaveFileName) ? "image" : _canvasSaveFileName;
        var filePath = Path.Combine(desktopPath, $"{fileName}.png");
        using var stream = File.Create(filePath);
        RefreshCompositeTexture();
        _canvas.SaveAsPng(stream, _canvasWidth, _canvasHeight);
        _saveMessage = $"Saved {fileName}.png to Desktop";
        if (_activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count)
        {
            _documents[_activeDocumentIndex].Title = fileName;
            _documents[_activeDocumentIndex].FilePath = filePath;
            StoreActiveDocument();
        }
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

    private static (Rectangle SaturationValue, Rectangle Hue, Rectangle HexBounds, Rectangle ColorPreview, Vector2 HexPosition) GetColorPickerBounds(Viewport viewport)
    {
        var sidebar = GetRightSidebarBounds(viewport);
        var square = new Rectangle(sidebar.X + 16, sidebar.Y + 36, 144, 144);
        var hue = new Rectangle(square.Right + 8, square.Y, 20, square.Height);
        var hexBounds = new Rectangle(sidebar.X + 12, square.Bottom + 12, 132, 36);
        var colorPreview = new Rectangle(hexBounds.Right + 8, hexBounds.Y, sidebar.Right - hexBounds.Right - 20, hexBounds.Height);
        var hexPosition = new Vector2(hexBounds.X + 6, hexBounds.Y + 6);
        return (square, hue, hexBounds, colorPreview, hexPosition);
    }

    private void DrawColorPicker(Rectangle sidebar)
    {
        var bounds = GetColorPickerBounds(GraphicsDevice.Viewport);
        DrawPixelText("Color", new Vector2(sidebar.X + 16, sidebar.Y + 3), PixologyPalette.Ink);
        _spriteBatch.Draw(_saturationValueTexture, bounds.SaturationValue, Color.White);
        _spriteBatch.Draw(_hueTexture, bounds.Hue, Color.White);

        var hueMarkerY = bounds.Hue.Y + (int)MathF.Round(_selectedHue * (bounds.Hue.Height - 1));
        DrawOutline(new Rectangle(bounds.Hue.X - 2, hueMarkerY - 2, bounds.Hue.Width + 4, 5), Color.White, 1);
        DrawOutline(bounds.SaturationValue, PixologyPalette.Ink, 1);
        DrawOutline(bounds.Hue, PixologyPalette.Ink, 1);

        var selectedX = bounds.SaturationValue.X +
                        (int)MathF.Round(_selectedSaturation * (bounds.SaturationValue.Width - 1));
        var selectedY = bounds.SaturationValue.Y +
                        (int)MathF.Round((1f - _selectedValue) * (bounds.SaturationValue.Height - 1));
        DrawOutline(new Rectangle(selectedX - 5, selectedY - 5, 11, 11), Color.White, 2);
        DrawOutline(new Rectangle(selectedX - 2, selectedY - 2, 5, 5), PixologyPalette.Ink, 1);

        var hexColor = _hexSelected
            ? $"#{_hexEditText}{(_hexEditText.Length < 6 ? "_" : string.Empty)}"
            : GetSelectedHexColor();
        DrawRectangle(bounds.HexBounds, _hexSelected ? PixologyPalette.Accent : Color.White);
        DrawOutline(bounds.HexBounds, PixologyPalette.Ink, 1);
        DrawPixelText(hexColor, bounds.HexPosition, PixologyPalette.Ink);
        DrawRectangle(bounds.ColorPreview, _selectedColor);
        DrawOutline(bounds.ColorPreview, PixologyPalette.Ink, 1);
    }

    private void DrawLayersPanel(Rectangle sidebar, MouseState mouse)
    {
        var bounds = GetLayersPanelBounds(sidebar);
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixologyPalette.Border);
        DrawPixelText("Layers", new Vector2(bounds.X + 12, bounds.Y + 8), PixologyPalette.Ink);

        var opacityBounds = GetLayerOpacityBounds(bounds);
        DrawPixelText("Opacity", new Vector2(bounds.X + 12, opacityBounds.Y - 30), PixologyPalette.Ink);
        DrawRectangle(opacityBounds, Color.White);
        DrawOutline(opacityBounds, PixologyPalette.Ink, 1);
        var markerX = opacityBounds.X + (int)MathF.Round(_layers[_activeLayerIndex].Opacity * (opacityBounds.Width - 1));
        var fillWidth = Math.Max(0, markerX - opacityBounds.X - 1);
        DrawRectangle(new Rectangle(opacityBounds.X + 1, opacityBounds.Y + 1, fillWidth, opacityBounds.Height - 2), PixologyPalette.Accent);
        DrawOutline(opacityBounds, PixologyPalette.Ink, 1);
        DrawRectangle(new Rectangle(markerX - 2, opacityBounds.Y - 2, 4, opacityBounds.Height + 4), PixologyPalette.Ink);
        DrawPixelText($"{(int)MathF.Round(_layers[_activeLayerIndex].Opacity * 100)}%", new Vector2(opacityBounds.Right + 6, opacityBounds.Y - 2), PixologyPalette.Ink);

        var listBounds = GetLayerListBounds(bounds);
        DrawRectangle(listBounds, Color.White);
        DrawOutline(listBounds, PixologyPalette.Ink, 1);
        const int rowHeight = 36;
        var visibleRows = Math.Max(0, listBounds.Height / rowHeight);
        for (var row = 0; row < visibleRows; row++)
        {
            var layerIndex = _layers.Count - 1 - _layerListOffset - row;
            if (layerIndex < 0)
                break;

            var rowBounds = new Rectangle(listBounds.X + 1, listBounds.Y + 1 + row * rowHeight, listBounds.Width - 2, rowHeight);
            if (layerIndex == _activeLayerIndex)
                DrawRectangle(rowBounds, PixologyPalette.Accent);
            else if (_isDraggingLayer && layerIndex == _layerDropTargetIndex)
                DrawRectangle(rowBounds, new Color(0xED, 0xF0, 0xFC));

            var layerName = FitText(_layers[layerIndex].Name, rowBounds.Width - 12);
            var layerNameSize = _font.MeasureString(layerName);
            DrawPixelText(
                layerName,
                new Vector2(rowBounds.X + 6, MathF.Round(rowBounds.Center.Y - layerNameSize.Y / 2)),
                PixologyPalette.Ink);
        }

        if (_isDraggingLayer && _layerDropTargetIndex >= 0)
        {
            var targetRow = _layerDropTargetIndex;
            var targetY = listBounds.Y + 1 + (_layers.Count - 1 - _layerListOffset - targetRow) * rowHeight;
            DrawRectangle(
                new Rectangle(listBounds.X + 1, targetY, listBounds.Width - 2, 2),
                new Color(30, 90, 180));
        }

        DrawButton(GetAddLayerButtonBounds(bounds), "Add", mouse);
        if (_isLayerContextMenuOpen)
            DrawLayerContextMenu(mouse);
    }

    private static Rectangle GetLayersPanelBounds(Rectangle sidebar)
    {
        return new Rectangle(sidebar.X, sidebar.Y + 240, sidebar.Width, Math.Max(0, sidebar.Height - 240));
    }

    private static Rectangle GetAddLayerButtonBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + 12, panel.Bottom - 42, panel.Width - 24, 32);
    }

    private static Rectangle GetLayerOpacityBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + 12, panel.Y + 66, Math.Max(40, panel.Width - 76), 18);
    }

    private static Rectangle GetLayerListBounds(Rectangle panel)
    {
        var top = panel.Y + 112;
        return new Rectangle(
            panel.X + 12,
            top,
            panel.Width - 24,
            Math.Max(0, GetAddLayerButtonBounds(panel).Y - top - 8));
    }

    private Rectangle GetLayerContextMenuBounds(Point position, int itemCount)
    {
        var width = Math.Min(LayerContextMenuWidth, GraphicsDevice.Viewport.Width);
        return new Rectangle(position.X, position.Y, width, itemCount * LayerContextMenuItemHeight + 2);
    }

    private bool HandleLayerPanelClick(Point position)
    {
        var panel = GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport));
        if (_isLayerContextMenuOpen)
        {
            var menuItems = GetLayerContextMenuItems(_contextMenuLayerIndex);
            var menu = GetLayerContextMenuBounds(_layerContextMenuPosition, menuItems.Length);
            if (menu.Contains(position))
            {
                var itemIndex = (position.Y - menu.Y - 1) / LayerContextMenuItemHeight;
                _isLayerContextMenuOpen = false;
                if (itemIndex >= 0 && itemIndex < menuItems.Length && menuItems[itemIndex].Enabled)
                {
                    switch (menuItems[itemIndex].Action)
                    {
                        case LayerContextMenuAction.MergeUp:
                            MergeLayer(_contextMenuLayerIndex, 1);
                            break;
                        case LayerContextMenuAction.MergeDown:
                            MergeLayer(_contextMenuLayerIndex, -1);
                            break;
                        case LayerContextMenuAction.Delete:
                            DeleteLayer(_contextMenuLayerIndex);
                            break;
                    }
                }

                return true;
            }

            _isLayerContextMenuOpen = false;
        }

        var addButton = GetAddLayerButtonBounds(panel);
        if (addButton.Contains(position))
        {
            AddLayer();
            return true;
        }

        var opacityBounds = GetLayerOpacityBounds(panel);
        if (opacityBounds.Contains(position))
        {
            _opacityDocumentSnapshot = CaptureDocumentSnapshot();
            _layerOpacityChanged = false;
            _isAdjustingLayerOpacity = true;
            UpdateLayerOpacity(position);
            return true;
        }

        var listBounds = GetLayerListBounds(panel);
        if (listBounds.Contains(position))
        {
            var row = (position.Y - listBounds.Y - 1) / 36;
            var layerIndex = _layers.Count - 1 - _layerListOffset - row;
            if (row >= 0 && layerIndex >= 0 && layerIndex < _layers.Count)
            {
                SelectLayer(layerIndex);
                _isDraggingLayer = true;
                _draggedLayerIndex = layerIndex;
                _layerDropTargetIndex = layerIndex;
                _layerDragStartPosition = position;
                _layerDragMoved = false;
            }

            return true;
        }

        return panel.Contains(position);
    }

    private void HandleLayerPanelRightClick(Point position)
    {
        var panel = GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport));
        var listBounds = GetLayerListBounds(panel);
        if (!listBounds.Contains(position))
        {
            _isLayerContextMenuOpen = false;
            return;
        }

        var row = (position.Y - listBounds.Y - 1) / 36;
        var layerIndex = _layers.Count - 1 - _layerListOffset - row;
        if (row < 0 || layerIndex < 0 || layerIndex >= _layers.Count)
        {
            _isLayerContextMenuOpen = false;
            return;
        }

        SelectLayer(layerIndex);
        var viewport = GraphicsDevice.Viewport;
        var menuHeight = GetLayerContextMenuItems(layerIndex).Length * LayerContextMenuItemHeight + 2;
        var menuWidth = Math.Min(LayerContextMenuWidth, viewport.Width);
        _layerContextMenuPosition = new Point(
            Math.Clamp(position.X, 0, Math.Max(0, viewport.Width - menuWidth)),
            Math.Clamp(position.Y, 0, Math.Max(0, viewport.Height - menuHeight)));
        _contextMenuLayerIndex = layerIndex;
        _isLayerContextMenuOpen = true;
        _isDraggingLayer = false;
    }

    private void DrawLayerContextMenu(MouseState mouse)
    {
        var items = GetLayerContextMenuItems(_contextMenuLayerIndex);
        var menu = GetLayerContextMenuBounds(_layerContextMenuPosition, items.Length);
        DrawRectangle(menu, PixologyPalette.Panel);
        DrawOutline(menu, PixologyPalette.Ink, 1);
        for (var index = 0; index < items.Length; index++)
            DrawLayerContextMenuItem(menu, index, items[index], mouse);
    }

    private LayerContextMenuItem[] GetLayerContextMenuItems(int layerIndex)
    {
        var items = new List<LayerContextMenuItem>(3);
        if (layerIndex >= 0 && layerIndex < _layers.Count - 1)
            items.Add(new LayerContextMenuItem("Merge Up", LayerContextMenuAction.MergeUp, true));
        if (layerIndex > 0 && layerIndex < _layers.Count)
            items.Add(new LayerContextMenuItem("Merge Down", LayerContextMenuAction.MergeDown, true));
        items.Add(new LayerContextMenuItem("Delete", LayerContextMenuAction.Delete, _layers.Count > 1));
        return items.ToArray();
    }

    private void DrawLayerContextMenuItem(
        Rectangle menu,
        int item,
        LayerContextMenuItem menuItem,
        MouseState mouse)
    {
        var bounds = new Rectangle(
            menu.X + 1,
            menu.Y + 1 + item * LayerContextMenuItemHeight,
            menu.Width - 2,
            LayerContextMenuItemHeight);
        if (menuItem.Enabled && bounds.Contains(mouse.Position))
            DrawRectangle(bounds, PixologyPalette.Accent);

        if (item > 0)
            DrawRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixologyPalette.Border);

        var textSize = _font.MeasureString(menuItem.Label);
        DrawPixelText(
            menuItem.Label,
            new Vector2(bounds.X + 10, MathF.Round(bounds.Center.Y - textSize.Y / 2)),
            menuItem.Enabled ? PixologyPalette.Ink : new Color(145, 145, 145));
    }

    private void SelectLayer(int layerIndex)
    {
        _activeLayerIndex = layerIndex;
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
        if (_activeTool == EditorTool.Select)
            UpdateSelectionBounds();
    }

    private void UpdateLayerDrag(Point position)
    {
        var dragX = position.X - _layerDragStartPosition.X;
        var dragY = position.Y - _layerDragStartPosition.Y;
        if (dragX * dragX + dragY * dragY >= 16)
            _layerDragMoved = true;

        var listBounds = GetLayerListBounds(GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport)));
        if (!listBounds.Contains(position))
            return;

        var row = (position.Y - listBounds.Y - 1) / 36;
        var layerIndex = _layers.Count - 1 - _layerListOffset - row;
        if (row >= 0 && layerIndex >= 0 && layerIndex < _layers.Count)
            _layerDropTargetIndex = layerIndex;
    }

    private void FinishLayerDrag()
    {
        if (_layerDragMoved && _layerDropTargetIndex >= 0)
            ReorderLayer(_draggedLayerIndex, _layerDropTargetIndex);

        _isDraggingLayer = false;
        _draggedLayerIndex = -1;
        _layerDropTargetIndex = -1;
        _layerDragMoved = false;
    }

    private void ReorderLayer(int sourceIndex, int destinationIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _layers.Count ||
            destinationIndex < 0 || destinationIndex >= _layers.Count ||
            sourceIndex == destinationIndex)
        {
            return;
        }

        _undoHistory.Push(CaptureDocumentSnapshot());
        var activeLayer = _layers[_activeLayerIndex];
        var layer = _layers[sourceIndex];
        _layers.RemoveAt(sourceIndex);
        _layers.Insert(destinationIndex, layer);
        _activeLayerIndex = _layers.IndexOf(activeLayer);
        SetLayerListOffset(_layerListOffset);
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
        RefreshCompositeTexture();
    }

    private void SetLayerListOffset(int offset)
    {
        var panel = GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport));
        var visibleRows = Math.Max(1, GetLayerListBounds(panel).Height / 36);
        _layerListOffset = Math.Clamp(offset, 0, Math.Max(0, _layers.Count - visibleRows));
    }

    private void UpdateLayerOpacity(Point position)
    {
        var bounds = GetLayerOpacityBounds(GetLayersPanelBounds(GetRightSidebarBounds(GraphicsDevice.Viewport)));
        var opacity = Math.Clamp((position.X - bounds.X) / (float)(bounds.Width - 1), 0f, 1f);
        if (MathF.Abs(_layers[_activeLayerIndex].Opacity - opacity) < 0.001f)
            return;

        _layers[_activeLayerIndex].Opacity = opacity;
        _layerOpacityChanged = true;
        RefreshCompositeTexture();
    }

    private void AddLayer()
    {
        _undoHistory.Push(CaptureDocumentSnapshot());
        var layer = new Layer
        {
            Name = $"Layer {_layers.Count + 1}",
            Pixels = new Color[_canvasWidth * _canvasHeight]
        };
        _layers.Insert(_activeLayerIndex + 1, layer);
        _activeLayerIndex++;
        _layerListOffset = 0;
        _canvasPixels = layer.Pixels;
        if (_activeTool == EditorTool.Select)
            UpdateSelectionBounds();
        RefreshCompositeTexture();
    }

    private void MergeLayer(int layerIndex, int direction)
    {
        var neighborIndex = layerIndex + direction;
        if (layerIndex < 0 || layerIndex >= _layers.Count ||
            neighborIndex < 0 || neighborIndex >= _layers.Count)
            return;

        _undoHistory.Push(CaptureDocumentSnapshot());
        var lowerIndex = Math.Min(layerIndex, neighborIndex);
        var upperIndex = lowerIndex + 1;
        var lower = _layers[lowerIndex];
        var upper = _layers[upperIndex];
        var mergedPixels = new Color[_canvasWidth * _canvasHeight];
        for (var index = 0; index < mergedPixels.Length; index++)
        {
            var lowerColor = CompositeOver(Color.Transparent, lower.Pixels[index], lower.Opacity);
            mergedPixels[index] = CompositeOver(lowerColor, upper.Pixels[index], upper.Opacity);
        }

        var merged = new Layer
        {
            Name = $"{lower.Name} + {upper.Name}",
            Pixels = mergedPixels
        };
        _layers.RemoveAt(upperIndex);
        _layers[lowerIndex] = merged;
        _activeLayerIndex = lowerIndex;
        SetLayerListOffset(_layerListOffset);
        _canvasPixels = merged.Pixels;
        if (_activeTool == EditorTool.Select)
            UpdateSelectionBounds();
        RefreshCompositeTexture();
    }

    private void DeleteLayer(int layerIndex)
    {
        if (_layers.Count <= 1 || layerIndex < 0 || layerIndex >= _layers.Count)
            return;

        _undoHistory.Push(CaptureDocumentSnapshot());
        _layers.RemoveAt(layerIndex);
        if (_activeLayerIndex > layerIndex)
            _activeLayerIndex--;
        else if (_activeLayerIndex == layerIndex)
            _activeLayerIndex = Math.Min(layerIndex, _layers.Count - 1);

        SetLayerListOffset(_layerListOffset);
        _canvasPixels = _layers[_activeLayerIndex].Pixels;
        if (_activeTool == EditorTool.Select)
            UpdateSelectionBounds();
        RefreshCompositeTexture();
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
        var pixelScale = Math.Max(1, (int)MathF.Round(fitScale * _zoom));
        var width = _canvasWidth * pixelScale;
        var height = _canvasHeight * pixelScale;
        return new Rectangle(
            workspace.X + (workspace.Width - width) / 2 + (int)MathF.Round(_canvasPan.X),
            workspace.Y + (workspace.Height - height) / 2 + (int)MathF.Round(_canvasPan.Y),
            width,
            height);
    }

    private void DrawToolButton(Rectangle bounds, EditorTool tool, MouseState mouse)
    {
        var fill = _activeTool == tool
            ? PixologyPalette.Accent
            : bounds.Contains(mouse.Position) ? new Color(0xED, 0xF0, 0xFC) : Color.White;
        DrawRectangle(bounds, fill);
        DrawOutline(bounds, _activeTool == tool ? PixologyPalette.Ink : PixologyPalette.Border, 2);
    }

    private void DrawToolIcons()
    {
        var tools = new[]
        {
            (new Rectangle(16, 0, 10, 10), 104),
            (new Rectangle(0, 16, 10, 10), 168),
            (new Rectangle(16, 16, 10, 10), 232),
            (new Rectangle(32, 16, 10, 10), 296),
            (new Rectangle(0, 32, 10, 10), 360),
            (new Rectangle(32, 0, 10, 10), 424)
        };
        foreach (var (source, y) in tools)
        {
            var icon = new Rectangle(SidebarWidth / 2 - 15, y + 13, 30, 30);
            _spriteBatch.Draw(_toolIcons, icon, source, PixologyPalette.Ink);
        }
    }

    private void DrawButton(Rectangle bounds, string label, MouseState mouse, float textScale = 1f, bool prominent = false)
    {
        var hovered = bounds.Contains(mouse.Position);
        var fill = hovered
            ? prominent ? PixologyPalette.Ink : new Color(0xED, 0xF0, 0xFC)
            : Color.White;
        var foreground = hovered && prominent ? Color.White : PixologyPalette.Ink;
        var borderColor = hovered && prominent ? Color.White : PixologyPalette.Ink;
        var borderWidth = prominent ? 3 : 2;

        DrawRectangle(bounds, fill);
        DrawOutline(bounds, borderColor, borderWidth);

        var textSize = _font.MeasureString(label) * textScale;
        var textPosition = new Vector2(
            bounds.Center.X - textSize.X / 2,
            bounds.Center.Y - textSize.Y / 2);
        DrawPixelText(_font, label, textPosition, foreground, textScale);
    }

    private void DrawPixelText(string text, Vector2 position, Color color)
    {
        DrawPixelText(_font, text, position, color);
    }

    private void DrawVerticallyCenteredText(string text, int x, int centerY)
    {
        var size = _font.MeasureString(text);
        DrawPixelText(
            text,
            new Vector2(x, MathF.Round(centerY - size.Y / 2)),
            PixologyPalette.Ink);
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

    private void DrawFolderIcon(Rectangle bounds)
    {
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, 8, 4), PixologyPalette.Accent);
        DrawRectangle(new Rectangle(bounds.X + 1, bounds.Y + 1, 6, 2), PixologyPalette.Ink);
        DrawRectangle(new Rectangle(bounds.X, bounds.Y + 4, bounds.Width, bounds.Height - 4), PixologyPalette.Ink);
        DrawRectangle(new Rectangle(bounds.X + 1, bounds.Y + 5, bounds.Width - 2, bounds.Height - 6), PixologyPalette.Accent);
    }

    private void DrawOutline(Rectangle bounds, Color color, int thickness)
    {
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
        DrawRectangle(new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
        DrawRectangle(new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
        DrawRectangle(new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
    }
}
