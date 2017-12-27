// File: BaseGame.cs
// Created: 13.10.2017
// 
// See <summary> tags for more information.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using AForge;
using AForge.Imaging.Filters;
using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = System.Drawing.Rectangle;

namespace Chirp
{
    public class BaseGame : Game
    {
        private static readonly Vector2[] transformAnchors = new Vector2[4];
        private static int anchorState = -1;
        private static bool lastVisibilityState;

        private static int yOffset;
        private static int yHeight;

        private static SimpleQuadrilateralTransformation quadrilateralFilter;
        private readonly GraphicsDeviceManager graphics;
        private Bitmap image;
        private SpriteBatch spriteBatch;
        private VideoCaptureDevice videoSource;

        private List<Vector2> drawing = new List<Vector2>();

        public BaseGame()
        {
            this.graphics = new GraphicsDeviceManager(this);
            this.Content.RootDirectory = "Content";
        }

        protected override void Initialize()
        {
            this.IsFixedTimeStep = true;
            this.TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 1000 / 30);
            this.IsMouseVisible = true;

            this.graphics.PreferredBackBufferWidth = 1024;
            this.graphics.PreferredBackBufferHeight = 768;
            this.graphics.ApplyChanges();

            BaseGame.yHeight = (int) (1024f / 16 * 9);
            BaseGame.yOffset = 768 - BaseGame.yHeight;

            var videosources = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videosources.Count > 0)
            {
                for (var i = 0; i < videosources.Count; i++)
                {
                    if (videosources[i].MonikerString.Contains("usb") && videosources[i].Name.Contains("Logitech"))
                    {
                        this.videoSource = new VideoCaptureDevice(videosources[i].MonikerString);
                        this.videoSource.NewFrame += this.videoSource_NewFrame;
                        this.videoSource.Start();
                        break;
                    }
                }

                if (this.videoSource == null)
                {
                    this.Exit();
                }
            }
            else
            {
                this.Exit();
            }

            base.Initialize();
        }

        private void videoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            var orig = (Bitmap) eventArgs.Frame.Clone();
            var clone = new Bitmap(orig.Width, orig.Height,
                PixelFormat.Format32bppArgb);

            using (var gr = Graphics.FromImage(clone))
            {
                gr.DrawImage(orig, new Rectangle(0, 0, clone.Width, clone.Height));
            }

            orig.Dispose();
            if (this.image != null)
            {
                lock (this.image)
                {
                    this.image = clone;
                }
            }
            else
            {
                this.image = clone;
            }
        }

        protected override void LoadContent()
        {
            this.spriteBatch = new SpriteBatch(this.GraphicsDevice);
        }

        protected override void UnloadContent()
        {
            this.videoSource.Stop();
        }

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                this.Exit();
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            this.GraphicsDevice.Clear(Color.White);
            this.spriteBatch.Begin();

            if (this.image != null)
            {
                lock (this.image)
                {
                    var bmp = this.image;
                    bmp = BaseGame.Grayscale(bmp);
                    var t2d = BaseGame.GetTexture2DFromBitmap(this.GraphicsDevice, bmp);
                    this.spriteBatch.Draw(t2d,
                        Vector2.Zero, null, Color.White, 0f, Vector2.Zero, new Vector2(0.4f, 0.4f),
                        SpriteEffects.None, 0f);

                    if (BaseGame.anchorState == 4)
                    {
                        // Draw phase, apply transform
                        bmp = BaseGame.AnchorQuadrilateral(bmp);
                    }

                    bmp = BaseGame.Threshhold(bmp);
                    var t2d2 = BaseGame.GetTexture2DFromBitmap(this.GraphicsDevice, bmp);
                    this.spriteBatch.Draw(t2d2,
                        new Vector2(this.graphics.PreferredBackBufferWidth - t2d2.Width * 0.4f, 0), null, Color.White,
                        0f,
                        Vector2.Zero, new Vector2(0.4f, 0.4f),
                        SpriteEffects.None, 0f);
                    
                    bmp = BaseGame.Shrink(bmp, 0.4f);

                    var brightPoint = BaseGame.Position(bmp);
                    if (brightPoint.HasValue)
                    {
                        var previewPos = BaseGame.ToScreenCoord(brightPoint.Value,
                            new Size((int) (t2d2.Width * 0.4f), (int) (t2d2.Height * 0.4f)), bmp.Size);
                        this.spriteBatch.DrawPoint(
                            previewPos.X + this.graphics.PreferredBackBufferWidth - t2d2.Width * 0.4f, previewPos.Y,
                            Color.Red, 5F);

                        if (BaseGame.anchorState < 4)
                        {
                            // Setup phase
                            if (!BaseGame.lastVisibilityState && BaseGame.anchorState >= 0)
                            {
                                // Rising edge
                                BaseGame.transformAnchors[BaseGame.anchorState] = brightPoint.Value.ToVector2()/0.4f;
                                BaseGame.anchorState++;
                            }
                        }
                        else
                        {
                            // Draw phase
                            var truePos = BaseGame.ToScreenCoord(brightPoint.Value,
                                new Size(this.graphics.PreferredBackBufferWidth, BaseGame.yHeight), bmp.Size);
                            this.drawing.Add(new Vector2(truePos.X, truePos.Y + BaseGame.yOffset));
                        }
                    }

                    BaseGame.lastVisibilityState = brightPoint.HasValue;
                }

                switch (BaseGame.anchorState)
                {
                    case 0:
                        this.spriteBatch.DrawPoint(10, BaseGame.yOffset + 10, Color.Red, 10f);
                        break;
                    case 1:
                        this.spriteBatch.DrawPoint(this.graphics.PreferredBackBufferWidth - 10, BaseGame.yOffset + 10,
                            Color.Red,
                            10f);
                        break;
                    case 2:
                        this.spriteBatch.DrawPoint(this.graphics.PreferredBackBufferWidth - 10,
                            BaseGame.yOffset + BaseGame.yHeight - 10, Color.Red, 10f);
                        break;
                    case 3:
                        this.spriteBatch.DrawPoint(10, BaseGame.yHeight + BaseGame.yOffset - 10, Color.Red, 10f);
                        break;
                    default:
                        foreach (var vector2 in this.drawing)
                        {
                            this.spriteBatch.DrawPoint(vector2, Color.Red, 6f);
                        }
                        break;
                }

                if (Keyboard.GetState().IsKeyDown(Keys.C))
                {
                    this.drawing.Clear();
                }

                if (Keyboard.GetState().IsKeyDown(Keys.Space))
                {
                    BaseGame.anchorState = 0;
                }
            }

            this.spriteBatch.End();
            base.Draw(gameTime);
        }

        public static Texture2D GetTexture2DFromBitmap(GraphicsDevice device, Bitmap bitmap)
        {
            var tex = new Texture2D(device, bitmap.Width, bitmap.Height, false, SurfaceFormat.Bgr32);

            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppRgb);

            var bufferSize = data.Height * data.Stride;

            //create data buffer 
            var bytes = new byte[bufferSize];

            // copy bitmap data into buffer
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            // copy our buffer to the texture
            tex.SetData(bytes);

            // unlock the bitmap data
            bitmap.UnlockBits(data);

            return tex;
        }

        public static Bitmap Grayscale(Bitmap img)
        {
            var grayscale = new Grayscale(0.2125, 0.7154, 0.0721);
            return grayscale.Apply(img);
        }

        public static Bitmap Threshhold(Bitmap img)
        {
            var threshhold = new Threshold(100);
            return threshhold.Apply(img);
        }

        public static Bitmap Shrink(Bitmap img, float scaleFactor)
        {
            var newX = (int) (img.Width * scaleFactor);
            var newY = (int) (img.Height * scaleFactor);
            var scale = new ResizeNearestNeighbor(newX, newY);
            return scale.Apply(img);
        }

        public static Point? Position(Bitmap img)
        {
            var data = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            var bufferSize = data.Height * data.Stride;

            //create data buffer 
            var bytes = new byte[bufferSize];

            // copy bitmap data into buffer
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            img.UnlockBits(data);

            // Apply sliding window
            const int WSIZE = 2;

            var maxBrightness = 0;
            var maxBrightnessPosX = -1;
            var maxBrightnessPosY = -1;

            for (var y = 0; y < img.Height - WSIZE; y += WSIZE)
            {
                for (var x = 0; x < img.Width - WSIZE; x += WSIZE)
                {
                    var brightness = 0;
                    for (var y2 = 0; y2 < WSIZE; y2++)
                    {
                        for (var x2 = 0; x2 < WSIZE; x2++)
                        {
                            var xr = x + x2;
                            var yr = y + y2;
                            if (bytes[(yr * img.Width + xr) * 3] > 100)
                            {
                                brightness++;
                            }
                        }
                    }

                    if (brightness > maxBrightness)
                    {
                        maxBrightness = brightness;
                        maxBrightnessPosX = x + WSIZE / 2;
                        maxBrightnessPosY = y + WSIZE / 2;
                    }
                }
            }

            if (maxBrightness == 0)
            {
                return null;
            }

            return new Point(maxBrightnessPosX, maxBrightnessPosY);
        }

        public static Vector2 ToScreenCoord(Point p, Size screenSize, Size imgSize)
        {
            return new Vector2(screenSize.Width * (p.X / (float) imgSize.Width),
                screenSize.Height * (p.Y / (float) imgSize.Height));
        }

        public static Bitmap AnchorQuadrilateral(Bitmap img)
        {
            if (BaseGame.quadrilateralFilter == null)
            {
                BaseGame.quadrilateralFilter =
                    new SimpleQuadrilateralTransformation(
                        BaseGame.transformAnchors.Select(x => new IntPoint((int) x.X, (int) x.Y)).ToList(), img.Width, img.Height);
            }

            return BaseGame.quadrilateralFilter.Apply(img);
        }
    }
}