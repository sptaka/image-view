using SampleViewer.Models;
using SampleViewer.Repositories;
using SampleViewer.Services;
using SampleViewer.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;
using static SampleViewer.Models.UserSettings;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using static SampleViewer.Utils.VisualTreeUtils;


namespace SampleViewer.Windows
{
    public partial class MainWindow
    {
        #region フィールド・定数

        private readonly TrendGraphRepository _trendRepo = new();
        private string _selectedMeasurement = "Vb1";
        private long? _selectedImageId = null;
        private string? _selectedFeature = null;

        // 選択中の画像の測定（測定値の点の強調表示）とパターン（特徴量の候補）
        private long? _selectedMeasurementId = null;
        private string? _selectedPattern = null;

        // 右ペインのグラフ1枚あたりの最小の高さ（これ未満になる場合は縦スクロール）
        private const double MinTrendGraphHeight = 170;

        // 全グラフ共通の軸範囲（耐久間で比較しやすいように揃える）
        private (double MinX, double MaxX, double MinYLeft, double MaxYLeft, double MinYRight, double MaxYRight) _trendRange;

        // 描画領域の余白（左：測定値の目盛り、右：特徴量の目盛り、下：枚数の目盛り）
        private const double GraphLeftMargin = 45;
        private const double GraphRightMargin = 50;
        private const double GraphTopMargin = 8;
        private const double GraphBottomMargin = 20;
        private const string ViewBandTag = "ViewBand";

        // 耐久ID → 比較行で現在表示している枚数の範囲
        private readonly Dictionary<string, (double From, double To)> _visibleRanges = new();

        #endregion

        #region データ取得

        // グラフ対象の耐久：比較モードは表示中の行、通常モードは左リストの選択
        private List<TrendGraphItem> CreateTrendGraphItems()
        {
            if (IsCompareMode)
            {
                return GetDisplayDurabilityIds()
                    .Select(id =>
                    {
                        var target = DurabilityCompareTargets.FirstOrDefault(x => x.DurabilityId == id);
                        return new TrendGraphItem
                        {
                            DurabilityId = id,
                            SlotName = target?.SlotName ?? "閲覧",
                            Purpose = target?.EvaluationPurpose
                                ?? (LB_taikyu_ID.SelectedItem as DurabilityDisplayItem)?.EvaluationPurpose
                                ?? ""
                        };
                    })
                    .ToList();
            }

            return LB_taikyu_ID.SelectedItems
                .Cast<DurabilityDisplayItem>()
                .Select(x => new TrendGraphItem
                {
                    DurabilityId = x.Id,
                    Purpose = x.EvaluationPurpose
                })
                .ToList();
        }

        // 対象耐久のデータを取得し直してグラフを作り直す
        private void RefreshTrendGraph()
        {
            var graphs = CreateTrendGraphItems();

            if (graphs.Count == 0)
            {
                IC_TrendGraphs.ItemsSource = null;
                TB_TrendTarget.Text = "";
                return;
            }

            var ids = graphs.Select(x => x.DurabilityId).ToList();

            var points = _trendRepo.GetMeasurementTrend(ids, _selectedMeasurement);

            if (!string.IsNullOrWhiteSpace(_selectedFeature))
            {
                points.AddRange(_trendRepo.GetFeatureTrend(ids, _selectedFeature));
            }

            var developerChanges = _trendRepo.GetDeveloperChanges(ids);
            var infos = SearchService.GetDurabilityInfo(ids);

            foreach (var graph in graphs)
            {
                string prefix = graph.DurabilityId + ":";
                graph.Points = points.Where(p => p.SeriesName.StartsWith(prefix)).ToList();
                graph.DeveloperChanges = developerChanges.GetValueOrDefault(graph.DurabilityId) ?? new();
                (graph.Env, graph.Model) = infos.GetValueOrDefault(graph.DurabilityId);
                graph.Env ??= "";
                graph.Model ??= "";
            }

            _trendRange = CalculateTrendRange(points);

            TB_TrendTarget.Text = points.Count == 0
                ? $"{_selectedMeasurement} data not found"
                : $"Durability : {string.Join(", ", ids)}";

            IC_TrendGraphs.ItemsSource = graphs;
            UpdateTrendGraphHeight();
        }

        private static (double, double, double, double, double, double) CalculateTrendRange(List<TrendPoint> points)
        {
            var measurement = points.Where(x => !x.IsFeature).ToList();
            var feature = points.Where(x => x.IsFeature).ToList();

            double minX = points.Count > 0 ? points.Min(x => x.Pages) : 0;
            double maxX = points.Count > 0 ? points.Max(x => x.Pages) : 1;

            double minYLeft = measurement.Count > 0 ? measurement.Min(x => x.Value) : 0;
            double maxYLeft = measurement.Count > 0 ? measurement.Max(x => x.Value) : 1;

            double minYRight = feature.Count > 0 ? feature.Min(x => x.Value) : 0;
            double maxYRight = feature.Count > 0 ? feature.Max(x => x.Value) : 1;

            if (maxX == minX) maxX += 1;
            if (maxYLeft == minYLeft) maxYLeft += 1;
            if (maxYRight == minYRight) maxYRight += 1;

            return (minX, maxX, minYLeft, maxYLeft, minYRight, maxYRight);
        }

        // 特徴量はパターン固有なので、候補をパターンで絞る
        // パターンは「検索条件のパターン」→「選択中の画像のパターン」の順に決め、どちらも無ければ全特徴量
        // selectFirst=true（画像を選んだとき）は、特徴量が未選択か候補に無ければ先頭の特徴量を選ぶ
        // 戻り値：選択中の特徴量が変わったか
        private bool UpdateFeatureList(bool selectFirst)
        {
            string? searchPattern = CB_SamplePatternTop.SelectedItem?.ToString();
            string? pattern = string.IsNullOrWhiteSpace(searchPattern) ? _selectedPattern : searchPattern;

            var names = _trendRepo.GetFeatureNames(pattern);
            names.Insert(0, "");

            string current = _selectedFeature ?? "";
            string next = current;

            if (!names.Contains(next) || (selectFirst && next == ""))
                next = selectFirst && names.Count > 1 ? names[1] : "";

            bool wasUpdating = _isUpdating;
            _isUpdating = true;
            try
            {
                CB_Feature.ItemsSource = names;
                CB_Feature.SelectedItem = next;
            }
            finally
            {
                _isUpdating = wasUpdating;
            }

            _selectedFeature = next == "" ? null : next;
            return next != current;
        }

        private void CB_Feature_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            _selectedFeature =
                CB_Feature.SelectedItem?.ToString();

            RefreshTrendGraph();
        }

        private void CB_Measurement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            if (CB_Measurement.SelectedItem == null)
                return;

            _selectedMeasurement =
                CB_Measurement.SelectedItem.ToString()!;

            RefreshTrendGraph();
        }

        #endregion

        #region 描画

        // グラフが少なければ領域を等分して埋め、多ければ最小の高さを保って縦スクロールにする
        // 1件だけのときは縦長になりすぎないよう、2件のときと同じ高さにする
        private void UpdateTrendGraphHeight()
        {
            int count = (IC_TrendGraphs.ItemsSource as IList<TrendGraphItem>)?.Count ?? 0;

            if (count == 0 || SV_TrendGraphs.ActualHeight <= 0)
                return;

            double graphHeight = Math.Max(SV_TrendGraphs.ActualHeight / Math.Max(count, 2), MinTrendGraphHeight);

            IC_TrendGraphs.Height = graphHeight * count;
        }

        private void TrendGraphs_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrendGraphHeight();
        }

        private void TrendCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Canvas canvas)
                DrawTrendGraph(canvas);
        }

        private void TrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Canvas canvas)
                DrawTrendGraph(canvas);
        }

        // データは取得し直さず、選択状態の変更などを反映して描き直す
        private void RedrawTrendGraphs()
        {
            foreach (var canvas in FindVisualChildren<Canvas>(IC_TrendGraphs))
            {
                DrawTrendGraph(canvas);
            }
        }

        // 耐久1件分のグラフを描く。軸範囲は全グラフ共通の _trendRange を使う
        // 測定値：青の実線・丸（左軸）、特徴量：赤の破線・四角（右軸）
        private void DrawTrendGraph(Canvas canvas)
        {
            canvas.Children.Clear();

            if (canvas.DataContext is not TrendGraphItem graph)
                return;

            double leftMargin = GraphLeftMargin;
            double topMargin = GraphTopMargin;
            double width = canvas.ActualWidth - GraphLeftMargin - GraphRightMargin;
            double height = canvas.ActualHeight - GraphTopMargin - GraphBottomMargin;

            if (width <= 0 || height <= 0)
                return;

            if (graph.Points.Count == 0)
            {
                var empty = new TextBlock { Text = "データなし", Foreground = Brushes.Gray, FontSize = 11 };
                Canvas.SetLeft(empty, leftMargin + width / 2 - 25);
                Canvas.SetTop(empty, topMargin + height / 2 - 8);
                canvas.Children.Add(empty);
                return;
            }

            var (minX, maxX, minYLeft, maxYLeft, minYRight, maxYRight) = _trendRange;

            double ToX(double pages) => leftMargin + (pages - minX) / (maxX - minX) * width;

            double ToY(TrendPoint p) => p.IsFeature
                ? topMargin + height - (p.Value - minYRight) / (maxYRight - minYRight) * height
                : topMargin + height - (p.Value - minYLeft) / (maxYLeft - minYLeft) * height;

            bool hasFeature = graph.Points.Any(x => x.IsFeature);

            // 横グリッドと縦軸の目盛り
            for (int i = 0; i <= 4; i++)
            {
                double y = topMargin + height / 4 * i;

                canvas.Children.Add(new Line
                {
                    X1 = leftMargin,
                    X2 = leftMargin + width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                });

                var leftTb = new TextBlock
                {
                    Text = (maxYLeft - (maxYLeft - minYLeft) / 4 * i).ToString("F1"),
                    FontSize = 10,
                    Foreground = Brushes.Blue
                };
                Canvas.SetLeft(leftTb, 4);
                Canvas.SetTop(leftTb, y - 8);
                canvas.Children.Add(leftTb);

                if (hasFeature)
                {
                    var rightTb = new TextBlock
                    {
                        Text = (maxYRight - (maxYRight - minYRight) / 4 * i).ToString("F2"),
                        FontSize = 10,
                        Foreground = Brushes.DarkRed
                    };
                    Canvas.SetLeft(rightTb, leftMargin + width + 5);
                    Canvas.SetTop(rightTb, y - 8);
                    canvas.Children.Add(rightTb);
                }
            }

            // 横軸
            canvas.Children.Add(new Line
            {
                X1 = leftMargin,
                X2 = leftMargin + width,
                Y1 = topMargin + height,
                Y2 = topMargin + height,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            });

            for (int i = 0; i <= 4; i++)
            {
                double x = leftMargin + width / 4 * i;
                double pages = minX + (maxX - minX) / 4 * i;

                var tb = new TextBlock
                {
                    Text = $"{pages / 1000:F0}k",
                    FontSize = 10,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(tb, x - 10);
                Canvas.SetTop(tb, topMargin + height + 3);
                canvas.Children.Add(tb);
            }

            DrawDeveloperChanges(canvas, graph, ToX, topMargin, height);

            // 折れ線
            foreach (var series in graph.Points.GroupBy(x => x.SeriesName))
            {
                bool isFeature = series.Any(x => x.IsFeature);

                var polyline = new Polyline
                {
                    Stroke = isFeature ? Brushes.DarkRed : Brushes.Blue,
                    StrokeThickness = 1.5
                };

                if (isFeature)
                    polyline.StrokeDashArray = new DoubleCollection { 5, 3 };

                foreach (var p in series.OrderBy(x => x.Pages))
                    polyline.Points.Add(new Point(ToX(p.Pages), ToY(p)));

                canvas.Children.Add(polyline);
            }

            // マーカー（クリックで画像を選択）
            // 測定値の点は選択中の画像と同じ測定、特徴量の点は選択中の画像そのものを強調する
            bool selectionLineDrawn = false;

            foreach (var p in graph.Points)
            {
                double x = ToX(p.Pages);
                double y = ToY(p);

                bool isSelected = p.IsFeature
                    ? _selectedImageId.HasValue && p.ImageId == _selectedImageId.Value
                    : _selectedMeasurementId.HasValue && p.MeasurementId == _selectedMeasurementId.Value;

                Brush color = p.IsFeature ? Brushes.DarkRed : Brushes.Blue;

                Shape marker = p.IsFeature ? new Rectangle() : new Ellipse();

                marker.Tag = p;
                marker.ToolTip = string.IsNullOrEmpty(p.ImageName)
                    ? $"Pages={p.Pages}\n{p.SeriesName}={p.Value}"
                    : $"Pages={p.Pages}\n{p.SeriesName}={p.Value}\n{p.ImageName}";
                marker.MouseLeftButtonDown += TrendPoint_MouseLeftButtonDown;
                marker.Width = isSelected ? 12 : 7;
                marker.Height = isSelected ? 12 : 7;
                marker.Fill = isSelected ? Brushes.White : color;
                marker.Stroke = isSelected ? Brushes.Black : color;
                marker.StrokeThickness = isSelected ? 2 : 1;

                Canvas.SetLeft(marker, x - marker.Width / 2);
                Canvas.SetTop(marker, y - marker.Height / 2);
                canvas.Children.Add(marker);

                if (isSelected && !selectionLineDrawn)
                {
                    selectionLineDrawn = true;
                    canvas.Children.Add(new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = topMargin,
                        Y2 = topMargin + height,
                        Stroke = Brushes.OrangeRed,
                        StrokeThickness = 2
                    });
                }
            }

            UpdateViewBand(canvas);
        }

        // 現像器を交換した位置に緑の縦破線と「TN3」などのラベルを描く（マーカーより背面）
        private static void DrawDeveloperChanges(Canvas canvas, TrendGraphItem graph, Func<double, double> toX, double top, double height)
        {
            var brush = Brushes.ForestGreen;

            foreach (var change in graph.DeveloperChanges)
            {
                double x = toX(change.Pages);
                string tooltip = $"現像器交換：{change.Pages:#,0}枚\n{string.Join("\n", change.Details)}";

                canvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = top,
                    Y2 = top + height,
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    ToolTip = tooltip
                });

                var label = new TextBlock
                {
                    Text = string.Join(",", change.Developers),
                    FontSize = 9,
                    Foreground = brush,
                    ToolTip = tooltip
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, top);
                canvas.Children.Add(label);
            }
        }

        private void TrendPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Shape shape)
                return;

            if (shape.Tag is not TrendPoint point)
                return;

            // 下のキャンバスの「クリックで移動」を発火させない
            e.Handled = true;

            string durabilityId = point.SeriesName.Split(':')[0];
            var icon = FindIconForTrendPoint(point, durabilityId);

            if (icon != null)
            {
                // 左のアイコンも同じ画像を選択して、そこまでスクロールする
                RevealIcon(icon.ImageId);
                ShowImageDetails(icon);
                return;
            }

            _selectedMeasurementId = point.MeasurementId;

            if (point.ImageId > 0)
            {
                // 検索結果に無い画像（特徴量の点）は情報表示だけ行う
                _selectedImageId = point.ImageId;
                TB_SelectedImage.Text = $"Image : {point.ImageName}";
                DisplayImageInformation(point.ImageId.ToString());
            }
            else
            {
                ShowStatus("この測定の画像は検索結果にありません");
            }

            RedrawTrendGraphs();
        }

        // グラフの点に対応する、左に表示中のアイコンを探す
        // 特徴量の点は画像そのもの、測定値の点（画像を持たない）はその測定の画像から選ぶ
        // （選択中の画像と同じパターンを優先し、無ければ表示順で最初の画像）
        private IconItem? FindIconForTrendPoint(TrendPoint point, string durabilityId)
        {
            var icons = IsCompareMode
                ? CompareRowLists
                    .SelectMany(lb => lb.Items.OfType<CompareCell>())
                    .Select(c => c.Icon)
                    .OfType<IconItem>()
                    .Distinct()
                    .ToList()
                : LB_NormalIcons.Items.OfType<IconItem>().ToList();

            if (point.ImageId > 0)
            {
                string imageId = point.ImageId.ToString();

                var exact = icons.FirstOrDefault(x => x.ImageId == imageId);
                if (exact != null)
                    return exact;
            }

            return icons
                .Where(x => x.MeasurementId == point.MeasurementId ||
                            (x.DurabilityId == durabilityId && x.Pages == point.Pages))
                .OrderBy(x => x.MeasurementId == point.MeasurementId ? 0 : 1)
                .ThenBy(x => x.Pattern == _selectedPattern ? 0 : 1)
                .FirstOrDefault();
        }

        #endregion

        #region 表示中の枚数範囲（帯）

        // 各比較行の表示範囲を読み取り、グラフの帯を更新する
        private void UpdateVisibleRanges()
        {
            _visibleRanges.Clear();

            foreach (var lb in CompareRowLists)
            {
                if (lb.DataContext is not DurabilityImageGroup group || group.Cells.Count == 0)
                    continue;

                var sv = GetScrollViewer(lb);
                if (sv == null)
                    continue;

                // CanContentScroll=True のため Offset/Viewport の単位は「件」
                int first = Math.Clamp((int)sv.HorizontalOffset, 0, group.Cells.Count - 1);
                int last = Math.Clamp(first + (int)Math.Ceiling(sv.ViewportWidth) - 1, first, group.Cells.Count - 1);

                var pages = group.Cells
                    .Skip(first)
                    .Take(last - first + 1)
                    .Select(c => c.PositionPages)
                    .OfType<double>()
                    .ToList();

                if (pages.Count > 0)
                    _visibleRanges[group.DurabilityId] = (pages.Min(), pages.Max());
            }

            foreach (var canvas in FindVisualChildren<Canvas>(IC_TrendGraphs))
            {
                UpdateViewBand(canvas);
            }
        }

        // 帯だけを移動する（スクロールのたびにグラフ全体を描き直さない）
        private void UpdateViewBand(Canvas canvas)
        {
            var band = canvas.Children
                .OfType<Rectangle>()
                .FirstOrDefault(r => ViewBandTag.Equals(r.Tag));

            double width = canvas.ActualWidth - GraphLeftMargin - GraphRightMargin;
            double height = canvas.ActualHeight - GraphTopMargin - GraphBottomMargin;

            if (canvas.DataContext is not TrendGraphItem graph ||
                graph.Points.Count == 0 ||
                width <= 0 || height <= 0 ||
                !_visibleRanges.TryGetValue(graph.DurabilityId, out var range))
            {
                if (band != null)
                    band.Visibility = Visibility.Collapsed;
                return;
            }

            if (band == null)
            {
                band = new Rectangle
                {
                    Tag = ViewBandTag,
                    Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x0D, 0x2E, 0xA0)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x0D, 0x2E, 0xA0)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };

                // 折れ線やマーカーの背面に置く
                canvas.Children.Insert(0, band);
            }

            var (minX, maxX, _, _, _, _) = _trendRange;

            double ToX(double p) =>
                Math.Clamp(GraphLeftMargin + (p - minX) / (maxX - minX) * width,
                    GraphLeftMargin, GraphLeftMargin + width);

            double x1 = ToX(range.From);
            double x2 = ToX(range.To);

            band.Width = Math.Max(2, x2 - x1);
            band.Height = height;
            band.ToolTip = $"表示中：{range.From:#,0} ～ {range.To:#,0}枚";
            Canvas.SetLeft(band, x1);
            Canvas.SetTop(band, GraphTopMargin);
            band.Visibility = Visibility.Visible;
        }

        #endregion

        #region グラフをクリック／ドラッグして枚数を移動

        private void TrendCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Canvas canvas)
                return;

            canvas.CaptureMouse();
            NavigateFromGraph(canvas, e.GetPosition(canvas).X);
            e.Handled = true;
        }

        private void TrendCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Canvas canvas && canvas.IsMouseCaptured)
                NavigateFromGraph(canvas, e.GetPosition(canvas).X);
        }

        private void TrendCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas canvas && canvas.IsMouseCaptured)
                canvas.ReleaseMouseCapture();
        }

        // グラフ上のX座標を枚数に換算し、画像の表示位置をそこへ移動する
        private void NavigateFromGraph(Canvas canvas, double x)
        {
            if (canvas.DataContext is not TrendGraphItem graph)
                return;

            double width = canvas.ActualWidth - GraphLeftMargin - GraphRightMargin;
            if (width <= 0)
                return;

            var (minX, maxX, _, _, _, _) = _trendRange;
            double ratio = Math.Clamp((x - GraphLeftMargin) / width, 0, 1);
            double pages = minX + ratio * (maxX - minX);

            if (IsCompareMode)
            {
                // シンクロは全行、独立はそのグラフの耐久の行だけ
                var rows = CompareSyncMode
                    ? CompareRowLists.ToList()
                    : CompareRowLists
                        .Where(lb => (lb.DataContext as DurabilityImageGroup)?.DurabilityId == graph.DurabilityId)
                        .ToList();

                ScrollCompareRowsToPages(rows, pages, center: true);
                return;
            }

            // 通常表示：その耐久で最も近い枚数の画像を表示範囲に入れる
            var nearest = IconItems
                .Where(i => i.DurabilityId == graph.DurabilityId && i.Pages.HasValue)
                .MinBy(i => Math.Abs(i.Pages!.Value - pages));

            if (nearest != null)
                LB_NormalIcons.ScrollIntoView(nearest);
        }

        #endregion
    }
}
