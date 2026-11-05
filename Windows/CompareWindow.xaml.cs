using SampleViewer.Models;
using SampleViewer.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


namespace SampleViewer.Windows
{
    /// <summary>
    /// CompareWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class CompareWindow : Window
    {

        private Tile? FindTile(string key)
        {
            return _tiles.FirstOrDefault(x => x.Key == key);
        }

        private class Tile
        {
            public Border Border = null!;
            public Grid Root = null!;
            public Image Img = null!;
            public TextBlock LoadingText = null!;
            public TextBlock ErrorText = null!;
            public string Id ="";
            public string Key ="";
            public FrameworkElement Label = null!;
            public TextBlock OffsetText = null!;
            public int ImgW; // ピクセル数（CroppedBitmap の座標と揃える）
            public int ImgH;
            public BitmapSource? OriginalSource; // ★追加
            public BitmapSource? AdjustedSource; // 画質調整後（調整なしは null）
            public ImageAdjustment AppliedAdjustment = NoAdjustment;
            public bool Ready;

            // Ctrl＋ドラッグで付けた、共通の表示中心からのズレ（画像相対座標）
            public double OffsetX;
            public double OffsetY;
        }

        private static readonly ImageAdjustment NoAdjustment = new(0, 255, false, 16);
        private const int MaxDirectImages = 100;


        private double _zoom = 1.0;         // 1.0 = Fit基準
        private double _centerX = 0.5;      // 0..1（画像相対座標）
        private double _centerY = 0.5;
        private const double MaxZoom = 100.0;

        private readonly List<Tile> _tiles = new();
        private readonly HashSet<string> _ids = new(); // 重複除外（ファイル名）
        private Point? _dragStart;
        private Grid? _dragContainer;

        private readonly SemaphoreSlim _loadGate = new(4);
        private readonly CancellationTokenSource _cts = new();
        private readonly DispatcherTimer _adjustTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private CancellationTokenSource? _adjustCts;
        public CompareWindow()
        {

            InitializeComponent();
            _adjustTimer.Tick += async (_, __) =>
            {
                _adjustTimer.Stop();
                await ApplyAdjustmentAsync();
            };
            Loaded += (_, __) => UpdateAllTransforms();
            SizeChanged += (_, __) => UpdateAllTransforms();
            Closed += (_, __) =>
            {
                _adjustTimer.Stop();
                _adjustCts?.Cancel();
                _cts.Cancel();
                _cts.Dispose();
                _loadGate.Dispose();
            };

        }
        public void AddImages(IEnumerable<string> paths)
        {
            Debug.Print($"paths={paths.Count()}");
            Debug.Print($"tiles={_tiles.Count}");

            if (paths == null) return;
            var n = paths.Count();
            Debug.Print(n.ToString());

            if (n == 1)
            { TilesGrid.Rows = 1; TilesGrid.Columns = 1; }
            if (n >= 2 && n <= 5) { TilesGrid.Rows = 1; TilesGrid.Columns = n; }
            if (n >= 6 && n <= 10) { TilesGrid.Rows = 2; TilesGrid.Columns = 5; }
            if (n >= 11 && n <= 20) { TilesGrid.Rows = 5; TilesGrid.Columns = 5; }
            if (n >= 21 && n <100) { TilesGrid.Rows = (int)(Math.Sqrt(n)+1); TilesGrid.Columns = (int)(Math.Sqrt(n)+1); }
            if (n >=101) { TilesGrid.Rows = 10; TilesGrid.Columns = 10; }
            var files = paths
                        .Select(p => new { Path = p, Name = SafeFileName(p) })
                        .Where(x => !_ids.Contains(x.Name))
                        .GroupBy(x => x.Name).Select(g => g.First())
                        .ToList();

            int capacity = Math.Max(0, TilesGrid.Rows*TilesGrid.Columns - _tiles.Count);
            foreach (var f in files.Take(capacity))
            {
                _ids.Add(f.Name);
                var tile = CreateTile(new CompareItem { DisplayName = f.Name });
                tile.Id = f.Name;
                TilesGrid.Children.Add(tile.Border);
                _tiles.Add(tile);
                _ = LoadTileAsync(tile, f.Path, _cts.Token);
            }
        }




        // フル読み込み（OnLoad）＋キャンセル対応
        private Task<BitmapImage> LoadBitmapFromFileAsync(
    string path,
    CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                Exception? lastEx = null;

                for (int retry = 0; retry < 5; retry++)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var bi = new BitmapImage();

                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions =
                            BitmapCreateOptions.PreservePixelFormat;
                        bi.UriSource =
                            new Uri(path, UriKind.Absolute);
                        bi.EndInit();

                        bi.Freeze();

                        return bi;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Thread.Sleep(300);
                    }
                }

                throw lastEx ??
                    new System.IO.IOException(
                        $"画像読込失敗 : {path}");
            }, ct);
        }

        private static string SafeFileName(string path)
        {
            try { return System.IO.Path.GetFileName(path) ?? path; }
            catch { return path; }
        }

        // Drag&Dropで追加
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddImages(paths);
            }
        }

        // タイルUIを生成（コードビハインドのみ）
        private Tile CreateTile(CompareItem item)
        {
            var border = new Border { Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)) };

            // 余白部分でもドラッグできるよう透明の背景を付ける
            var root = new Grid { ClipToBounds = true, Background = Brushes.Transparent };

            var offsetText = new TextBlock
            {
                Foreground = Brushes.Orange,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var label = CreateTileLabel(item, offsetText);
            label.Visibility = CB_ShowLabels.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DockPanel.SetDock(label, Dock.Top);

            var tileContent = new DockPanel();
            tileContent.Children.Add(label);
            tileContent.Children.Add(root);
            border.Child = tileContent;

            // 端を超えて動かしたときに余白を出せるよう、Canvas 上に切り抜いた部分だけを配置する
            var img = new Image
            {
                Stretch = Stretch.Fill,
                Visibility = Visibility.Collapsed,
            };
            var imageLayer = new Canvas();
            imageLayer.Children.Add(img);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var loading = new TextBlock
            {
                Text = "Loading...",
                Foreground = Brushes.LightGray,
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Visible
            };

            var error = new TextBlock
            {
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            root.Children.Add(imageLayer);
            root.Children.Add(loading);
            root.Children.Add(error);

            // イベント（各タイル共通だが、同期ビューを更新）
            root.PreviewMouseWheel += Tile_PreviewMouseWheel;
            root.PreviewMouseLeftButtonDown += Tile_PreviewMouseLeftButtonDown;
            root.PreviewMouseMove += Tile_PreviewMouseMove;
            root.PreviewMouseLeftButtonUp += Tile_PreviewMouseLeftButtonUp;
            root.SizeChanged += (_, __) => UpdateTransformFor(root);

            var tile = new Tile
            {
                Border = border,
                Root = root,
                Img = img,
                LoadingText = loading,
                ErrorText = error,
                Label = label,
                OffsetText = offsetText,
                Ready = false
            };

            var resetOffset = new MenuItem { Header = "この画像のズレをリセット" };
            resetOffset.Click += (_, __) =>
            {
                ResetOffset(tile);
                UpdateTransformFor(root);
            };
            root.ContextMenu = new ContextMenu();
            root.ContextMenu.Items.Add(resetOffset);

            return tile;
        }

        // 画像の上に出す情報ラベル：耐久ID・環境・機種・枚数・パターン／ズレ・評価目的
        // 高さが違うと画像の表示倍率が揃わず位置合わせできないので、全タイルで同じ2行分に固定する（幅が足りない分は切る）
        private FrameworkElement CreateTileLabel(CompareItem item, TextBlock offsetText)
        {
            var light = new SolidColorBrush(Color.FromRgb(229, 231, 235));
            var muted = new SolidColorBrush(Color.FromRgb(156, 163, 175));

            TextBlock Text(string text, Brush brush, double left = 8) => new()
            {
                Text = text,
                Foreground = brush,
                FontSize = 11,
                Margin = new Thickness(left, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var firstRow = new StackPanel { Orientation = Orientation.Horizontal, Height = 18, ClipToBounds = true };
            var secondRow = new DockPanel { Height = 16, ClipToBounds = true };
            DockPanel.SetDock(offsetText, Dock.Left);
            secondRow.Children.Add(offsetText);

            var panel = new StackPanel { Margin = new Thickness(6, 3, 6, 3), DataContext = item, ToolTip = item.DisplayName };
            panel.Children.Add(firstRow);
            panel.Children.Add(secondRow);

            if (string.IsNullOrEmpty(item.DurabilityId))
            {
                // ドラッグ＆ドロップで追加した画像はファイル名だけ
                firstRow.Children.Add(Text(item.DisplayName, light, 0));
                return panel;
            }

            var id = Text(item.DurabilityId, Brushes.White, 0);
            id.FontWeight = FontWeights.SemiBold;
            firstRow.Children.Add(id);

            var envBadge = new Border
            {
                Style = (Style)FindResource("EnvBadge"),
                Height = 16,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = item.Env, FontSize = 10, FontWeight = FontWeights.Bold }
            };
            firstRow.Children.Add(envBadge);

            firstRow.Children.Add(Text(item.Model, light));

            if (item.Pages.HasValue)
            {
                var pages = Text($"{item.Pages:#,0}枚", Brushes.White);
                pages.FontWeight = FontWeights.SemiBold;
                firstRow.Children.Add(pages);
            }

            firstRow.Children.Add(Text(item.Pattern, light));

            var purpose = Text(item.Purpose, muted, 0);
            purpose.TextTrimming = TextTrimming.CharacterEllipsis;
            secondRow.Children.Add(purpose);

            return panel;
        }

        // 非同期読み込み（UIフリーズ回避）
        // まずは「切れないで読み込む」ことだけに集中した最小版
        // タイルへの反映（キャンセル対応）
        private async Task LoadTileAsync(Tile tile, string path, CancellationToken ct = default)
        {
            try
            {
                var bi = await LoadBitmapFromFileAsync(path, ct);
                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    tile.ImgW = Math.Max(1, bi.PixelWidth);
                    tile.ImgH = Math.Max(1, bi.PixelHeight);

                    tile.OriginalSource = bi; // ★追加
                    tile.Img.Source = bi;

                    tile.Ready = true;
                    tile.LoadingText.Visibility = Visibility.Collapsed;
                    tile.ErrorText.Visibility = Visibility.Collapsed;
                    tile.Img.Visibility = Visibility.Visible;

                    UpdateTransformFor(tile.Root);

                    // 調整中に読み込まれた画像にも同じ調整を適用する
                    if (CurrentAdjustment().IsActive)
                        ScheduleAdjustment();
                });

            }
            catch (OperationCanceledException)
            {
                // ...
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    tile.ErrorText.Text = ex.Message;
                    tile.ErrorText.Visibility = Visibility.Visible;
                    tile.LoadingText.Visibility = Visibility.Collapsed;
                });
            }
        }


        // パン・ズーム操作
        private void Tile_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var grid = (Grid)sender;
            var pos = e.GetPosition(grid);
            int dir = e.Delta > 0 ? 1 : -1;

            ZoomAt(grid, pos.X, pos.Y, dir);
            UpdateAllTransforms();
            e.Handled = true;
        }
        private void ClampCenterToVisible(Tile tile, double containerW, double containerH)
        {
            if (tile == null || tile.ImgW <= 0 || tile.ImgH <= 0 || containerW <= 0 || containerH <= 0) return;

            double fit = FitScale(containerW, containerH, tile.ImgW, tile.ImgH);
            double s = fit * _zoom;
            if (s <= 0) return;

            _centerX = ClampAxis(_centerX, containerW / (2.0 * s * tile.ImgW));
            _centerY = ClampAxis(_centerY, containerH / (2.0 * s * tile.ImgH));
        }

        // 画像の端より外が見えないように中心を収める（画像より表示領域が広ければ中央）
        private static double ClampAxis(double center, double half)
        {
            double min = Math.Max(0.0, half);
            double max = Math.Min(1.0, 1.0 - half);

            if (min > max)
                return 0.5;

            return Math.Max(min, Math.Min(max, center));
        }

        // このタイルの表示中心。ズレが無い軸は端で止め、ズレがある軸は位置合わせを保つため余白が出ても止めない
        private (double X, double Y) GetTileCenter(Tile tile, double containerW, double containerH)
        {
            double s = FitScale(containerW, containerH, tile.ImgW, tile.ImgH) * _zoom;

            double x = tile.OffsetX != 0
                ? _centerX + tile.OffsetX
                : ClampAxis(_centerX, containerW / (2.0 * s * tile.ImgW));

            double y = tile.OffsetY != 0
                ? _centerY + tile.OffsetY
                : ClampAxis(_centerY, containerH / (2.0 * s * tile.ImgH));

            return (x, y);
        }
        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragContainer = (Grid)sender;
            _dragStart = e.GetPosition(_dragContainer);
            Mouse.Capture(_dragContainer);
        }

        private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                var grid = _dragContainer ?? (Grid)sender;
                var pos = e.GetPosition(grid);
                var dx = pos.X - _dragStart.Value.X;
                var dy = pos.Y - _dragStart.Value.Y;
                Pan(grid, dx, dy);
                _dragStart = pos;
                UpdateAllTransforms();
            }
        }

        private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
            _dragContainer = null;
            Mouse.Capture(null);
        }

        // ビューポート計算（各タイルの画像サイズにあわせてFit）
        private static double FitScale(double containerW, double containerH, int imgW, int imgH)
        {
            if (imgW <= 0 || imgH <= 0 || containerW <= 0 || containerH <= 0) return 1.0;
            double sx = containerW / imgW;
            double sy = containerH / imgH;
            return Math.Min(sx, sy);
        }

        private void ZoomAt(Grid container, double mouseX, double mouseY, int direction)
        {
            // カーソル位置を基準にズーム（カーソル下の画像点を維持）
            var tile = _tiles.FirstOrDefault(t => t.Root == container && t.Ready);
            if (tile == null || tile.ImgW <= 0 || tile.ImgH <= 0) return;

            if (double.IsNaN(_centerX) || double.IsInfinity(_centerX)) _centerX = 0.5;
            if (double.IsNaN(_centerY) || double.IsInfinity(_centerY)) _centerY = 0.5;

            double cw = container.ActualWidth;
            double ch = container.ActualHeight;
            double fit = FitScale(cw, ch, tile.ImgW, tile.ImgH);
            double s = fit * _zoom;
            if (s <= 0) return;



            var (viewCenterX, viewCenterY) = GetTileCenter(tile, cw, ch);

            double offsetX = cw / 2.0 - viewCenterX * s * tile.ImgW;
            double offsetY = ch / 2.0 - viewCenterY * s * tile.ImgH;

            double xImg = (mouseX - offsetX) / s;
            double yImg = (mouseY - offsetY) / s;
            double nx = xImg / tile.ImgW;
            double ny = yImg / tile.ImgH;

            double factor = direction > 0 ? 1.2 : (1.0 / 1.2);
            double newZoom = Math.Max(1.0, Math.Min(MaxZoom, _zoom * factor));
            double s2 = fit * newZoom;

            double cx = (cw / 2.0 + nx * s2 * tile.ImgW - mouseX) / (s2 * tile.ImgW);
            double cy = (ch / 2.0 + ny * s2 * tile.ImgH - mouseY) / (s2 * tile.ImgH);

            _zoom = newZoom;
            _centerX = cx - tile.OffsetX;
            _centerY = cy - tile.OffsetY;

            ClampCenterToVisible(tile, cw, ch);

            ZoomText.Text = $"Zoom: {_zoom:F2}x";
        }

        private void Pan(Grid container, double dx, double dy)
        {
            var tile = _tiles.FirstOrDefault(t => t.Root == container && t.Ready);
            if (tile == null || tile.ImgW <= 0 || tile.ImgH <= 0) return;

            if (double.IsNaN(_centerX) || double.IsInfinity(_centerX)) _centerX = 0.5;
            if (double.IsNaN(_centerY) || double.IsInfinity(_centerY)) _centerY = 0.5;

            double cw = container.ActualWidth;
            double ch = container.ActualHeight;
            double fit = FitScale(cw, ch, tile.ImgW, tile.ImgH);
            double s = fit * _zoom;
            if (s <= 0) return;

            // Ctrl を押している間はこの画像のズレだけを動かす（離すとズレを保ったまま全体が動く）
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                tile.OffsetX = Math.Clamp(tile.OffsetX - dx / (s * tile.ImgW), -1.0, 1.0);
                tile.OffsetY = Math.Clamp(tile.OffsetY - dy / (s * tile.ImgH), -1.0, 1.0);
                UpdateOffsetText(tile);
                return;
            }

            double cx = _centerX - dx / (s * tile.ImgW);
            double cy = _centerY - dy / (s * tile.ImgH);
            _centerX = Clamp01(cx);
            _centerY = Clamp01(cy);
            ClampCenterToVisible(tile, cw, ch);
        }


        private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));

        // 各タイルのImage.RenderTransformを更新
        private void UpdateAllTransforms()
        {
            foreach (var t in _tiles) UpdateTransformFor(t.Root);
        }

        private void UpdateTransformFor(Grid container)
        {
            var tile = _tiles.FirstOrDefault(t => t.Root == container && t.Ready);
            if (tile == null || tile.ImgW <= 0 || tile.ImgH <= 0) return;

            double cw = container.ActualWidth;
            double ch = container.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            double fit = FitScale(cw, ch, tile.ImgW, tile.ImgH);
            double scale = fit * _zoom;
            if (scale <= 0) return;

            var (centerX, centerY) = GetTileCenter(tile, cw, ch);

            // 表示範囲（画像のピクセル座標）
            double viewX = centerX * tile.ImgW - cw / scale / 2.0;
            double viewY = centerY * tile.ImgH - ch / scale / 2.0;

            // 画像と重なる部分だけを切り抜く
            int x0 = Math.Max(0, (int)Math.Floor(viewX));
            int y0 = Math.Max(0, (int)Math.Floor(viewY));
            int x1 = Math.Min(tile.ImgW, (int)Math.Ceiling(viewX + cw / scale));
            int y1 = Math.Min(tile.ImgH, (int)Math.Ceiling(viewY + ch / scale));

            var src = tile.AdjustedSource ?? tile.OriginalSource;
            if (src == null || x1 <= x0 || y1 <= y0)
            {
                // ズレにより画像全体が表示範囲の外にある
                tile.Img.Visibility = Visibility.Collapsed;
                return;
            }

            tile.Img.Source = new CroppedBitmap(src, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            tile.Img.Width = (x1 - x0) * scale;
            tile.Img.Height = (y1 - y0) * scale;
            Canvas.SetLeft(tile.Img, (x0 - viewX) * scale);
            Canvas.SetTop(tile.Img, (y0 - viewY) * scale);
            tile.Img.Visibility = Visibility.Visible;
        }

        private static void UpdateOffsetText(Tile tile)
        {
            bool hasOffset = tile.OffsetX != 0 || tile.OffsetY != 0;

            tile.OffsetText.Text = hasOffset
                ? $"ズレ X:{tile.OffsetX * tile.ImgW:+0;-0;0} Y:{tile.OffsetY * tile.ImgH:+0;-0;0}px"
                : "";
            tile.OffsetText.Visibility = hasOffset ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ResetOffset(Tile tile)
        {
            tile.OffsetX = 0;
            tile.OffsetY = 0;
            UpdateOffsetText(tile);
        }


        // UIボタン
        private void Fit_Click(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            _centerX = 0.5;
            _centerY = 0.5;
            ZoomText.Text = $"Zoom: {_zoom:F2}x";
            UpdateAllTransforms();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _tiles.ForEach(ResetOffset);
            Fit_Click(sender, e);
        }

        private void ResetOffsets_Click(object sender, RoutedEventArgs e)
        {
            _tiles.ForEach(ResetOffset);
            UpdateAllTransforms();
        }

        private void ShowLabels_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            var visibility = CB_ShowLabels.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tile in _tiles)
                tile.Label.Visibility = visibility;
        }

        #region 画質調整

        private ImageAdjustment CurrentAdjustment()
        {
            return new ImageAdjustment(
                (int)SL_Black.Value,
                (int)SL_White.Value,
                CB_Highlight.IsChecked == true,
                (int)SL_Threshold.Value);
        }

        private void Adjust_Changed(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
                ScheduleAdjustment();
        }

        // スライダー操作中に毎回計算しないよう、止まってから適用する
        private void ScheduleAdjustment()
        {
            _adjustTimer.Stop();
            _adjustTimer.Start();
        }

        private void EmphasizeWhite_Click(object sender, RoutedEventArgs e)
        {
            SL_Black.Value = 200;
            SL_White.Value = 255;
        }

        private void ResetAdjust_Click(object sender, RoutedEventArgs e)
        {
            SL_Black.Value = NoAdjustment.BlackLevel;
            SL_White.Value = NoAdjustment.WhiteLevel;
            CB_Highlight.IsChecked = false;
        }

        // 全タイルに調整を適用する。新しい調整が始まったら古い処理は途中で打ち切る
        private async Task ApplyAdjustmentAsync()
        {
            _adjustCts?.Cancel();
            var cts = new CancellationTokenSource();
            _adjustCts = cts;

            var adjustment = CurrentAdjustment();

            foreach (var tile in _tiles.ToList())
            {
                var source = tile.OriginalSource;

                if (!tile.Ready || source == null || tile.AppliedAdjustment == adjustment)
                    continue;

                try
                {
                    var adjusted = adjustment.IsActive
                        ? await Task.Run(() => ImageAdjuster.Apply(source, adjustment, cts.Token), cts.Token)
                        : null;

                    if (cts.IsCancellationRequested)
                        return;

                    tile.AdjustedSource = adjusted;
                    tile.AppliedAdjustment = adjustment;
                    UpdateTransformFor(tile.Root);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    tile.ErrorText.Text = ex.Message;
                    tile.ErrorText.Visibility = Visibility.Visible;
                }
            }
        }

        #endregion

        public void CreatePlaceholders(IEnumerable<CompareItem> items)
        {
            if (items == null)
                return;

            var list = items.ToList();

            int n = list.Count;

            if (n == 1)
            {
                TilesGrid.Rows = 1;
                TilesGrid.Columns = 1;
            }
            else if (n >= 2 && n <= 5)
            {
                TilesGrid.Rows = 1;
                TilesGrid.Columns = n;
            }
            else if (n >= 6 && n <= 10)
            {
                TilesGrid.Rows = 2;
                TilesGrid.Columns = 5;
            }
            else if (n >= 11 && n <= 20)
            {
                TilesGrid.Rows = 5;
                TilesGrid.Columns = 5;
            }
            else
            {
                int size = (int)Math.Ceiling(Math.Sqrt(n));

                TilesGrid.Rows = size;
                TilesGrid.Columns = size;
            }

            foreach (var item in list)
            {
                if (_ids.Contains(item.Key))
                    continue;

                var tile = CreateTile(item);

                tile.Key = item.Key;

                tile.LoadingText.Text =
                    string.IsNullOrEmpty(item.DisplayName)
                    ? "Loading..."
                    : item.DisplayName;

                TilesGrid.Children.Add(tile.Border);

                _tiles.Add(tile);

                _ids.Add(item.Key);
            }
        }

        // 画像を直接読める場合（Box 等）：ラベル付きのタイルを作ってすぐ読み込む
        public void AddItems(IEnumerable<CompareItem> items)
        {
            var list = items
                .Where(x => !string.IsNullOrWhiteSpace(x.SourcePath))
                .Take(MaxDirectImages)
                .ToList();

            CreatePlaceholders(list);

            foreach (var item in list)
                _ = SetImageAsync(item.Key, item.SourcePath!);
        }

        public async Task SetImageAsync(string key,string localImagePath)
        {
            var tile = FindTile(key);

            if (tile == null)
                return;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await LoadTileAsync(
                        tile,
                        localImagePath,
                        _cts.Token);

                    return;
                }
                catch
                {
                    await Task.Delay(300);
                }
            }
        }
    }


}
