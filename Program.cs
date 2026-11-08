using System;
using System.IO;
using Pixology;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

using var game = new PixologyGame();
game.Run();
