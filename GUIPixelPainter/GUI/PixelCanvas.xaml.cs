﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GUIPixelPainter.GUI
{
    /// <summary>
    /// Interaction logic for PixelCanvas.xaml
    /// </summary>
    public partial class PixelCanvas : UserControl
    {
        enum Tools
        {
            MOVE,
            BRUSH,
            HISTORYBRUSH
        }

        class Pixel
        {
            public Color c = new Color();
            public int x = 0;
            public int y = 0;
        }

        private ScaleTransform scale = new ScaleTransform();
        private TranslateTransform translate = new TranslateTransform();
        private Point mouseDownPoint = new Point();
        private bool shiftDirectionDeterm = false;
        private bool shiftDirHor = false;
        private bool diagDirectionDeterm = false;
        private bool diagDirAsc = false;
        private bool loading = false;
        private bool tracking = false;
        private double overlayOpacity = 0.5;

        private Tools tool = Tools.MOVE;
        private int brushSize = 5;
        private Color selectedColor = Color.FromArgb(0, 1, 2, 3);
        private int scalingPower = 0;

        private Dictionary<int, Border> nameLabels = new Dictionary<int, Border>();
        private Dictionary<int, long> userPlaceTime = new Dictionary<int, long>();
        private long lastUpdateTime = -1;

        private WriteableBitmap bitmap;
        private System.Drawing.Bitmap revertState;
        private int canvasId = -1;
        private bool running = false;

        private bool firstLoad = true;
        private Action firstLoadAction;

        private DropShadowEffect textShadow = new DropShadowEffect();

        private List<Pixel> manualTask = new List<Pixel>();


        private GUIHelper helper;
        public GUIHelper Helper { get { return helper; } set { helper = value; } }
        public GUIDataExchange DataExchange { get; set; }

        public PixelCanvas()
        {
            InitializeComponent();

            TransformGroup group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            MainCanvas.RenderTransform = group;

            textShadow.Color = System.Windows.Media.Color.FromRgb(255, 255, 255);
            textShadow.Direction = 320;
            textShadow.ShadowDepth = 0.5;
            textShadow.Opacity = 0.5;
            textShadow.BlurRadius = 0;
            textShadow.Freeze(); //memory leak possible fix

            OnToolClick(moveTool, null);
            SetNameLabelDisplay(false);
            ChangeBrushSize(0);
        }

        public void Run()
        {
            CreatePalette();
            running = true;
            ReloadCanvas(canvasId);
        }

        public void SetOnFirstLoad(Action action)
        {
            this.firstLoadAction = action;
        }

        public void ReloadCanvas(int id)
        {
            if (loading)
                return;
            if (!running)
            {
                canvasId = id;
                return;
            }
            Thread loadThread = new Thread(() =>
            {
                loading = true;
                Dispatcher.Invoke(() =>
                {
                    HideOverlay(true);
                    RemoveNameLabers();
                    OnResetPosition(null, null);
                    MainImageBorder.Visibility = Visibility.Hidden;
                    loadingSign.Visibility = Visibility.Visible;
                    bitmap = null;
                    DataExchange.PushLoadingState(true);
                    loadingFailSign.Visibility = Visibility.Collapsed;
                });

                canvasId = id;
                bool success = false;
                Logger.Info("Loading canvas {0} in PixelCanvas", id);

                System.Net.WebResponse response = null;
                System.IO.Stream responseStream = null;
                try
                {
                    System.Net.WebRequest request = System.Net.WebRequest.Create("https://pixelplace.io/canvas/" + id.ToString() + ".png");
                    response = request.GetResponse();
                    responseStream = response.GetResponseStream();
                    using (var loadedBitmap = new System.Drawing.Bitmap(responseStream))
                    {
                        System.Drawing.Bitmap white = new System.Drawing.Bitmap(loadedBitmap.Width, loadedBitmap.Height);
                        using (System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(white))
                        {
                            gr.Clear(System.Drawing.Color.White);
                            gr.DrawImageUnscaled(loadedBitmap, new System.Drawing.Point(0, 0));
                        }
                        loadedBitmap.Dispose();
                        Dispatcher.Invoke(() => bitmap = new WriteableBitmap(Helper.Convert(white)));
                        Dispatcher.Invoke(() => MainImage.Source = bitmap);
                    }
                    Dispatcher.Invoke(() => CreatePalette());
                    success = true;
                }
                catch (System.Net.WebException)
                {
                    Logger.Warning("Could not load canvas in pixelcanvas");
                    responseStream?.Dispose();
                }

                if (!success)
                {
                    Dispatcher.Invoke(() =>
                    {
                        bitmap = new WriteableBitmap(Helper.Convert(new System.Drawing.Bitmap(100, 100)));
                        loadingFailSign.Visibility = Visibility.Visible;
                    });
                }

                loading = false;
                Dispatcher.Invoke(() =>
                {
                    OnSaveRevertStateClick(null, null);
                    DataExchange.PushLoadingState(false);
                    MainImageBorder.Visibility = Visibility.Visible;
                    loadingSign.Visibility = Visibility.Hidden;
                    HideOverlay(false);
                    if (firstLoad)
                    {
                        firstLoad = false;
                        if (firstLoadAction != null)
                            firstLoadAction();
                    }
                });
            });
            loadThread.Name = "canvas loading thread";
            loadThread.IsBackground = true;
            loadThread.Start();
        }

        public System.Drawing.Color GetSelectedColor()
        {
            return System.Drawing.Color.FromArgb(selectedColor.A, selectedColor.R, selectedColor.G, selectedColor.B);
        }

        public void SaveBitmapToStream(Stream stream)
        {
            if (bitmap == null)
                return;

            int width = 0, height = 0, stride = 0;
            Dispatcher.Invoke(() =>
            {
                width = bitmap.PixelWidth;
                height = bitmap.PixelHeight;
                stride = width * ((bitmap.Format.BitsPerPixel + 7) / 8);
            });
            var bitmapData = new byte[height * stride];
            Dispatcher.Invoke(() =>
            {
                bitmap.CopyPixels(bitmapData, stride, 0);
            });
            stream.Write(bitmapData, 0, height * stride);
        }

        public System.Drawing.Size GetCanvasSize()
        {
            if (bitmap == null)
                return new System.Drawing.Size(2200, 1500);
            int w = 0, h = 0;
            Dispatcher.Invoke(() =>
            {
                w = bitmap.PixelWidth;
                h = bitmap.PixelHeight;
            });

            return new System.Drawing.Size(w, h);
        }

        private void HideOverlay(bool hide)
        {
            for (int i = MainCanvas.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement elem = MainCanvas.Children[i] as FrameworkElement;
                if ((string)elem.Tag == "taskOverlay")
                    elem.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
            }
        }

        public void OverlayTasks(List<GUITask> tasks)
        {
            for (int i = MainCanvas.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement elem = MainCanvas.Children[i] as FrameworkElement;
                if ((string)elem.Tag == "taskOverlay")
                    MainCanvas.Children.Remove(elem);
            }
            foreach (GUITask task in tasks)
            {
                Image image = new Image();
                image.Source = Helper.Convert(task.Dithering == true ? task.DitheredConvertedBitmap : task.ConvertedBitmap);
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                image.Opacity = overlayOpacity;
                Canvas.SetLeft(image, task.X);
                Canvas.SetTop(image, task.Y);
                image.Tag = "taskOverlay";
                if (loading)
                    image.Visibility = Visibility.Hidden;
                MainCanvas.Children.Add(image);
            }
        }

        public void SetTaskOverlayTranslucency(double value)
        {
            if (value > 1)
                value = 1;
            else if (value < 0)
                value = 0;
            overlayOpacity = 1 - value;
            for (int i = MainCanvas.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement elem = MainCanvas.Children[i] as FrameworkElement;
                if ((string)elem.Tag == "taskOverlay")
                    (elem as Image).Opacity = overlayOpacity;
            }
        }

        public void SetPixel(int x, int y, Color color, int userId)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                return;

            bitmap.SetPixel(x, y, color);
            UpdateNameLabel(x, y, color, userId);
        }

        public void SetNameLabelDisplay(bool visible)
        {
            tracking = visible;
            Visibility newVisib = visible ? Visibility.Visible : Visibility.Hidden;
            MainImage.Opacity = visible ? 0.5 : 1.0;
            foreach (KeyValuePair<int, long> userTime in userPlaceTime)
            {
                nameLabels[userTime.Key].Visibility = newVisib;
            }
        }

        private void ChangeBrushSize(int delta)
        {
            int newSize = brushSize + delta;
            if (newSize < 1)
                newSize = 1;
            else if (newSize > 25)
                newSize = 25;
            brushSize = newSize;
            brushHighlight.Width = newSize;
            brushHighlight.Height = newSize;
        }

        private void RemoveNameLabers()
        {
            List<int> toDelete = new List<int>();

            foreach (KeyValuePair<int, long> userTime in userPlaceTime)
            {
                toDelete.Add(userTime.Key);
            }

            foreach (int id in toDelete)
            {
                userPlaceTime.Remove(id);
                MainCanvas.Children.Remove(nameLabels[id]);
                nameLabels.Remove(id);
            }
        }

        private void UpdateNameLabel(int x, int y, Color c, int userId)
        {
            if (!userPlaceTime.ContainsKey(userId))
            {
                userPlaceTime.Add(userId, 0);
                nameLabels.Add(userId, AddNameLabel(userId, x, y, c));
            }

            var time = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            userPlaceTime[userId] = time;
            Canvas.SetLeft(nameLabels[userId], x + 1);
            Canvas.SetTop(nameLabels[userId], y + 1);
            (nameLabels[userId].Child as TextBlock).Text = Helper.GetUsernameById(userId);
            (nameLabels[userId].Child as TextBlock).Foreground = new SolidColorBrush(c);

            if (time - lastUpdateTime > 100)
            {
                lastUpdateTime = time;
                List<int> toDelete = new List<int>();

                foreach (KeyValuePair<int, long> userTime in userPlaceTime)
                {
                    if (time - userTime.Value > 1500)
                        toDelete.Add(userTime.Key);
                }

                foreach (int id in toDelete)
                {
                    userPlaceTime.Remove(id);
                    MainCanvas.Children.Remove(nameLabels[id]);
                    nameLabels.Remove(id);
                }
            }
        }

        private Border AddNameLabel(int name, int x, int y, Color color)
        {
            Border border = new Border()
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0.0),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Height = 24,
            };
            border.Visibility = tracking ? Visibility.Visible : Visibility.Hidden;
            TextBlock nameLabel = new TextBlock()
            {
                Text = Helper.GetUsernameById(name),
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            nameLabel.Effect = textShadow;
            nameLabel.FontSize = 20;
            border.Child = nameLabel;
            Canvas.SetLeft(border, x + 1);
            Canvas.SetTop(border, y + 1);
            MainCanvas.Children.Add(border);
            return border;
        }

        private void CreatePalette()
        {
            palettePanel.Children.Clear();

            int key = canvasId;
            if (!Helper.Palette.ContainsKey(key))
                key = 7;

            foreach (System.Drawing.Color c in Helper.Palette[key])
            {
                Rectangle rectangle = new Rectangle() { Fill = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)), Width = 25, Height = 25, Stroke = Brushes.Black, StrokeThickness = 1 };
                rectangle.MouseDown += OnSelectColor;
                palettePanel.Children.Add(rectangle);
            }

            OnSelectColor(palettePanel.Children[0], null);
        }

        private void OnSelectColor(object sender, EventArgs args)
        {
            Rectangle rect = sender as Rectangle;

            foreach (Rectangle c in palettePanel.Children)
            {
                c.StrokeThickness = 1;
            }

            rect.StrokeThickness = 3;
            selectedColor = (rect.Fill as SolidColorBrush).Color;
            DataExchange.UpdateSelectedColorFromGUI();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (loading)
                return;

            var mouseCoords = e.GetPosition(MainCanvas);
            coordsLabel.Text = String.Format("{0},{1}", (int)mouseCoords.X, (int)mouseCoords.Y);

            HandleMoveLock(e);
            if (!MainCanvas.IsMouseCaptured)
                return;
            HandleDraggingDrawing(e);
        }

        private void HandleDraggingDrawing(MouseEventArgs e)
        {
            //Alt-pick color
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                var coords = e.GetPosition(MainCanvas);
                if (coords.X >= 0 && coords.Y >= 0 && coords.X < bitmap.Width && coords.Y < bitmap.Height)
                {
                    Color color = bitmap.GetPixel((int)coords.X, (int)coords.Y);
                    foreach (Rectangle child in palettePanel.Children)
                    {
                        if ((child.Fill as SolidColorBrush).Color == color)
                        {
                            OnSelectColor(child, null);
                            break;
                        }
                    }
                }
            }
            //Move or draw
            else if (tool == Tools.MOVE || e.LeftButton != MouseButtonState.Pressed)
            {
                Point curPosition = e.GetPosition((UIElement)MainCanvas.Parent);
                translate.X = curPosition.X - mouseDownPoint.X;
                translate.Y = curPosition.Y - mouseDownPoint.Y;
            }
            else if (tool == Tools.BRUSH)
            {
                int drawx = (int)Canvas.GetLeft(brushHighlight);
                int drawy = (int)Canvas.GetTop(brushHighlight);
                for (int i = drawx; i < drawx + brushSize; i++)
                {
                    for (int j = drawy; j < drawy + brushSize; j++)
                    {
                        if (!(i >= 0 && j >= 0 && i < bitmap.Width && j < bitmap.Height))
                            continue;
                        var pixel = bitmap.GetPixel(i, j);
                        if (
                            pixel != selectedColor &&
                            !(pixel.A == 0 && selectedColor.R == 255 && selectedColor.G == 255 && selectedColor.B == 255)
                            )
                        {
                            DataExchange.CreateManualPixel(new GUIPixel(i, j, System.Drawing.Color.FromArgb(selectedColor.A, selectedColor.R, selectedColor.G, selectedColor.B)));
                        }
                    }
                }
            }
            else if (tool == Tools.HISTORYBRUSH)
            {
                int drawx = (int)Canvas.GetLeft(brushHighlight);
                int drawy = (int)Canvas.GetTop(brushHighlight);
                for (int i = drawx; i < drawx + brushSize; i++)
                {
                    for (int j = drawy; j < drawy + brushSize; j++)
                    {
                        if (!(i >= 0 && j >= 0 && i < bitmap.Width && j < bitmap.Height))
                            continue;
                        System.Drawing.Color revertPixel = revertState.GetPixel(i, j);
                        Color curPixel = bitmap.GetPixel(i, j);
                        if (curPixel.R == 204 && curPixel.G == 204 && curPixel.B == 204)
                            continue;
                        if (curPixel.R != revertPixel.R || curPixel.G != revertPixel.G || curPixel.B != revertPixel.B)
                        {
                            if (revertPixel.A == 0) //Have never been painted
                                DataExchange.CreateManualPixel(new GUIPixel(i, j, System.Drawing.Color.FromArgb(255, 255, 255, 255)));
                            else
                                DataExchange.CreateManualPixel(new GUIPixel(i, j, System.Drawing.Color.FromArgb(revertPixel.A, revertPixel.R, revertPixel.G, revertPixel.B)));
                        }
                    }
                }
            }
        }

        private void HandleMoveLock(MouseEventArgs e)
        {
            var mouseCoords = e.GetPosition(MainCanvas);

            bool shiftpressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool zpressed = Keyboard.IsKeyDown(Key.Z);
            bool butpressed = e.LeftButton == MouseButtonState.Pressed;

            if ((shiftpressed || zpressed) && butpressed && tool != Tools.MOVE)
            {
                if (shiftpressed)
                {
                    Point curPosition = e.GetPosition((UIElement)MainCanvas.Parent);
                    curPosition.X -= translate.X;
                    curPosition.Y -= translate.Y;

                    int dx = (int)((mouseDownPoint.X - curPosition.X) / scale.ScaleX);
                    int dy = (int)((mouseDownPoint.Y - curPosition.Y) / scale.ScaleY);

                    if (!shiftDirectionDeterm)
                    {
                        if (Math.Abs(dx) > Math.Abs(dy))
                        {
                            shiftDirHor = true;
                            shiftDirectionDeterm = true;
                        }
                        else if (Math.Abs(dx) < Math.Abs(dy))
                        {
                            shiftDirHor = false;
                            shiftDirectionDeterm = true;
                        }
                        //unable to determine of cursor has yet to move more than a pixel
                    }

                    if (shiftDirectionDeterm)
                    {
                        if (shiftDirHor)
                        {
                            Canvas.SetLeft(brushHighlight, Math.Floor(mouseCoords.X - brushSize / 2));
                            Canvas.SetLeft(pixelHighlight, Math.Floor(mouseCoords.X));
                        }
                        else
                        {
                            Canvas.SetTop(brushHighlight, Math.Floor(mouseCoords.Y - brushSize / 2));
                            Canvas.SetTop(pixelHighlight, Math.Floor(mouseCoords.Y));
                        }
                    }
                }
                else if (zpressed)
                {
                    Point curPosition = e.GetPosition((UIElement)MainCanvas.Parent);
                    curPosition.X -= translate.X;
                    curPosition.Y -= translate.Y;

                    int dx = (int)((mouseDownPoint.X - curPosition.X) / scale.ScaleX);
                    int dy = (int)((mouseDownPoint.Y - curPosition.Y) / scale.ScaleY);

                    if (!diagDirectionDeterm)
                    {
                        if (dx < 0 && dy < 0 || dx > 0 && dy > 0)
                        {
                            diagDirectionDeterm = true;
                            diagDirAsc = true;
                        }
                        else if (dx > 0 && dy < 0 || dx < 0 && dy > 0)
                        {
                            diagDirectionDeterm = true;
                            diagDirAsc = false;
                        }
                        //unable to determine of cursor has yet to move more than a pixel diagonally
                    }

                    if (diagDirectionDeterm)
                    {
                        if (diagDirAsc)
                        {
                            double mdpx = (int)(mouseDownPoint.X / scale.ScaleX);
                            double mdpy = (int)(mouseDownPoint.Y / scale.ScaleY);
                            double cpx = (int)(curPosition.X / scale.ScaleX);
                            double cpy = (int)(curPosition.Y / scale.ScaleY);

                            double xD = (cpx + cpy - mdpy + mdpx) / 2;
                            double yD = (xD - mdpx + mdpy);

                            int x, y;
                            RoundDiag(mdpx, mdpy, xD, yD, out x, out y);

                            Canvas.SetLeft(brushHighlight, x - brushSize / 2);
                            Canvas.SetTop(brushHighlight, y - brushSize / 2);

                            Canvas.SetLeft(pixelHighlight, x);
                            Canvas.SetTop(pixelHighlight, y);
                        }
                        else
                        {
                            double mdpx = (int)(mouseDownPoint.X / scale.ScaleX);
                            double mdpy = (int)(mouseDownPoint.Y / scale.ScaleY);
                            double cpx = (int)(curPosition.X / scale.ScaleX);
                            double cpy = (int)(curPosition.Y / scale.ScaleY);

                            double xD = (mdpx + mdpy - cpy + cpx) / 2;
                            double yD = (xD - cpx + cpy);

                            int x, y;
                            RoundDiag(mdpx, mdpy, xD, yD, out x, out y);

                            Canvas.SetLeft(brushHighlight, x - brushSize / 2);
                            Canvas.SetTop(brushHighlight, y - brushSize / 2);

                            Canvas.SetLeft(pixelHighlight, x);
                            Canvas.SetTop(pixelHighlight, y);
                        }
                    }
                }
                return;
            }

            shiftDirectionDeterm = false;
            diagDirectionDeterm = false;

            Canvas.SetLeft(brushHighlight, Math.Floor(mouseCoords.X - brushSize / 2));
            Canvas.SetTop(brushHighlight, Math.Floor(mouseCoords.Y - brushSize / 2));

            Canvas.SetLeft(pixelHighlight, Math.Floor(mouseCoords.X));
            Canvas.SetTop(pixelHighlight, Math.Floor(mouseCoords.Y));
        }

        private void RoundDiag(double mdpx, double mdpy, double x, double y, out int newX, out int newY)
        {
            double dx = x - mdpx;
            double dy = y - mdpy;

            if (dx > 0 && dy > 0)
            {
                newX = (int)Math.Floor(x);
                newY = (int)Math.Floor(y);
            }
            else if (dx < 0 && dy > 0)
            {
                newX = (int)Math.Ceiling(x);
                newY = (int)Math.Floor(y);
            }
            else if (dx > 0 && dy < 0)
            {
                newX = (int)Math.Floor(x);
                newY = (int)Math.Ceiling(y);
            }
            else
            {
                newX = (int)Math.Ceiling(x);
                newY = (int)Math.Ceiling(y);
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (loading)
                return;

            Keyboard.Focus(MainCanvas);

            mouseDownPoint = e.GetPosition((UIElement)MainCanvas.Parent);
            mouseDownPoint.X -= translate.X;
            mouseDownPoint.Y -= translate.Y;
            MainCanvas.CaptureMouse();
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (loading)
                return;
            MainCanvas.ReleaseMouseCapture();
        }

        private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (loading)
                return;

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && (tool == Tools.BRUSH || tool == Tools.HISTORYBRUSH))
            {
                ChangeBrushSize(e.Delta / 120);
                return;
            }

            Point curPosition = e.GetPosition((UIElement)MainCanvas.Parent);
            curPosition.X -= translate.X;
            curPosition.Y -= translate.Y;

            scalingPower += e.Delta / 120;

            if (scalingPower < -6)
                scalingPower = -6;
            else if (scalingPower > 15)
                scalingPower = 15;

            double oldScale = scale.ScaleX;
            double newScale = Math.Pow(1.4, scalingPower);

            scale.ScaleX = newScale;
            scale.ScaleY = newScale;

            if (scalingPower < 0)
                RenderOptions.SetBitmapScalingMode(MainImage, BitmapScalingMode.HighQuality);
            else
                RenderOptions.SetBitmapScalingMode(MainImage, BitmapScalingMode.NearestNeighbor);

            double factor = newScale / oldScale;
            Point newPos = new Point(curPosition.X * factor, curPosition.Y * factor);

            translate.X -= newPos.X - curPosition.X;
            translate.Y -= newPos.Y - curPosition.Y;

            mouseDownPoint.X *= factor;
            mouseDownPoint.Y *= factor;
        }

        private void MainCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            if (tool == Tools.BRUSH || tool == Tools.HISTORYBRUSH)
                ShowBrush(true);
            else
                ShowBrush(false);
        }

        private void MainCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            HideBrush();
        }

        private void OnResetPosition(object sender, MouseButtonEventArgs e)
        {
            translate.X = 0;
            translate.Y = 0;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            scalingPower = 0;
        }

        private void MainCanvas_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P)
            {
                var mousePos = Mouse.GetPosition(MainCanvas);
                DataExchange.PushTaskPosition((int)mousePos.X, (int)mousePos.Y);
            }
            else if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                if (tool == Tools.BRUSH || tool == Tools.HISTORYBRUSH)
                    ChangeBrushSize(1);
            }
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
            {
                if (tool == Tools.BRUSH || tool == Tools.HISTORYBRUSH)
                    ChangeBrushSize(-1);
            }
        }

        private void OnToolClick(object sender, MouseButtonEventArgs e)
        {
            moveTool.Background = Brushes.Black;
            brushTool.Background = Brushes.Black;
            historyBrushTool.Background = Brushes.Black;

            (sender as Border).Background = Brushes.Gray;

            if (sender == moveTool)
            {
                tool = Tools.MOVE;
                ShowBrush(false);
            }
            else if (sender == brushTool)
            {
                tool = Tools.BRUSH;
                ShowBrush(true);
            }
            else if (sender == historyBrushTool)
            {
                tool = Tools.HISTORYBRUSH;
                ShowBrush(true);
            }
        }

        private void ShowBrush(bool big)
        {
            if (big)
            {
                pixelHighlight.Visibility = Visibility.Hidden;
                brushHighlight.Visibility = Visibility.Visible;
            }
            else
            {
                pixelHighlight.Visibility = Visibility.Visible;
                brushHighlight.Visibility = Visibility.Hidden;
            }
        }

        private void HideBrush()
        {
            pixelHighlight.Visibility = Visibility.Hidden;
            brushHighlight.Visibility = Visibility.Hidden;
        }

        private void OnSaveMouseUp(object sender, MouseButtonEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            var time = DateTime.Now;
            saveFileDialog.FileName = String.Format("pixeplace.io-{0}-{1}-{2:00}-{3:00}-{4:00}-{5:00}-{6:00}.png", canvasId, time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second);
            saveFileDialog.Filter = "Image|*.png";
            if (saveFileDialog.ShowDialog() != true)
                return;

            using (var file = File.Create(saveFileDialog.FileName))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(file);
            }
        }

        private void OnSaveRevertStateClick(object sender, MouseButtonEventArgs e)
        {
            if (loading)
                return;

            revertState?.Dispose();

            using (var stream = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                revertState = new System.Drawing.Bitmap(stream);
            }
        }

        private void OnLoadRevertStateClick(object sender, MouseButtonEventArgs e)
        {
            if (loading)
                return;

            OpenFileDialog loadFileDialog = new OpenFileDialog();
            loadFileDialog.Filter = "Image|*.png";
            if (loadFileDialog.ShowDialog() != true)
                return;

            System.Drawing.Bitmap loaded = new System.Drawing.Bitmap(loadFileDialog.FileName);
            if (loaded.Width != bitmap.Width || loaded.Height != bitmap.Height)
            {
                MessageBox.Show("Selected image is not a valid canvas state");
                return;
            }

            revertState?.Dispose();
            revertState = loaded;
        }
    }
}
