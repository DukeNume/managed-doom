﻿//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//



using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SFML.Graphics;
using SFML.System;

namespace ManagedDoom.SoftwareRendering
{
    public sealed class SfmlRenderer : IRenderer, IDisposable
    {
        private static double[] gammaCorrectionParameters = new double[]
        {
            1.00,
            0.95,
            0.90,
            0.85,
            0.80,
            0.75,
            0.70,
            0.65,
            0.60,
            0.55,
            0.50
        };

        private Config config;

        private RenderWindow sfmlWindow;
        private Palette palette;

        private DrawScreen screen;

        private int sfmlTextureWidth;
        private int sfmlTextureHeight;

        private byte[] sfmlTextureData;
        private SFML.Graphics.Texture sfmlTexture;
        private SFML.Graphics.RenderStates sfmlStatesMain;
        private SFML.Graphics.RenderStates sfmlStatesBlackBars;

        private MenuRenderer menu;
        private ThreeDRenderer threeD;
        private StatusBarRenderer statusBar;
        private IntermissionRenderer intermission;
        private OpeningSequenceRenderer openingSequence;
        private AutoMapRenderer autoMap;
        private FinaleRenderer finale;

        private Patch pause;

        private int wipeBandWidth;
        private int wipeBandCount;
        private int wipeHeight;
        private byte[] wipeBuffer;

        private SFML.Graphics.Vertex[] sfmlVerticesMain;
        private SFML.Graphics.Vertex[][] sfmlVerticesBlackBars;

        public SfmlRenderer(Config config, RenderWindow window, CommonResource resource)
        {
            try
            {
                Console.Write("Initialize renderer: ");

                this.config = config;

                config.video_gamescreensize = Math.Clamp(config.video_gamescreensize, 0, MaxWindowSize);
                config.video_gammacorrection = Math.Clamp(config.video_gammacorrection, 0, MaxGammaCorrectionLevel);

                sfmlWindow = window;
                palette = resource.Palette;

                if (config.video_highresolution)
                {
                    screen = new DrawScreen(resource.Wad, 640, 400);
                    sfmlTextureWidth = 512;
                    sfmlTextureHeight = 1024;
                }
                else
                {
                    screen = new DrawScreen(resource.Wad, 320, 200);
                    sfmlTextureWidth = 256;
                    sfmlTextureHeight = 512;
                }

                sfmlTextureData = new byte[4 * screen.Width * screen.Height];

                sfmlTexture = new SFML.Graphics.Texture((uint)sfmlTextureWidth, (uint)sfmlTextureHeight);

                sfmlStatesMain = new RenderStates();
                sfmlStatesMain.Texture = sfmlTexture;
                sfmlStatesMain.BlendMode = BlendMode.None;
                sfmlStatesMain.Transform = Transform.Identity;

                sfmlStatesBlackBars = new RenderStates();
                sfmlStatesBlackBars.BlendMode = BlendMode.None;
                sfmlStatesBlackBars.Transform = Transform.Identity;

                menu = new MenuRenderer(resource.Wad, screen);
                threeD = new ThreeDRenderer(resource, screen, config.video_gamescreensize);
                statusBar = new StatusBarRenderer(resource.Wad, screen);
                intermission = new IntermissionRenderer(resource.Wad, screen);
                openingSequence = new OpeningSequenceRenderer(resource.Wad, screen, this);
                autoMap = new AutoMapRenderer(resource.Wad, screen);
                finale = new FinaleRenderer(resource, screen);

                pause = Patch.FromWad(resource.Wad, "M_PAUSE");

                var scale = screen.Width / 320;
                wipeBandWidth = 2 * scale;
                wipeBandCount = screen.Width / wipeBandWidth + 1;
                wipeHeight = screen.Height / scale;
                wipeBuffer = new byte[screen.Data.Length];

                palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);

                ResetScreenSize((int)window.Size.X, (int)window.Size.Y);

                sfmlWindow.Resized += SfmlWindow_Resized;

                Console.WriteLine("OK");
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed");
                Dispose();
                ExceptionDispatchInfo.Throw(e);
            }
        }

        private void ResetScreenSize(int width, int height)
        {
            sfmlWindow.SetView(new View(new FloatRect(0, 0, width, height)));

            sfmlVerticesMain = new SFML.Graphics.Vertex[4];

            sfmlVerticesMain[0] = new SFML.Graphics.Vertex(
                new Vector2f(0, 0),
                Color.White,
                new Vector2f(0, 0));

            sfmlVerticesMain[1] = new SFML.Graphics.Vertex(
                new Vector2f(0, height),
                Color.White,
                new Vector2f(screen.Height, 0));

            sfmlVerticesMain[2] = new SFML.Graphics.Vertex(
                new Vector2f(width, 0),
                Color.White,
                new Vector2f(0, screen.Width));

            sfmlVerticesMain[3] = new SFML.Graphics.Vertex(
                new Vector2f(width, height),
                Color.White,
                new Vector2f(screen.Height, screen.Width));
        }

        private void SfmlWindow_Resized(object sender, SFML.Window.SizeEventArgs e)
        {
            ResetScreenSize((int)e.Width, (int)e.Height);
        }

        public void RenderApplication(DoomApplication app)
        {
            if (app.State == ApplicationState.Opening)
            {
                openingSequence.Render(app.Opening);
            }
            else if (app.State == ApplicationState.DemoPlayback)
            {
                RenderGame(app.DemoPlayback.Game);
            }
            else if (app.State == ApplicationState.Game)
            {
                RenderGame(app.Game);
            }

            if (!app.Menu.Active)
            {
                if (app.State == ApplicationState.Game &&
                    app.Game.State == GameState.Level &&
                    app.Game.Paused)
                {
                    var scale = screen.Width / 320;
                    screen.DrawPatch(
                        pause,
                        (screen.Width - scale * pause.Width) / 2,
                        4 * scale,
                        scale);
                }
            }
        }

        public void RenderMenu(DoomApplication app)
        {
            if (app.Menu.Active)
            {
                menu.Render(app.Menu);
            }
        }

        public void RenderGame(DoomGame game)
        {
            if (game.State == GameState.Level)
            {
                var consolePlayer = game.World.ConsolePlayer;
                var displayPlayer = game.World.DisplayPlayer;

                if (game.World.AutoMap.Visible)
                {
                    autoMap.Render(consolePlayer);
                    statusBar.Render(consolePlayer, true);
                }
                else
                {
                    threeD.Render(displayPlayer);
                    if (threeD.WindowSize < 8)
                    {
                        statusBar.Render(consolePlayer, true);
                    }
                    else if (threeD.WindowSize == ThreeDRenderer.MaxScreenSize)
                    {
                        statusBar.Render(consolePlayer, false);
                    }
                }

                if (config.video_displaymessage || ReferenceEquals(consolePlayer.Message, (string)DoomInfo.Strings.MSGOFF))
                {
                    if (consolePlayer.MessageTime > 0)
                    {
                        var scale = screen.Width / 320;
                        screen.DrawText(consolePlayer.Message, 0, 7 * scale, scale);
                    }
                }
            }
            else if (game.State == GameState.Intermission)
            {
                intermission.Render(game.Intermission);
            }
            else if (game.State == GameState.Finale)
            {
                finale.Render(game.Finale);
            }
        }

        public void Render(DoomApplication app)
        {
            RenderApplication(app);
            RenderMenu(app);

            var colors = palette[0];
            if (app.State == ApplicationState.Game &&
                app.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(app.Game.World.ConsolePlayer)];
            }
            else if (app.State == ApplicationState.Opening &&
                app.Opening.State == OpeningSequenceState.Demo &&
                app.Opening.DemoGame.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(app.Opening.DemoGame.World.ConsolePlayer)];
            }
            else if (app.State == ApplicationState.DemoPlayback &&
                app.DemoPlayback.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(app.DemoPlayback.Game.World.ConsolePlayer)];
            }

            Display(colors);
        }

        public void RenderWipe(DoomApplication app, WipeEffect wipe)
        {
            RenderApplication(app);

            var scale = screen.Width / 320;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)MathF.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(app);

            Display(palette[0]);
        }

        public void InitializeWipe()
        {
            Array.Copy(screen.Data, wipeBuffer, screen.Data.Length);
        }

        private void Display(uint[] colors)
        {
            var screenData = screen.Data;
            var p = MemoryMarshal.Cast<byte, uint>(sfmlTextureData);
            for (var i = 0; i < p.Length; i++)
            {
                p[i] = colors[screenData[i]];
            }
            sfmlTexture.Update(sfmlTextureData, (uint)screen.Height, (uint)screen.Width, 0, 0);
            sfmlWindow.Draw(sfmlVerticesMain, PrimitiveType.TriangleStrip, sfmlStatesMain);
            sfmlWindow.Display();
        }

        private static int GetPaletteNumber(Player player)
        {
            var count = player.DamageCount;

            if (player.Powers[(int)PowerType.Strength] != 0)
            {
                // Slowly fade the berzerk out.
                var bzc = 12 - (player.Powers[(int)PowerType.Strength] >> 6);
                if (bzc > count)
                {
                    count = bzc;
                }
            }

            int palette;

            if (count != 0)
            {
                palette = (count + 7) >> 3;

                if (palette >= Palette.DamageCount)
                {
                    palette = Palette.DamageCount - 1;
                }

                palette += Palette.DamageStart;
            }
            else if (player.BonusCount != 0)
            {
                palette = (player.BonusCount + 7) >> 3;

                if (palette >= Palette.BonusCount)
                {
                    palette = Palette.BonusCount - 1;
                }

                palette += Palette.BonusStart;
            }
            else if (player.Powers[(int)PowerType.IronFeet] > 4 * 32 ||
                (player.Powers[(int)PowerType.IronFeet] & 8) != 0)
            {
                palette = Palette.IronFeet;
            }
            else
            {
                palette = 0;
            }

            return palette;
        }

        public void Dispose()
        {
            Console.WriteLine("Shutdown renderer.");

            if (sfmlTexture != null)
            {
                sfmlTexture.Dispose();
                sfmlTexture = null;
            }
        }

        public int WipeBandCount => wipeBandCount;
        public int WipeHeight => wipeHeight;

        public int MaxWindowSize
        {
            get
            {
                return ThreeDRenderer.MaxScreenSize;
            }
        }

        public int WindowSize
        {
            get
            {
                return threeD.WindowSize;
            }

            set
            {
                config.video_gamescreensize = value;
                threeD.WindowSize = value;
            }
        }

        public bool DisplayMessage
        {
            get
            {
                return config.video_displaymessage;
            }

            set
            {
                config.video_displaymessage = value;
            }
        }

        public int MaxGammaCorrectionLevel
        {
            get
            {
                return gammaCorrectionParameters.Length - 1;
            }
        }

        public int GammaCorrectionLevel
        {
            get
            {
                return config.video_gammacorrection;
            }

            set
            {
                config.video_gammacorrection = value;
                palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
            }
        }
    }
}
