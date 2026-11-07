using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Pixology;

public sealed class PixologyGame : Game
{
    private const int ButtonWidth = 220;
    private const int ButtonHeight = 64;
    private const int ButtonGap = 24;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;

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
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.White);

        var viewport = GraphicsDevice.Viewport;
        var firstButton = new Rectangle(
            (viewport.Width - (ButtonWidth * 2 + ButtonGap)) / 2,
            (viewport.Height - ButtonHeight) / 2,
            ButtonWidth,
            ButtonHeight);
        var secondButton = firstButton;
        secondButton.X += ButtonWidth + ButtonGap;

        var mouse = Mouse.GetState();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawButton(firstButton, "New", mouse);
        DrawButton(secondButton, "Open File", mouse);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawButton(Rectangle bounds, string label, MouseState mouse)
    {
        var hovered = bounds.Contains(mouse.Position);
        var fill = hovered ? new Color(242, 242, 242) : Color.White;
        const int borderWidth = 2;

        _spriteBatch.Draw(_pixel, bounds, fill);
        _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, borderWidth), Color.Black);
        _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Bottom - borderWidth, bounds.Width, borderWidth), Color.Black);
        _spriteBatch.Draw(_pixel, new Rectangle(bounds.X, bounds.Y, borderWidth, bounds.Height), Color.Black);
        _spriteBatch.Draw(_pixel, new Rectangle(bounds.Right - borderWidth, bounds.Y, borderWidth, bounds.Height), Color.Black);

        var textSize = _font.MeasureString(label);
        var textPosition = new Vector2(
            bounds.Center.X - textSize.X / 2,
            bounds.Center.Y - textSize.Y / 2);
        _spriteBatch.DrawString(_font, label, textPosition, Color.Black);
    }
}
