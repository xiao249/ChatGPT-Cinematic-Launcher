using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

// Visuals only: application launch and readiness are owned by Launcher.
internal sealed class CinematicSplash : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties {
        public uint Flags;
        public NativeRect Destination, Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out NativeSize size);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private DispatcherTimer alignmentTimer;
    private DispatcherTimer handoffTimer;
    private IntPtr thumbnail;
    private double previousFrame;
    public bool IsToolWindow { get; private set; }
    public bool WindowAligned { get; private set; }
    public object AlignmentRecord { get; private set; }
    public double MaximumFrameGapMilliseconds { get; private set; }
    public bool NativeHandoff { get; private set; }
    public int HandoffFrameCount { get; private set; }
    private sealed class RainDrop
    {
        public Line Shape;
        public TranslateTransform Move;
        public double X, Y, Speed;
    }

    private sealed class Spark
    {
        public Ellipse Shape;
        public TranslateTransform Move;
        public double X, Y, Speed, Phase;
    }

    private sealed class Entrance
    {
        public UIElement Element;
        public TranslateTransform Move;
        public double Delay;
    }

    private readonly Stopwatch clock = new Stopwatch();
    private readonly DispatcherTimer timer;
    private readonly List<RainDrop> rain = new List<RainDrop>();
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly List<Entrance> entrances = new List<Entrance>();
    private readonly Canvas scene, web;
    private readonly ScaleTransform zoom;
    private readonly TranslateTransform camera, intro, sweepMove, redMove;
    private readonly TranslateTransform scan, border, redShift, blueShift;
    private readonly Rectangle light, rim, sweep, redSweep, revealTop, revealBottom;
    private readonly TextBlock redEcho, blueEcho, status, chapter;
    private readonly Path horizon;
    private bool disposed, closing, skipRaised;

    public Window Window { get; private set; }
    public event Action SkipRequested;
    public double ElapsedSeconds { get { return clock.Elapsed.TotalSeconds; } }

    public CinematicSplash(string root)
    {
        string assets = System.IO.Path.Combine(root, "assets");
        using (FileStream input = File.OpenRead(System.IO.Path.Combine(assets, "Splash.xaml")))
            Window = (Window)XamlReader.Load(input);

        Window.Title = "ChatGPT · Cinematic";
        Window.KeyDown += OnKeyDown;
        Window.MouseLeftButtonDown += OnMouseDown;
        Window.ContentRendered += OnContentRendered;
        Window.SourceInitialized += OnSourceInitialized;
        Window.Closed += OnClosed;

        scene = Find<Canvas>("Scene");
        web = Find<Canvas>("WebOverlay");
        zoom = Find<ScaleTransform>("CameraZoom");
        camera = Find<TranslateTransform>("HeroTransform");
        intro = Find<TranslateTransform>("IntroTransform");
        scan = Find<TranslateTransform>("ScanTransform");
        border = Find<TranslateTransform>("BorderScanTransform");
        light = Find<Rectangle>("AtmosphereLight");
        rim = Find<Rectangle>("BlueRim");
        sweep = Find<Rectangle>("EnergySweep");
        sweepMove = Find<TranslateTransform>("SweepTransform");
        revealTop = Find<Rectangle>("RevealTop");
        revealBottom = Find<Rectangle>("RevealBottom");
        redEcho = Find<TextBlock>("TitleRedEcho");
        blueEcho = Find<TextBlock>("TitleBlueEcho");
        redShift = Find<TranslateTransform>("TitleRedShift");
        blueShift = Find<TranslateTransform>("TitleBlueShift");
        status = Find<TextBlock>("StatusText");
        status.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        status.FontSize = 10;
        status.Width = 730;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Text = "正在打开 ChatGPT…";

        BitmapImage art = new BitmapImage();
        art.BeginInit();
        art.CacheOption = BitmapCacheOption.OnLoad;
        art.DecodePixelWidth = 1600;
        art.UriSource = new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(assets, "artwork-v2.jpg")));
        art.EndInit();
        art.Freeze();
        Find<Image>("HeroArtwork").Source = art;

        Button skip = Find<Button>("SkipButton");
        skip.Content = "跳过  ↗";
        skip.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        skip.ToolTip = "Esc · 跳过动画，继续打开 ChatGPT";
        skip.Click += OnSkip;

        PrepareEntrances();
        PrepareParticles();

        redMove = new TranslateTransform(-420, 0);
        redSweep = new Rectangle();
        redSweep.Width = 160;
        redSweep.Height = 740;
        redSweep.IsHitTestVisible = false;
        Canvas.SetTop(redSweep, -100);
        TransformGroup redTransforms = new TransformGroup();
        redTransforms.Children.Add(new SkewTransform(-20, 0));
        redTransforms.Children.Add(redMove);
        redSweep.RenderTransform = redTransforms;
        redSweep.Fill = Gradient(new Point(0, 0), new Point(1, 0),
            "#00ED4654", "#88ED4654", "#00ED4654");
        // Light stays behind typography and never replaces the artwork.
        scene.Children.Insert(scene.Children.IndexOf(Find<Canvas>("SparkCanvas")), redSweep);

        horizon = new Path();
        horizon.Data = Geometry.Parse("M 734,491 L 908,491 L 908,463");
        horizon.Stroke = Brush("#95C9E9");
        horizon.StrokeThickness = 0.65;
        horizon.StrokeDashArray = new DoubleCollection(new double[] { 420, 420 });
        horizon.IsHitTestVisible = false;
        scene.Children.Add(horizon);

        chapter = new TextBlock();
        chapter.FontFamily = new FontFamily("Segoe UI");
        chapter.FontSize = 8;
        chapter.FontWeight = FontWeights.SemiBold;
        chapter.Foreground = Brush("#99AFC5");
        chapter.Width = 225;
        chapter.TextAlignment = TextAlignment.Right;
        chapter.IsHitTestVisible = false;
        Canvas.SetLeft(chapter, 677);
        Canvas.SetTop(chapter, 500);
        scene.Children.Add(chapter);

        timer = new DispatcherTimer(DispatcherPriority.Render, Window.Dispatcher);
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += OnTick;
        SetFrame(0);
    }

    private T Find<T>(string name) where T : class
    {
        T value = Window.FindName(name) as T;
        if (value == null) throw new InvalidDataException("启动动画缺少画面元素：" + name);
        return value;
    }

    private static SolidColorBrush Brush(string value)
    {
        SolidColorBrush result = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        result.Freeze();
        return result;
    }

    private static LinearGradientBrush Gradient(Point start, Point end, string a, string b, string c)
    {
        LinearGradientBrush result = new LinearGradientBrush();
        result.StartPoint = start;
        result.EndPoint = end;
        result.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(a), 0));
        result.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(b), 0.5));
        result.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), 1));
        result.Freeze();
        return result;
    }

    private void PrepareEntrances()
    {
        foreach (UIElement item in scene.Children)
        {
            Canvas titleCanvas = item as Canvas;
            if (titleCanvas == null || titleCanvas.RenderTransform != intro) continue;
            foreach (UIElement piece in titleCanvas.Children)
            {
                TextBlock text = piece as TextBlock;
                if (text == redEcho || text == blueEcho) continue;
                double y = Canvas.GetTop(piece);
                double delay = y < 100 ? 0.12 : (y < 240 ? 0.38 : (y < 300 ? 0.68 : 0.96));
                TranslateTransform move = new TranslateTransform();
                piece.RenderTransform = move;
                entrances.Add(new Entrance { Element = piece, Move = move, Delay = delay });
                if (text != null && text.Text == "Your next chapter starts here.")
                {
                    text.Text = "Your next chapter starts here.";
                    text.Foreground = Brush("#B2BECD");
                }
            }
        }
    }

    private void PrepareParticles()
    {
        Random random = new Random(413);
        Canvas rainCanvas = Find<Canvas>("RainCanvas");
        Canvas sparkCanvas = Find<Canvas>("SparkCanvas");
        rainCanvas.Children.Clear();
        sparkCanvas.Children.Clear();
        for (int i = 0; i < 96; i++)
        {
            double depth = random.NextDouble();
            double length = 8 + 29 * depth;
            TranslateTransform move = new TranslateTransform();
            Line drop = new Line();
            drop.X2 = -length * 0.27;
            drop.Y2 = length;
            drop.Stroke = Brush("#BED8EC");
            drop.StrokeThickness = 0.4 + depth * 0.65;
            drop.Opacity = 0.075 + depth * 0.19;
            drop.RenderTransform = move;
            drop.IsHitTestVisible = false;
            rainCanvas.Children.Add(drop);
            rain.Add(new RainDrop { Shape = drop, Move = move,
                X = random.NextDouble() * 1100, Y = random.NextDouble() * 650,
                Speed = 215 + 290 * depth });
        }
        for (int i = 0; i < 21; i++)
        {
            TranslateTransform move = new TranslateTransform();
            Ellipse shape = new Ellipse();
            shape.Width = shape.Height = 1.1 + random.NextDouble() * 2;
            shape.Fill = Brush(i % 3 == 0 ? "#9ECFED" : "#F4A18C");
            shape.RenderTransform = move;
            shape.IsHitTestVisible = false;
            sparkCanvas.Children.Add(shape);
            sparks.Add(new Spark { Shape = shape, Move = move,
                X = 380 + random.NextDouble() * 590, Y = random.NextDouble() * 620,
                Speed = 12 + random.NextDouble() * 24, Phase = random.NextDouble() * Math.PI * 2 });
        }
    }

    private static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }
    private static double Ease(double value) { double p = Clamp(value); return p * p * (3 - 2 * p); }
    private static double Out(double value) { return 1 - Math.Pow(1 - Clamp(value), 3); }
    private static double Mod(double value, double divisor) { return (value % divisor + divisor) % divisor; }

    private void SetFrame(double seconds)
    {
        double t = Math.Max(0, seconds);
        double story = Math.Min(8, t);
        // After the eight-second sequence, hold the complete scene. Keep rain alive
        // while Launcher waits for the real desktop window; never fade to black.
        scene.Opacity = 0.25 + 0.75 * Ease(t / 0.34);
        double reveal = 1 - Out(story / 1.25);
        double push = Ease((story - 1.1) / 6.9);
        zoom.ScaleX = zoom.ScaleY = 1.016 + 0.076 * reveal + 0.039 * push;
        camera.X = 10 * reveal + 2.5 * Math.Sin(story * 0.42);
        camera.Y = -2.2 * push;
        intro.X = -10 * (1 - Out(story / 1.0));
        intro.Y = 7 * (1 - Out(story / 1.0));
        foreach (Entrance entry in entrances)
        {
            double p = Out((story - entry.Delay) / 0.65);
            entry.Element.Opacity = p;
            entry.Move.X = -13 * (1 - p);
            entry.Move.Y = 7 * (1 - p);
        }

        double echo = Math.Sin(Math.PI * Clamp((story - 0.4) / 0.62));
        if (story >= 5.55) echo = 0.6 * Math.Sin(Math.PI * Clamp((story - 5.55) / 0.6));
        redShift.X = -5.2 * echo;
        blueShift.X = 4.3 * echo;
        redEcho.Opacity = 0.33 * echo;
        blueEcho.Opacity = 0.26 * echo;

        light.Opacity = 0.022 + 0.015 * Math.Sin(t * 1.2);
        rim.Opacity = 0.13 + 0.035 * Math.Sin(t * 0.82);
        double sweepProgress = Clamp((story - 0.75) / 2.7);
        if (story >= 5.0) sweepProgress = Clamp((story - 5.0) / 2.2);
        sweepMove.X = -360 + 1600 * sweepProgress;
        sweep.Opacity = 0.23 * Math.Sin(Math.PI * sweepProgress);
        double redProgress = Clamp((story - 3.0) / 1.7);
        redMove.X = 1080 - 1550 * redProgress;
        redSweep.Opacity = 0.18 * Math.Sin(Math.PI * redProgress);

        int webIndex = 0;
        foreach (UIElement child in web.Children)
        {
            Path path = child as Path;
            if (path == null) continue;
            double trace = Out((story - 0.38 - webIndex * 0.08) / 1.55);
            path.StrokeDashOffset = 1400 * (1 - trace);
            webIndex++;
        }
        web.Opacity = (0.31 - 0.17 * Ease((story - 2.2) / 1.7)) * Ease(story / 0.4);
        revealTop.Height = revealBottom.Height = 48 * Math.Pow(1 - Clamp(story / 1.2), 2);
        scan.X = -130 + Mod(t * 190, 430);
        border.X = -200 + 1400 * Mod(t * 0.18, 1);

        horizon.StrokeDashOffset = 420 * (1 - Out((story - 1.1) / 2.4));
        horizon.Opacity = 0.16 + 0.13 * Ease((story - 1) / 2);
        chapter.Opacity = 0.7 * Ease((story - 1.4) / 0.8);
        chapter.Text = story < 2.8 ? "01  /  NIGHTFALL" : story < 5.4 ? "02  /  THE CITY IS AWAKE" : "03  /  YOUR NEXT CHAPTER";

        foreach (RainDrop drop in rain)
        {
            drop.Move.X = Mod(drop.X - t * drop.Speed * 0.27, 1100) - 70;
            drop.Move.Y = Mod(drop.Y + t * drop.Speed, 650) - 55;
        }
        foreach (Spark spark in sparks)
        {
            spark.Move.X = spark.X + 7 * Math.Sin(t * 0.62 + spark.Phase);
            spark.Move.Y = Mod(spark.Y - t * spark.Speed, 620) - 40;
            spark.Shape.Opacity = (0.1 + 0.22 * Math.Pow(Math.Sin(t * 0.8 + spark.Phase), 2)) * Ease(t / 0.4);
        }
    }

    private void OnContentRendered(object sender, EventArgs e)
    {
        if (disposed || closing || clock.IsRunning) return;
        clock.Start();
        timer.Start();
        Window.Focus();
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        // Only our overlay is a tool window. Chromium can keep painting the
        // official client underneath it, without altering the client or flags.
        IntPtr handle = new WindowInteropHelper(Window).Handle;
        long style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x80));
        IsToolWindow = (GetWindowLongPtr(handle, -20).ToInt64() & 0x80) != 0;
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (closing || disposed) return;
        double now = ElapsedSeconds;
        if (previousFrame > 0.5) MaximumFrameGapMilliseconds = Math.Max(MaximumFrameGapMilliseconds, (now - previousFrame) * 1000);
        previousFrame = now;
        SetFrame(now);
    }

    public bool AlignToWindow(IntPtr target, Action completed)
    {
        if (disposed || closing || !Window.Dispatcher.CheckAccess()) return false;
        IntPtr own = new WindowInteropHelper(Window).Handle;
        NativeRect start, end;
        if (!GetWindowRect(own, out start) || !GetWindowRect(target, out end) || end.Right - end.Left < 320 || end.Bottom - end.Top < 200 || end.Right - end.Left > 32768 || end.Bottom - end.Top > 32768) return false;
        if (alignmentTimer != null) alignmentTimer.Stop();
        WindowAligned = false;
        Stopwatch movement = Stopwatch.StartNew();
        alignmentTimer = new DispatcherTimer(DispatcherPriority.Render, Window.Dispatcher);
        alignmentTimer.Interval = TimeSpan.FromMilliseconds(16);
        alignmentTimer.Tick += delegate {
            if (disposed || closing) { alignmentTimer.Stop(); return; }
            double p = Ease(movement.Elapsed.TotalSeconds / 0.36);
            int left = (int)Math.Round(start.Left + (end.Left - start.Left) * p);
            int top = (int)Math.Round(start.Top + (end.Top - start.Top) * p);
            int right = (int)Math.Round(start.Right + (end.Right - start.Right) * p);
            int bottom = (int)Math.Round(start.Bottom + (end.Bottom - start.Bottom) * p);
            SetWindowPos(own, IntPtr.Zero, left, top, right - left, bottom - top, 0x214);
            if (p < 1) return;
            alignmentTimer.Stop();
            NativeRect current, final;
            if (GetWindowRect(target, out final)) {
                SetWindowPos(own, IntPtr.Zero, final.Left, final.Top, final.Right - final.Left, final.Bottom - final.Top, 0x214);
                WindowAligned = GetWindowRect(own, out current) && Math.Abs(current.Left - final.Left) <= 1 && Math.Abs(current.Top - final.Top) <= 1 && Math.Abs(current.Right - final.Right) <= 1 && Math.Abs(current.Bottom - final.Bottom) <= 1;
                AlignmentRecord = new Dictionary<string, object> { { "target", new int[] { final.Left, final.Top, final.Right, final.Bottom } }, { "overlay", new int[] { current.Left, current.Top, current.Right, current.Bottom } }, { "matched", WindowAligned } };
                if (WindowAligned) PrepareNativeHandoff(target);
            }
            completed();
        };
        alignmentTimer.Start();
        return true;
    }
    private void OnSkip(object sender, RoutedEventArgs e) { RequestSkip(); }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        RequestSkip();
    }

    private void RequestSkip()
    {
        if (skipRaised || disposed || closing) return;
        skipRaised = true;
        Action handler = SkipRequested;
        if (handler != null) handler();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || closing || disposed) return;
        DependencyObject item = e.OriginalSource as DependencyObject;
        while (item != null && item != Window)
        {
            if (item is Button) return;
            item = VisualTreeHelper.GetParent(item);
        }
        try { Window.DragMove(); } catch (InvalidOperationException) { }
    }

    public void SetStatus(string message)
    {
        if (disposed || closing) return;
        if (!Window.Dispatcher.CheckAccess())
        {
            Window.Dispatcher.BeginInvoke(new Action<string>(SetStatus), message);
            return;
        }
        status.Text = String.IsNullOrWhiteSpace(message) ? "正在打开 ChatGPT…" : message;
    }

    private void ReleaseThumbnail()
    {
        if (thumbnail == IntPtr.Zero) return;
        DwmUnregisterThumbnail(thumbnail);
        thumbnail = IntPtr.Zero;
    }

    private void PrepareNativeHandoff(IntPtr source)
    {
        ReleaseThumbnail();
        IntPtr own = new WindowInteropHelper(Window).Handle;
        NativeRect destination;
        NativeSize size;
        if (!GetClientRect(own, out destination) || DwmRegisterThumbnail(own, source, out thumbnail) != 0) { ReleaseThumbnail(); return; }
        if (DwmQueryThumbnailSourceSize(thumbnail, out size) != 0 || size.Width <= 0 || size.Height <= 0) { ReleaseThumbnail(); return; }
        var properties = new ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = destination, Opacity = 0, Visible = true, ClientOnly = false };
        if (DwmUpdateThumbnailProperties(thumbnail, ref properties) != 0) ReleaseThumbnail();
    }

    public void FadeAndClose()
    {
        if (disposed || closing) return;
        if (!Window.Dispatcher.CheckAccess())
        {
            Window.Dispatcher.BeginInvoke(new Action(FadeAndClose));
            return;
        }
        closing = true;
        timer.Stop();
        if (!Window.IsVisible) { Window.Close(); return; }
        // Keep the ready desktop window underneath this short visual handoff.
        TimeSpan exit = TimeSpan.FromMilliseconds(650);
        status.Text = "进入 ChatGPT";
        CubicEase accelerate = new CubicEase { EasingMode = EasingMode.EaseIn };
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(zoom.ScaleX, zoom.ScaleX + 0.035, exit) { EasingFunction = accelerate });
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(zoom.ScaleY, zoom.ScaleY + 0.035, exit) { EasingFunction = accelerate });
        sweep.Opacity = 0.36;
        redSweep.Opacity = 0.27;
        sweepMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-360, 1430, exit) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
        redMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(1180, -420, exit) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
        web.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(web.Opacity, 0.36, TimeSpan.FromMilliseconds(130)) { AutoReverse = true });
        foreach (UIElement element in web.Children)
        {
            Path strand = element as Path;
            if (strand != null) strand.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(strand.StrokeDashOffset, strand.StrokeDashOffset - 240, exit));
        }
        redEcho.Opacity = 0.28;
        blueEcho.Opacity = 0.24;
        redShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(redShift.X, -7, exit));
        blueShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(blueShift.X, 7, exit));
        // Composite the live, ready chat window over the scene in this same
        // HWND. Fading an opaque WPF HWND itself can produce a black flash.
        NativeHandoff = thumbnail != IntPtr.Zero;
        if (!NativeHandoff) { Window.Close(); return; }
        Stopwatch transition = Stopwatch.StartNew();
        handoffTimer = new DispatcherTimer(DispatcherPriority.Render, Window.Dispatcher);
        handoffTimer.Interval = TimeSpan.FromMilliseconds(16);
        handoffTimer.Tick += delegate {
            if (disposed) { handoffTimer.Stop(); return; }
            double p = Ease((transition.Elapsed.TotalSeconds - 0.05) / 0.6);
            var properties = new ThumbnailProperties { Flags = 4 | 8, Opacity = (byte)Math.Round(255 * p), Visible = true };
            if (DwmUpdateThumbnailProperties(thumbnail, ref properties) != 0) { handoffTimer.Stop(); Window.Close(); return; }
            HandoffFrameCount++;
            if (transition.Elapsed.TotalSeconds < 0.71) return;
            handoffTimer.Stop();
            DwmFlush();
            Window.Close();
        };
        handoffTimer.Start();
    }

    public void RenderFrame(double seconds, string outputPath)
    {
        if (disposed) throw new ObjectDisposedException("CinematicSplash");
        if (!Window.Dispatcher.CheckAccess()) throw new InvalidOperationException("画面预览需要 STA 窗口线程。");
        if (Double.IsNaN(seconds) || Double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException("seconds");
        SetFrame(seconds);
        FrameworkElement visual = (FrameworkElement)Window.Content;
        visual.Measure(new Size(960, 540));
        visual.Arrange(new Rect(0, 0, 960, 540));
        visual.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outputPath));
        Directory.CreateDirectory(parent);
        using (FileStream output = File.Create(outputPath)) encoder.Save(output);
        if (clock.IsRunning && !closing) SetFrame(ElapsedSeconds);
    }

    private void OnClosed(object sender, EventArgs e)
    {
        timer.Stop();
        if (alignmentTimer != null) alignmentTimer.Stop();
        if (handoffTimer != null) handoffTimer.Stop();
        ReleaseThumbnail();
        clock.Stop();
    }

    public void Dispose()
    {
        if (disposed) return;
        timer.Stop();
        if (alignmentTimer != null) alignmentTimer.Stop();
        if (handoffTimer != null) handoffTimer.Stop();
        ReleaseThumbnail();
        timer.Tick -= OnTick;
        clock.Stop();
        Window.ContentRendered -= OnContentRendered;
        Window.SourceInitialized -= OnSourceInitialized;
        Window.KeyDown -= OnKeyDown;
        Window.MouseLeftButtonDown -= OnMouseDown;
        Window.Closed -= OnClosed;
        Find<Button>("SkipButton").Click -= OnSkip;
        if (Window.IsVisible) Window.Close();
        disposed = true;
    }
}
