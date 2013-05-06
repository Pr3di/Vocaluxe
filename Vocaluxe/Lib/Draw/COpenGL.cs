﻿#region license
// /*
//     This file is part of Vocaluxe.
// 
//     Vocaluxe is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     Vocaluxe is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
// 
//     You should have received a copy of the GNU General Public License
//     along with Vocaluxe. If not, see <http://www.gnu.org/licenses/>.
//  */
#endregion

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using OpenTK;
using OpenTK.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Vocaluxe.Base;
using VocaluxeLib.Draw;
using VocaluxeLib.Menu;
using BeginMode = OpenTK.Graphics.OpenGL.BeginMode;
using BlendingFactorDest = OpenTK.Graphics.OpenGL.BlendingFactorDest;
using BlendingFactorSrc = OpenTK.Graphics.OpenGL.BlendingFactorSrc;
using BufferAccess = OpenTK.Graphics.OpenGL.BufferAccess;
using BufferTarget = OpenTK.Graphics.OpenGL.BufferTarget;
using BufferUsageHint = OpenTK.Graphics.OpenGL.BufferUsageHint;
using ClearBufferMask = OpenTK.Graphics.OpenGL.ClearBufferMask;
using DepthFunction = OpenTK.Graphics.OpenGL.DepthFunction;
using EnableCap = OpenTK.Graphics.OpenGL.EnableCap;
using GL = OpenTK.Graphics.OpenGL.GL;
using GenerateMipmapTarget = OpenTK.Graphics.OpenGL.GenerateMipmapTarget;
using KeyPressEventArgs = System.Windows.Forms.KeyPressEventArgs;
using MatrixMode = OpenTK.Graphics.OpenGL.MatrixMode;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;
using PixelInternalFormat = OpenTK.Graphics.OpenGL.PixelInternalFormat;
using PixelStoreParameter = OpenTK.Graphics.OpenGL.PixelStoreParameter;
using PixelType = OpenTK.Graphics.OpenGL.PixelType;
using TextureMagFilter = OpenTK.Graphics.OpenGL.TextureMagFilter;
using TextureMinFilter = OpenTK.Graphics.OpenGL.TextureMinFilter;
using TextureParameterName = OpenTK.Graphics.OpenGL.TextureParameterName;
using TextureTarget = OpenTK.Graphics.OpenGL.TextureTarget;

namespace Vocaluxe.Lib.Draw
{
    struct SClientRect
    {
        public Point Location;
        public int Width;
        public int Height;
    };

    struct STextureQueue
    {
        public int Index;
        public int Width;
        public int Height;
        public byte[] Data;
    }

    class COpenGL : Form, IDraw
    {
        #region private vars
        private readonly CKeys _Keys;
        private readonly CMouse _Mouse;
        private bool _Run;

        private readonly GLControl _Control;

        private SClientRect _Restore;
        private bool _Fullscreen;

        private readonly Dictionary<int, CTexture> _Textures;
        private readonly Queue<int> _IDs;
        private readonly List<STextureQueue> _Queue;

        private readonly Object _MutexTexture = new Object();

        private int _H = 1;
        private int _W = 1;
        private int _Y;
        private int _X;

        private bool _UsePBO;
        #endregion private vars

        public COpenGL()
        {
            Icon = new Icon(Path.Combine(Environment.CurrentDirectory, CSettings.Icon));

            _Textures = new Dictionary<int, CTexture>();
            _Queue = new List<STextureQueue>();
            _IDs = new Queue<int>(1000000);

            for (int i = 1; i < 1000000; i++)
                _IDs.Enqueue(i);

            //Check AA Mode
            CConfig.AAMode = (EAntiAliasingModes)_CheckAntiAliasingMode((int)CConfig.AAMode);

            bool ok = false;
            try
            {
                GraphicsMode gm = new GraphicsMode(32, 24, 0, (int)CConfig.AAMode);
                _Control = new GLControl(gm, 2, 1, GraphicsContextFlags.Default);
                if (_Control.GraphicsMode != null)
                    ok = true;
            }
            catch (Exception)
            {
                ok = false;
            }

            if (!ok)
                _Control = new GLControl();

            _Control.MakeCurrent();
            _Control.VSync = CConfig.VSync == EOffOn.TR_CONFIG_ON;

            Controls.Add(_Control);


            _Keys = new CKeys();
            Paint += _OnPaintEvent;
            Closing += _OnClosingEvent;
            Resize += _OnResizeEvent;

            _Control.KeyDown += _OnKeyDownEvent;
            _Control.PreviewKeyDown += _OnPreviewKeyDownEvent;
            _Control.KeyPress += _OnKeyPressEvent;
            _Control.KeyUp += _OnKeyUpEvent;

            _Mouse = new CMouse();
            _Control.MouseMove += _OnMouseMove;
            _Control.MouseWheel += _OnMouseWheel;
            _Control.MouseDown += _OnMouseDown;
            _Control.MouseUp += _OnMouseUp;
            _Control.MouseLeave += _OnMouseLeave;
            _Control.MouseEnter += _OnMouseEnter;

            ClientSize = new Size(CConfig.ScreenW, CConfig.ScreenH);
            CenterToScreen();
        }

        #region Helpers
        private int _CheckAntiAliasingMode(int setValue)
        {
            int samples = 0;

            if (setValue > 32)
                setValue = 32;

            while (samples <= setValue)
            {
                GraphicsMode mode;
                try
                {
                    mode = new GraphicsMode(16, 0, 0, samples);
                }
                catch (Exception)
                {
                    break;
                }

                if (mode.Samples != samples)
                    break;
                if (samples == 0)
                    samples = 2;
                else
                    samples *= 2;
            }

            if (samples == 2)
                return 0;
            return samples / 2;
        }

        /*
        private int _CheckColorDepth(int setValue)
        {
            int result = 16;

            if (setValue > 32)
                setValue = 32;

            while (result <= setValue)
            {
                GraphicsMode mode;
                try
                {
                    mode = new GraphicsMode(result, 0, 0, 0);
                }
                catch (Exception)
                {
                    break;
                }
                if (mode.ColorFormat != result)
                    break;
                result += 8;
            }

            return result - 8;
        }
*/

        private void _ToggleFullScreen()
        {
            if (!_Fullscreen)
                _EnterFullScreen();
            else
                _LeaveFullScreen();
        }

        private void _EnterFullScreen()
        {
            _Fullscreen = true;
            CConfig.FullScreen = EOffOn.TR_CONFIG_ON;

            _Restore.Location = Location;
            _Restore.Width = Width;
            _Restore.Height = Height;

            FormBorderStyle = FormBorderStyle.None;

            int screenNr = 0;
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                Screen scr = Screen.AllScreens[i];
                if (scr.Bounds.Top <= Top && scr.Bounds.Left <= Left)
                    screenNr = i;
            }

            DesktopBounds = new Rectangle(Screen.AllScreens[screenNr].Bounds.Location, new Size(Screen.AllScreens[screenNr].Bounds.Width, Screen.AllScreens[screenNr].Bounds.Height));

            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                _DoResize();
                WindowState = FormWindowState.Maximized;
            }
            else
                _DoResize();

            CConfig.SaveConfig();
        }

        private void _LeaveFullScreen()
        {
            _Fullscreen = false;
            CConfig.FullScreen = EOffOn.TR_CONFIG_OFF;

            FormBorderStyle = FormBorderStyle.Sizable;
            DesktopBounds = new Rectangle(_Restore.Location, new Size(_Restore.Width, _Restore.Height));

            CConfig.SaveConfig();
        }
        #endregion Helpers

        #region form events
        private void _OnPaintEvent(object sender, PaintEventArgs e) {}

        private void _OnResizeEvent(object sender, EventArgs e) {}

        private void _OnClosingEvent(object sender, CancelEventArgs e)
        {
            _Run = false;
        }

        protected override void OnLoad(EventArgs e)
        {
            GL.ClearColor(Color.Black);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            _Control.ClientSize = ClientSize;
            _DoResize();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x112: // WM_SYSCOMMAND
                    switch ((int)m.WParam & 0xFFF0)
                    {
                        case 0xF100: // SC_KEYMENU
                            m.Result = IntPtr.Zero;
                            break;
                        default:
                            base.WndProc(ref m);
                            break;
                    }
                    break;
                default:
                    base.WndProc(ref m);
                    break;
            }
        }
        #endregion form events

        #region mouse event handlers
        private void _OnMouseMove(object sender, MouseEventArgs e)
        {
            _Mouse.MouseMove(e);
        }

        private void _OnMouseWheel(object sender, MouseEventArgs e)
        {
            _Mouse.MouseWheel(e);
        }

        private void _OnMouseDown(object sender, MouseEventArgs e)
        {
            _Mouse.MouseDown(e);
        }

        private void _OnMouseUp(object sender, MouseEventArgs e)
        {
            _Mouse.MouseUp(e);
        }

        private void _OnMouseLeave(object sender, EventArgs e)
        {
            _Mouse.Visible = false;
            Cursor.Show();
        }

        private void _OnMouseEnter(object sender, EventArgs e)
        {
            Cursor.Hide();
            _Mouse.Visible = true;
        }
        #endregion

        #region keyboard event handlers
        private void _OnPreviewKeyDownEvent(object sender, PreviewKeyDownEventArgs e)
        {
            _OnKeyDownEvent(sender, new KeyEventArgs(e.KeyData));
        }

        private void _OnKeyDownEvent(object sender, KeyEventArgs e)
        {
            _Keys.KeyDown(e);
        }

        private void _OnKeyPressEvent(object sender, KeyPressEventArgs e)
        {
            _Keys.KeyPress(e);
        }

        private void _OnKeyUpEvent(object sender, KeyEventArgs e)
        {
            _Keys.KeyUp(e);
        }
        #endregion keyboard event handlers

        private void _DoResize()
        {
            _H = _Control.Height;
            _W = _Control.Width;
            _Y = 0;
            _X = 0;


            if (_W / (float)_H > CSettings.GetRenderAspect())
            {
                _W = (int)Math.Round(_H * CSettings.GetRenderAspect());
                _X = (_Control.Width - _W) / 2;
            }
            else
            {
                _H = (int)Math.Round(_W / CSettings.GetRenderAspect());
                _Y = (_Control.Height - _H) / 2;
            }

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(0, CSettings.RenderW, CSettings.RenderH, 0, CSettings.ZNear, CSettings.ZFar);
            GL.Viewport(_X, _Y, _W, _H);
        }

        #region implementation

        #region main stuff
        public bool Init()
        {
            Text = CSettings.GetFullVersionText();

            // Init Texturing
            GL.Enable(EnableCap.Texture2D);

            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            GL.DepthRange(CSettings.ZFar, CSettings.ZNear);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.DepthTest);

            return true;
        }

        public void MainLoop()
        {
            _Run = true;
            int delay = 0;
            Show();

            if (CConfig.FullScreen == EOffOn.TR_CONFIG_ON)
            {
                CSettings.IsFullScreen = true;
                _EnterFullScreen();
            }

            while (_Run)
            {
                Application.DoEvents();

                if (_Run)
                {
                    ClearScreen();
                    if (!CGraphics.Draw())
                        _Run = false;
                    if (!CGraphics.UpdateGameLogic(_Keys, _Mouse))
                        _Run = false;

                    _Control.SwapBuffers();

                    if ((CSettings.IsFullScreen && !_Fullscreen) || (!CSettings.IsFullScreen && _Fullscreen))
                        _ToggleFullScreen();

                    _CheckQueue();

                    if (CTime.IsRunning())
                        delay = (int)Math.Floor(CConfig.CalcCycleTime() - CTime.GetMilliseconds());

                    if (delay >= 1 && CConfig.VSync == EOffOn.TR_CONFIG_OFF)
                        Thread.Sleep(delay);

                    CTime.CalculateFPS();
                    CTime.Restart();
                }
            }
            Close();
        }

        public void Unload()
        {
            try
            {
                Close();
            }
            catch {}
            CTexture[] textures = new CTexture[_Textures.Count];
            _Textures.Values.CopyTo(textures, 0);
            for (int i = 0; i < _Textures.Count; i++)
                RemoveTexture(ref textures[i]);
        }

        public int GetScreenWidth()
        {
            return _Control.Width;
        }

        public int GetScreenHeight()
        {
            return _Control.Height;
        }
        #endregion main stuff

        #region Basic Draw Methods
        public void ClearScreen()
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public CTexture CopyScreen()
        {
            CTexture texture = _GetNewTexture(_W, _H);

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            texture.ID = id;
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, texture.W2, texture.H2, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _X, _Y, _W, _H);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Add to Texture List

            lock (_MutexTexture)
            {
                texture.Index = _IDs.Dequeue();
                _Textures[texture.Index] = texture;
            }

            return texture;
        }

        public void CopyScreen(ref CTexture texture)
        {
            //Check for actual texture sizes! (W2/H2) as it may be up/downsized compared to OrigSize
            if (!_TextureExists(texture) || texture.W2 != GetScreenWidth() || texture.H2 != GetScreenHeight())
            {
                RemoveTexture(ref texture);
                texture = CopyScreen();
            }
            else
            {
                GL.BindTexture(TextureTarget.Texture2D, texture.ID);

                GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, texture.OrigSize.Width, texture.OrigSize.Height);

                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }

        public void MakeScreenShot()
        {
            const string file = "Screenshot_";
            string path = Path.Combine(Environment.CurrentDirectory, CSettings.FolderScreenshots);

            int i = 0;
            while (File.Exists(Path.Combine(path, file + i.ToString("00000") + ".bmp")))
                i++;

            int width = GetScreenWidth();
            int height = GetScreenHeight();

            using (Bitmap screen = new Bitmap(width, height))
            {
                BitmapData bmpData = screen.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, bmpData.Scan0);
                screen.UnlockBits(bmpData);

                screen.RotateFlip(RotateFlipType.RotateNoneFlipY);
                screen.Save(Path.Combine(path, file + i.ToString("00000") + ".bmp"), ImageFormat.Bmp);
            }
        }

        public void DrawLine(int a, int r, int g, int b, int w, int x1, int y1, int x2, int y2)
        {
            GL.Enable(EnableCap.Blend);
            GL.Color4(r, g, b, a * CGraphics.GlobalAlpha);

            GL.Begin(BeginMode.Lines);
            GL.Vertex3(x1, y1, CGraphics.ZOffset);
            GL.Vertex3(x2, y2, CGraphics.ZOffset);
            GL.End();

            GL.Disable(EnableCap.Blend);
        }

        public void DrawColor(SColorF color, SRectF rect)
        {
            GL.Enable(EnableCap.Blend);
            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);

            GL.Begin(BeginMode.Quads);
            GL.Vertex3(rect.X, rect.Y, rect.Z + CGraphics.ZOffset);
            GL.Vertex3(rect.X, rect.Y + rect.H, rect.Z + CGraphics.ZOffset);
            GL.Vertex3(rect.X + rect.W, rect.Y + rect.H, rect.Z + CGraphics.ZOffset);
            GL.Vertex3(rect.X + rect.W, rect.Y, rect.Z + CGraphics.ZOffset);
            GL.End();

            GL.Disable(EnableCap.Blend);
        }

        public void DrawColorReflection(SColorF color, SRectF rect, float space, float height)
        {
            if (rect.H < height)
                height = rect.H;

            float rx1 = rect.X;
            float rx2 = rect.X + rect.W;
            float ry1 = rect.Y + rect.H + space;
            float ry2 = rect.Y + rect.H + space + height;

            if (rx1 < rect.X)
                rx1 = rect.X;

            if (rx2 > rect.X + rect.W)
                rx2 = rect.X + rect.W;

            if (ry1 < rect.Y + space)
                ry1 = rect.Y + space;

            if (ry2 > rect.Y + rect.H + space + height)
                ry2 = rect.Y + rect.H + space + height;


            GL.Enable(EnableCap.Blend);

            if (Math.Abs(rect.Rotation) > 0.001)
            {
                GL.Translate(0.5f, 0.5f, 0);
                GL.Rotate(-rect.Rotation, 0f, 0f, 1f);
                GL.Translate(-0.5f, -0.5f, 0);
            }

            GL.Begin(BeginMode.Quads);

            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);
            GL.Vertex3(rx2, ry1, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, 0f);
            GL.Vertex3(rx2, ry2, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, 0f);
            GL.Vertex3(rx1, ry2, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);
            GL.Vertex3(rx1, ry1, rect.Z + CGraphics.ZOffset);

            GL.End();

            GL.Disable(EnableCap.Blend);
        }
        #endregion Basic Draw Methods

        #region Textures

        #region adding
        public CTexture AddTexture(string texturePath)
        {
            if (!File.Exists(texturePath))
            {
                CLog.LogError("Can't find File: " + texturePath);
                return null;
            }
            Bitmap bmp;
            try
            {
                bmp = new Bitmap(texturePath);
            }
            catch (Exception)
            {
                CLog.LogError("Error loading Texture: " + texturePath);
                return null;
            }
            try
            {
                return AddTexture(bmp);
            }
            finally
            {
                bmp.Dispose();
            }
        }

        public CTexture AddTexture(Bitmap bmp)
        {
            if (bmp.Height == 0 || bmp.Width == 0)
                return null;

            int maxSize;
            switch (CConfig.TextureQuality)
            {
                case ETextureQuality.TR_CONFIG_TEXTURE_LOWEST:
                    maxSize = 128;
                    break;
                case ETextureQuality.TR_CONFIG_TEXTURE_LOW:
                    maxSize = 256;
                    break;
                case ETextureQuality.TR_CONFIG_TEXTURE_MEDIUM:
                    maxSize = 512;
                    break;
                case ETextureQuality.TR_CONFIG_TEXTURE_HIGH:
                    maxSize = 1024;
                    break;
                case ETextureQuality.TR_CONFIG_TEXTURE_HIGHEST:
                    maxSize = 2048;
                    break;
                default:
                    maxSize = 512;
                    break;
            }

            int w = Math.Min(bmp.Width, maxSize);
            int h = Math.Min(bmp.Height, maxSize);
            w = MathHelper.NextPowerOfTwo(w);
            h = MathHelper.NextPowerOfTwo(h);

            CTexture texture = new CTexture(bmp.Width, bmp.Height, w, h, true);

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            texture.ID = id;
            Bitmap bmp2 = null;
            try
            {
                if (bmp.Width != w || bmp.Height != h)
                {
                    //Create a new Bitmap with the new sizes
                    bmp2 = new Bitmap(w, h);
                    //Scale the texture
                    using (Graphics g = Graphics.FromImage(bmp2))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.DrawImage(bmp, new Rectangle(0, 0, bmp2.Width, bmp2.Height));
                    }
                    bmp = bmp2;
                }

                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, texture.W2, texture.H2, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, bmpData.Width, bmpData.Height, PixelFormat.Bgra, PixelType.UnsignedByte, bmpData.Scan0);
                bmp.UnlockBits(bmpData);
            }
            finally
            {
                if (bmp2 != null)
                    bmp2.Dispose();
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureParameterName.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureParameterName.ClampToEdge);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            //GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.Ext.GenerateMipmap(GenerateMipmapTarget.Texture2D);


            // Add to Texture List
            texture.TexturePath = String.Empty;

            lock (_MutexTexture)
            {
                texture.Index = _IDs.Dequeue();
                _Textures[texture.Index] = texture;
            }

            return texture;
        }

        public CTexture AddTexture(int w, int h, byte[] data)
        {
            CTexture texture = _GetNewTexture(w, h);

            _CreateTexture(texture, w, h, data);

            lock (_MutexTexture)
            {
                texture.Index = _IDs.Dequeue();
                _Textures[texture.Index] = texture;
            }

            return texture;
        }

        private void _CreateTexture(CTexture texture, int w, int h, byte[] data)
        {
            if (_UsePBO)
            {
                try
                {
                    GL.GenBuffers(1, out texture.PBO);
                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, texture.PBO);
                    GL.BufferData(BufferTarget.PixelUnpackBuffer, (IntPtr)data.Length, IntPtr.Zero, BufferUsageHint.StreamDraw);
                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                }
                catch (Exception)
                {
                    //throw;
                    _UsePBO = false;
                }
            }

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            texture.ID = id;

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, texture.W2, texture.H2, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, data);


            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureParameterName.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureParameterName.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            //GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.Ext.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public CTexture EnqueueTexture(int w, int h, byte[] data)
        {
            CTexture texture = _GetNewTexture(w, h);
            STextureQueue queue = new STextureQueue {Data = data, Height = h, Width = w};

            lock (_MutexTexture)
            {
                texture.Index = _IDs.Dequeue();
                queue.Index = texture.Index;
                _Queue.Add(queue);
                _Textures[texture.Index] = texture;
            }

            return texture;
        }

        private CTexture _GetNewTexture(int w, int h)
        {
            return new CTexture(w, h, MathHelper.NextPowerOfTwo(w), MathHelper.NextPowerOfTwo(h));
        }
        #endregion adding

        #region updating
        public bool UpdateTexture(CTexture texture, byte[] data)
        {
            if (!_TextureExists(texture))
                return false;
            if (_UsePBO)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, texture.PBO);

                IntPtr buffer = GL.MapBuffer(BufferTarget.PixelUnpackBuffer, BufferAccess.WriteOnly);
                Marshal.Copy(data, 0, buffer, data.Length);

                GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer);

                GL.BindTexture(TextureTarget.Texture2D, texture.ID);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.OrigSize.Width, texture.OrigSize.Height, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

                return true;
            }

            GL.BindTexture(TextureTarget.Texture2D, texture.ID);

            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.OrigSize.Width, texture.OrigSize.Height, PixelFormat.Bgra, PixelType.UnsignedByte, data);

            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureParameterName.ClampToEdge);
            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureParameterName.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            //GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            //GL.Ext.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            return true;
        }
        #endregion updating

        public void RemoveTexture(ref CTexture texture)
        {
            if (_TextureExists(texture, false))
            {
                lock (_MutexTexture)
                {
                    _IDs.Enqueue(texture.Index);
                    GL.DeleteTexture(texture.ID);
                    if (texture.PBO > 0)
                        GL.DeleteBuffers(1, ref texture.PBO);
                    _Textures.Remove(texture.Index);
                }
            }
            texture = null;
        }

        private bool _TextureExists(CTexture texture, bool checkIfLoaded = true)
        {
            lock (_MutexTexture)
            {
                if (texture != null && _Textures.ContainsKey(texture.Index))
                {
                    if (!checkIfLoaded || _Textures[texture.Index].ID > 0)
                        return true;
                }
            }
            return false;
        }

        #region drawing
        public void DrawTexture(CTexture texture)
        {
            if (texture == null)
                return;
            DrawTexture(texture, texture.Rect, texture.Color);
        }

        public void DrawTexture(CTexture texture, SRectF rect)
        {
            if (texture == null)
                return;
            DrawTexture(texture, rect, texture.Color);
        }

        public void DrawTexture(CTexture texture, SRectF rect, SColorF color, bool mirrored = false)
        {
            DrawTexture(texture, rect, color, new SRectF(0, 0, CSettings.RenderW, CSettings.RenderH, rect.Z), mirrored);
        }

        public void DrawTexture(CTexture texture, SRectF rect, SColorF color, SRectF bounds, bool mirrored = false)
        {
            if (!_TextureExists(texture))
                return;

            if (Math.Abs(rect.W) < float.Epsilon || Math.Abs(rect.H) < float.Epsilon || Math.Abs(bounds.H) < float.Epsilon || Math.Abs(bounds.W) < float.Epsilon ||
                Math.Abs(color.A) < float.Epsilon)
                return;

            if (bounds.X > rect.X + rect.W || bounds.X + bounds.W < rect.X)
                return;

            if (bounds.Y > rect.Y + rect.H || bounds.Y + bounds.H < rect.Y)
                return;

            GL.BindTexture(TextureTarget.Texture2D, texture.ID);

            float x1 = (bounds.X - rect.X) / rect.W * texture.WidthRatio;
            float x2 = (bounds.X + bounds.W - rect.X) / rect.W * texture.WidthRatio;
            float y1 = (bounds.Y - rect.Y) / rect.H * texture.HeightRatio;
            float y2 = (bounds.Y + bounds.H - rect.Y) / rect.H * texture.HeightRatio;

            if (x1 < 0)
                x1 = 0f;

            if (x2 > texture.WidthRatio)
                x2 = texture.WidthRatio;

            if (y1 < 0)
                y1 = 0f;

            if (y2 > texture.HeightRatio)
                y2 = texture.HeightRatio;


            float rx1 = rect.X;
            float rx2 = rect.X + rect.W;
            float ry1 = rect.Y;
            float ry2 = rect.Y + rect.H;

            if (rx1 < bounds.X)
                rx1 = bounds.X;

            if (rx2 > bounds.X + bounds.W)
                rx2 = bounds.X + bounds.W;

            if (ry1 < bounds.Y)
                ry1 = bounds.Y;

            if (ry2 > bounds.Y + bounds.H)
                ry2 = bounds.Y + bounds.H;

            GL.Enable(EnableCap.Blend);
            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);

            GL.MatrixMode(MatrixMode.Texture);
            GL.PushMatrix();

            if (Math.Abs(rect.Rotation) > float.Epsilon)
            {
                GL.Translate(0.5f, 0.5f, 0);
                GL.Rotate(-rect.Rotation, 0f, 0f, 1f);
                GL.Translate(-0.5f, -0.5f, 0);
            }

            if (!mirrored)
            {
                GL.Begin(BeginMode.Quads);

                GL.TexCoord2(x1, y1);
                GL.Vertex3(rx1, ry1, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x1, y2);
                GL.Vertex3(rx1, ry2, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x2, y2);
                GL.Vertex3(rx2, ry2, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x2, y1);
                GL.Vertex3(rx2, ry1, rect.Z + CGraphics.ZOffset);

                GL.End();
            }
            else
            {
                GL.Begin(BeginMode.Quads);

                GL.TexCoord2(x2, y2);
                GL.Vertex3(rx2, ry1, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x2, y1);
                GL.Vertex3(rx2, ry2, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x1, y1);
                GL.Vertex3(rx1, ry2, rect.Z + CGraphics.ZOffset);

                GL.TexCoord2(x1, y2);
                GL.Vertex3(rx1, ry1, rect.Z + CGraphics.ZOffset);

                GL.End();
            }

            GL.PopMatrix();

            GL.Disable(EnableCap.Blend);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void DrawTexture(CTexture texture, SRectF rect, SColorF color, float begin, float end)
        {
            if (!_TextureExists(texture))
                return;
            GL.BindTexture(TextureTarget.Texture2D, texture.ID);

            GL.Enable(EnableCap.Blend);
            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);


            GL.Begin(BeginMode.Quads);

            GL.TexCoord2(0f + begin * texture.WidthRatio, 0f);
            GL.Vertex3(rect.X + begin * rect.W, rect.Y, rect.Z + CGraphics.ZOffset);

            GL.TexCoord2(0f + begin * texture.WidthRatio, texture.HeightRatio);
            GL.Vertex3(rect.X + begin * rect.W, rect.Y + rect.H, rect.Z + CGraphics.ZOffset);

            GL.TexCoord2(texture.WidthRatio * end, texture.HeightRatio);
            GL.Vertex3(rect.X + end * rect.W, rect.Y + rect.H, rect.Z + CGraphics.ZOffset);

            GL.TexCoord2(texture.WidthRatio * end, 0f);
            GL.Vertex3(rect.X + end * rect.W, rect.Y, rect.Z + CGraphics.ZOffset);

            GL.End();


            GL.Disable(EnableCap.Blend);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void DrawTextureReflection(CTexture texture, SRectF rect, SColorF color, SRectF bounds, float space, float height)
        {
            if (!_TextureExists(texture))
                return;

            if (Math.Abs(rect.W) < float.Epsilon || Math.Abs(rect.H) < float.Epsilon || Math.Abs(bounds.H) < float.Epsilon || Math.Abs(bounds.W) < float.Epsilon ||
                Math.Abs(color.A) < float.Epsilon || height <= float.Epsilon)
                return;

            if (bounds.X > rect.X + rect.W || bounds.X + bounds.W < rect.X)
                return;

            if (bounds.Y > rect.Y + rect.H || bounds.Y + bounds.H < rect.Y)
                return;

            if (height > bounds.H)
                height = bounds.H;

            GL.BindTexture(TextureTarget.Texture2D, texture.ID);

            float x1 = (bounds.X - rect.X) / rect.W * texture.WidthRatio;
            float x2 = (bounds.X + bounds.W - rect.X) / rect.W * texture.WidthRatio;
            float y1 = (bounds.Y - rect.Y + rect.H - height) / rect.H * texture.HeightRatio;
            float y2 = (bounds.Y + bounds.H - rect.Y) / rect.H * texture.HeightRatio;

            if (x1 < 0)
                x1 = 0f;

            if (x2 > texture.WidthRatio)
                x2 = texture.WidthRatio;

            if (y1 < 0)
                y1 = 0f;

            if (y2 > texture.HeightRatio)
                y2 = texture.HeightRatio;


            float rx1 = rect.X;
            float rx2 = rect.X + rect.W;
            float ry1 = rect.Y + rect.H + space;
            float ry2 = rect.Y + rect.H + space + height;

            if (rx1 < bounds.X)
                rx1 = bounds.X;

            if (rx2 > bounds.X + bounds.W)
                rx2 = bounds.X + bounds.W;

            if (ry1 < bounds.Y + space)
                ry1 = bounds.Y + space;

            if (ry2 > bounds.Y + bounds.H + space + height)
                ry2 = bounds.Y + bounds.H + space + height;

            GL.Enable(EnableCap.Blend);

            GL.MatrixMode(MatrixMode.Texture);
            GL.PushMatrix();

            if (Math.Abs(rect.Rotation) > float.Epsilon)
            {
                GL.Translate(0.5f, 0.5f, 0);
                GL.Rotate(-rect.Rotation, 0f, 0f, 1f);
                GL.Translate(-0.5f, -0.5f, 0);
            }


            GL.Begin(BeginMode.Quads);

            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);
            GL.TexCoord2(x2, y2);
            GL.Vertex3(rx2, ry1, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, 0f);
            GL.TexCoord2(x2, y1);
            GL.Vertex3(rx2, ry2, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, 0f);
            GL.TexCoord2(x1, y1);
            GL.Vertex3(rx1, ry2, rect.Z + CGraphics.ZOffset);

            GL.Color4(color.R, color.G, color.B, color.A * CGraphics.GlobalAlpha);
            GL.TexCoord2(x1, y2);
            GL.Vertex3(rx1, ry1, rect.Z + CGraphics.ZOffset);

            GL.End();


            GL.PopMatrix();

            GL.Disable(EnableCap.Blend);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        #endregion drawing

        public int GetTextureCount()
        {
            return _Textures.Count;
        }

        private void _CheckQueue()
        {
            lock (_MutexTexture)
            {
                while (_Queue.Count > 0)
                {
                    STextureQueue q = _Queue[0];
                    _Queue.RemoveAt(0);
                    CTexture texture;
                    if (!_Textures.TryGetValue(q.Index, out texture))
                        continue;

                    if (q.Width == texture.OrigSize.Width && q.Height == texture.OrigSize.Height)
                        _CreateTexture(texture, q.Width, q.Height, q.Data);
                    else
                        CLog.LogError("Wrong texture size in queue!");
                }
            }
        }
        #endregion Textures

        #endregion implementation
    }
}