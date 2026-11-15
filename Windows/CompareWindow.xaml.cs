using SampleViewer.Models;
using SampleViewer.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
            public CompareItem Item = null!;
            public TextBlock? PagesText;
            public CancellationTokenSource? LoadCts;
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

        // 枚数送り：同じ耐久・同じパターンの画像一覧を返す／移動先の画像の読み込みを依頼する
        public Func<CompareItem, IReadOnlyList<CompareItem>>? SeriesProvider { get; init; }
        public Action<IReadOnlyList<CompareItem>>? LoadRequested { get; init; }

        // クエリ方式など取得に時間がかかる場合は、キーの押しっぱなしで要求を連発しない
        public bool IsRemoteLoading { get; set; }

        private readonly Dictionary<(string DurabilityId, string Pattern), IReadOnlyList<CompareItem>> _seriesCache = new();


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

            // ウィンドウの大きさが変わったら、画像が最も大きくなる並びに直す
            TilesGrid.SizeChanged += (_, __) => UpdateGridLayout();
            Closed += (_, __) =>
            {
                _adjustTimer.Stop();
                _adjustCts?.Cancel();
                _cts.Cancel();
                _cts.Dispose();
                _loadGate.Dispose();
            };

        }
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        // ドラッグ＆ドロップで画像を追加する。表示中のタイルは残し、全体の枚数に合わせて並べ直す
        public void AddImages(IEnumerable<string> paths)
        {
            if (paths == null) return;

            var files = paths
                        .Where(p => ImageExtensions.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()))
                        .Select(p => new { Path = p, Name = SafeFileName(p) })
                        .Where(x => !_ids.Contains(x.Name))
                        .GroupBy(x => x.Name).Select(g => g.First())
                        .Take(Math.Max(0, MaxDirectImages - _tiles.Count))
                        .ToList();

            if (files.Count == 0)
            {
                StatusText.Text = "追加できる画像がありません（同じ名前の画像は追加済みです）";
                return;
            }

            SetGridLayout(_tiles.Count + files.Count);

            foreach (var f in files)
            {
                _ids.Add(f.Name);
                var tile = CreateTile(new CompareItem { DisplayName = f.Name });
                tile.Id = f.Name;
                TilesGrid.Children.Add(tile.Border);
                _tiles.Add(tile);
                _ = LoadTileAsync(tile, f.Path, _cts.Token);
            }

            UpdateGridLayout();
            StatusText.Text = $"{files.Count}枚を追加しました";
        }

        // 並べ方を決めるための寸法
        private const double DefaultImageAspect = 2550.0 / 3300;   // 画像の幅÷高さ（Letter 縦）
        private const double TileMargin = 8;                       // タイルの外側の余白（左右・上下の合計）
        private const double LabelHeightFull = 40;                 // ラベル2行
        private const double LabelHeightCompact = 24;              // ラベル1行
        private const double CompactLabelTileWidth = 230;          // これより狭いタイルはラベルを1行にする

        private int _layoutCount;

        private void SetGridLayout(int n)
        {
            _layoutCount = n;
            UpdateGridLayout();
        }

        // 画像が最も大きく表示される行数と列数にする
        // ウィンドウの大きさ、画像の縦横比、ラベルの高さから計算するので、枚数が多くても縦長の画像に合った並びになる
        private void UpdateGridLayout()
        {
            int n = _layoutCount;

            if (n <= 0)
                return;

            // まだ画面に出ていないときは、既定のウィンドウの大きさで見積もる
            double width = TilesGrid.ActualWidth > 0 ? TilesGrid.ActualWidth : 1570;
            double height = TilesGrid.ActualHeight > 0 ? TilesGrid.ActualHeight : 880;
            bool showLabels = CB_ShowLabels.IsChecked == true;

            var loaded = _tiles.Where(x => x.Ready && x.ImgH > 0).ToList();
            double aspect = loaded.Count > 0 ? loaded.Average(x => (double)x.ImgW / x.ImgH) : DefaultImageAspect;

            var candidates = new List<(int Columns, double ImageHeight, int Empty)>();

            for (int columns = 1; columns <= n; columns++)
            {
                int rows = (int)Math.Ceiling((double)n / columns);
                double tileWidth = width / columns;
                double labelHeight = !showLabels ? 0
                    : tileWidth < CompactLabelTileWidth ? LabelHeightCompact
                    : LabelHeightFull;

                // このタイルに収まる画像の高さ（幅で決まる場合と高さで決まる場合の小さい方）
                double imageHeight = Math.Min(
                    (tileWidth - TileMargin) / aspect,
                    height / rows - TileMargin - labelHeight);

                candidates.Add((columns, imageHeight, rows * columns - n));
            }

            // 画像が最も大きくなる並び。大きさがほぼ同じ（8%以内）なら、空きマスが少ない並びを優先する
            double largest = candidates.Max(x => x.ImageHeight);

            int bestColumns = candidates
                .Where(x => x.ImageHeight >= largest * 0.92)
                .OrderBy(x => x.Empty)
                .ThenByDescending(x => x.ImageHeight)
                .First()
                .Columns;

            TilesGrid.Columns = bestColumns;
            TilesGrid.Rows = (int)Math.Ceiling((double)n / bestColumns);

            // 狭いタイルはラベルを1行にして、画像の場所を確保する（2行目の内容はラベルのツールチップで見られる）
            bool compact = width / bestColumns < CompactLabelTileWidth;

            foreach (var tile in _tiles)
                SetLabelCompact(tile, compact);
        }

        private static void SetLabelCompact(Tile tile, bool compact)
        {
            if (tile.Label is Panel panel && panel.Children.Count > 1)
                panel.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
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

            var label = CreateTileLabel(item, offsetText, out var pagesText);
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
                Item = item,
                PagesText = pagesText,
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
        private FrameworkElement CreateTileLabel(CompareItem item, TextBlock offsetText, out TextBlock? pagesText)
        {
            pagesText = null;

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

            if (item.Badge != "")
            {
                // 限度見本は橙、比べる対象は青の札
                firstRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(item.IsLimitSample ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x3B, 0x6F, 0xE0)),
                    CornerRadius = new CornerRadius(3),
                    Height = 16,
                    Padding = new Thickness(5, 0, 5, 0),
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = item.Badge,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
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
                pagesText = Text($"{item.Pages:#,0}枚", Brushes.White);
                pagesText.FontWeight = FontWeights.SemiBold;
                firstRow.Children.Add(pagesText);
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

                // 枚数送りで画像が切り替わるときに、調整前の画像が一瞬見えないようにする
                var adjustment = CurrentAdjustment();
                BitmapSource? adjusted = adjustment.IsActive
                    ? await Task.Run(() => ImageAdjuster.Apply(bi, adjustment, ct), ct)
                    : null;
                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    tile.ImgW = Math.Max(1, bi.PixelWidth);
                    tile.ImgH = Math.Max(1, bi.PixelHeight);

                    tile.OriginalSource = bi; // ★追加
                    tile.AdjustedSource = adjusted;
                    tile.AppliedAdjustment = adjustment;
                    tile.Img.Source = bi;

                    tile.Ready = true;
                    tile.LoadingText.Visibility = Visibility.Collapsed;
                    tile.ErrorText.Visibility = Visibility.Collapsed;
                    tile.Img.Visibility = Visibility.Visible;

                    UpdateTransformFor(tile.Root);

                    // 読み込み中に調整が変わっていたら、今の調整を適用し直す
                    if (tile.AppliedAdjustment != CurrentAdjustment())
                        ScheduleAdjustment();
                });

            }
            catch (OperationCanceledException)
            {
                // ...
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ShowTileError(tile, ex.Message));
            }
        }


        // 読み込めなかったタイル。ラベルと違う古い画像を残さないよう消す
        private void ShowTileError(Tile tile, string message)
        {
            tile.Ready = false;
            tile.OriginalSource = null;
            tile.AdjustedSource = null;
            tile.Img.Visibility = Visibility.Collapsed;
            tile.ErrorText.Text = message;
            tile.ErrorText.Visibility = Visibility.Visible;
            tile.LoadingText.Visibility = Visibility.Collapsed;
        }

        public void ShowImageError(string key, string message)
        {
            var tile = FindTile(key);

            if (tile == null)
                return;

            tile.LoadCts?.Cancel();
            ShowTileError(tile, $"{message}\n{tile.Item.DisplayName}");
        }

        #region 枚数送り・コピー

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                CopyView();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Left && e.Key != Key.Right)
                return;

            // スライダーやリストでは矢印キーを本来の操作に使う
            if (Keyboard.FocusedElement is Slider or ComboBox)
                return;

            e.Handled = true;

            if (e.IsRepeat && IsRemoteLoading)
                return;

            StepPages(e.Key == Key.Right ? 1 : -1);
        }

        private void PreviousPages_Click(object sender, RoutedEventArgs e) => StepPages(-1);

        private void NextPages_Click(object sender, RoutedEventArgs e) => StepPages(1);

        // 全タイルを、同じ耐久・同じパターンの前後の枚数へ移動する（ズーム・位置・ズレは維持）
        private void StepPages(int direction)
        {
            if (SeriesProvider == null || LoadRequested == null)
                return;

            var moved = new List<CompareItem>();

            foreach (var tile in _tiles)
            {
                var item = tile.Item;

                if (string.IsNullOrEmpty(item.DurabilityId) || !item.Pages.HasValue)
                    continue;

                var series = GetSeries(item);
                var next = direction > 0
                    ? series.FirstOrDefault(x => x.Pages > item.Pages)
                    : series.LastOrDefault(x => x.Pages < item.Pages);

                if (next == null)
                    continue;

                tile.Item = new CompareItem
                {
                    Key = tile.Key,
                    DisplayName = next.DisplayName,
                    SourcePath = next.SourcePath,
                    DurabilityId = next.DurabilityId,
                    Pages = next.Pages,
                    Pattern = next.Pattern,
                    Env = next.Env,
                    Model = next.Model,
                    Purpose = next.Purpose
                };

                if (tile.PagesText != null)
                    tile.PagesText.Text = $"{next.Pages:#,0}枚";

                tile.Label.ToolTip = next.DisplayName;
                tile.ErrorText.Visibility = Visibility.Collapsed;
                tile.LoadingText.Text = "読み込み中...";
                tile.LoadingText.Visibility = Visibility.Visible;

                // 取得に時間がかかる方式では、ラベルと違う古い画像を見せない
                if (IsRemoteLoading)
                    tile.Img.Visibility = Visibility.Collapsed;

                moved.Add(tile.Item);
            }

            StatusText.Text = moved.Count == 0
                ? (direction > 0 ? "これより後の枚数はありません" : "これより前の枚数はありません")
                : "";

            if (moved.Count > 0)
                LoadRequested(moved);
        }

        private IReadOnlyList<CompareItem> GetSeries(CompareItem item)
        {
            var key = (item.DurabilityId, item.Pattern);

            if (!_seriesCache.TryGetValue(key, out var series))
            {
                series = SeriesProvider!(item);
                _seriesCache[key] = series;
            }

            return series;
        }

        private void CopyView_Click(object sender, RoutedEventArgs e) => CopyView();

        // ラベルを含む今の表示を、貼り付け先で背景が透けないよう不透明な画像にする
        private BitmapSource? CreateViewImage()
        {
            double width = TilesGrid.ActualWidth;
            double height = TilesGrid.ActualHeight;

            if (width <= 0 || height <= 0)
                return null;

            var dpi = VisualTreeHelper.GetDpi(this);
            var bounds = new Rect(0, 0, width, height);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, bounds);
                dc.DrawRectangle(new VisualBrush(TilesGrid), null, bounds);
            }

            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi.DpiScaleX),
                (int)Math.Ceiling(height * dpi.DpiScaleY),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }

        private void CopyView()
        {
            var image = CreateViewImage();

            if (image == null)
                return;

            try
            {
                Clipboard.SetImage(image);
                StatusText.Text = "表示中の画面をコピーしました";
            }
            catch (ExternalException)
            {
                // 他のアプリがクリップボードを使用中
                StatusText.Text = "コピーできませんでした。もう一度お試しください";
            }
        }

        #endregion

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

            // ラベルの有無で画像に使える高さが変わるので、並びを見直す
            UpdateGridLayout();
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

                    // 計算中に枚数送りで画像が替わっていたら、古い結果は使わない
                    if (!ReferenceEquals(tile.OriginalSource, source))
                        continue;

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

            SetGridLayout(list.Count);

            foreach (var item in list)
            {
                if (_ids.Contains(item.Key))
                    continue;

                var tile = CreateTile(item);

                tile.Key = item.Key;

                tile.LoadingText.Text =
                    string.IsNullOrEmpty(item.DisplayName)
                    ? "読み込み中…"
                    : "読み込み中…\n" + item.DisplayName;

                TilesGrid.Children.Add(tile.Border);

                _tiles.Add(tile);

                _ids.Add(item.Key);
            }

            UpdateGridLayout();
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

            // 枚数送りで続けて要求されたら、前の読み込みは取り消す（遅れて届いても上書きしない）
            tile.LoadCts?.Cancel();
            var loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            tile.LoadCts = loadCts;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await LoadTileAsync(
                        tile,
                        localImagePath,
                        loadCts.Token);

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
