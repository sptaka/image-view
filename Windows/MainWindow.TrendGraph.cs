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
using System.Windows.Controls.Primitives;
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
        private long? _selectedImageId = null;
        private string? _selectedFeature = null;

        // 選択中の画像の測定（測定値の点の強調表示）とパターン（特徴量の候補）
        private long? _selectedMeasurementId = null;
        private string? _selectedPattern = null;

        // 右ペインのグラフ1枚あたりの最小の高さ（これ未満になる場合は縦スクロール）
        private const double MinTrendGraphHeight = 170;

        // 全グラフ共通の軸範囲（耐久間で比較しやすいように揃える）。Y軸は測定値の種類・特徴量ごとに持つ
        private (double MinX, double MaxX) _trendXRange;
        private Dictionary<string, (double Min, double Max)> _trendYRanges = new();

        // 測定値の種類と、DBの列番号に対応する色（Vb1=Y, Vb2=M, Vb3=C, Vb4=K）
        private static readonly string[] TrendQuantities = { "Vb", "QM", "MA" };
        private static readonly string[] TonerColors = { "Y", "M", "C", "K" };

        // グラフで選択中の測定値の種類（線種とマーカーの形はこの順番で決まる）
        private List<string> _activeQuantities = new();

        // 種類ごとにグラフを分けて縦に並べているか（耐久を1つだけ見ているとき）。null は未判定
        private bool? _trendStacked;

        // 耐久が複数あるときに、詳しく見る耐久（タブで選択）。null は「全体」
        private string? _trendFocusId;

        // 表示ごとに覚えておく測定値の選択：分けるときは全種類、重ねるときは見づらいので Vb だけが既定
        private List<string> _stackedQuantities = TrendQuantities.ToList();
        private List<string> _overlayQuantities = new() { "Vb" };

        // 最後に色を合わせたパターンの色。同じ色の間は、手動で切り替えた表示を維持する
        private string? _lastPatternColor;

        private Dictionary<string, TrendAxisRange>? _trendAxisRanges;

        private static readonly Dictionary<string, Brush> TonerBrushes = new()
        {
            ["Y"] = new SolidColorBrush(Color.FromRgb(0xD4, 0xA9, 0x00)),
            ["M"] = new SolidColorBrush(Color.FromRgb(0xD6, 0x00, 0x7F)),
            ["C"] = new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0xD6)),
            ["K"] = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22))
        };

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
                IC_TrendTabs.Visibility = Visibility.Collapsed;
                TB_TrendTarget.Text = "";
                return;
            }

            // タブで耐久を選んでいるときは、その耐久だけを対象にする
            if (graphs.All(x => x.DurabilityId != _trendFocusId))
                _trendFocusId = null;

            UpdateTrendTabs(graphs);

            if (_trendFocusId != null)
                graphs = graphs.Where(x => x.DurabilityId == _trendFocusId).ToList();

            var ids = graphs.Select(x => x.DurabilityId).ToList();

            // 耐久が1つのときは種類ごとにグラフを分ける。複数を並べるときは1耐久1グラフに重ねる
            bool stacked = graphs.Count == 1;

            if (stacked != _trendStacked)
            {
                // 測定値の選択は表示ごとに覚えておき、戻ったときに復元する
                var current = TrendQuantities.Where(x => GetQuantityToggle(x).IsChecked == true).ToList();

                if (_trendStacked == true)
                    _stackedQuantities = current;
                else if (_trendStacked == false)
                    _overlayQuantities = current;

                _trendStacked = stacked;
                SetTrendQuantities(stacked ? _stackedQuantities : _overlayQuantities);

                // 重ねるときは特徴量を外す（必要なら一覧から選べる。画像を選んでも自動では付けない）
                // 分ける表示に戻ったら、選択中の画像に合う特徴量を付け直す
                if (!stacked)
                    ClearSelectedFeature();
                else
                    UpdateFeatureList(selectFirst: _selectedImageId.HasValue);
            }

            _activeQuantities = TrendQuantities
                .Where(x => GetQuantityToggle(x).IsChecked == true)
                .ToList();

            var points = _trendRepo.GetMeasurementTrend(ids, CreateTrendMeasurements());

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

                // 画像・測定に付いたメモを、枚数ごとにまとめてグラフに出す
                graph.Memos = _annotations.ForDurability(graph.DurabilityId)
                    .Where(x => x.Pages.HasValue)
                    .GroupBy(x => x.Pages!.Value)
                    .Select(g => (g.Key, $"メモ：{g.Key:#,0}枚\n{string.Join("\n", g.Select(x => x.Summary))}"))
                    .ToList();
                (graph.Env, graph.Model, _) = infos.GetValueOrDefault(graph.DurabilityId);
                graph.Env ??= "";
                graph.Model ??= "";
            }

            CalculateTrendRanges(points);
            UpdateTrendLegend();

            TB_TrendTarget.Text = points.Count == 0
                ? "data not found"
                : $"Durability : {string.Join(", ", ids)}";

            IC_TrendGraphs.ItemsSource = stacked ? HideEmptyGraphs(SplitByQuantity(graphs[0])) : graphs;
            TB_TrendEmpty.Visibility = stacked && TB_TrendEmpty.Text != "" ? Visibility.Visible : Visibility.Collapsed;
            UpdateTrendGraphHeight();
        }

        // 種類ごとに分けたグラフのうち、データが無い種類は空のグラフを並べず、1行の案内にまとめる
        private List<TrendGraphItem> HideEmptyGraphs(List<TrendGraphItem> graphs)
        {
            var empty = graphs.Where(x => x.HasTitle && x.Points.Count == 0).ToList();

            TB_TrendEmpty.Text = empty.Count > 0
                ? $"データなし：{string.Join("、", empty.Select(x => x.Title))}"
                : "";

            var shown = graphs.Except(empty).ToList();

            // 全部無いときは、空のグラフを1つだけ残す
            return shown.Count > 0 ? shown : graphs.Take(1).ToList();
        }

        // 耐久が2つ以上あるときだけタブを出す：「全体」と各耐久
        private void UpdateTrendTabs(List<TrendGraphItem> graphs)
        {
            if (graphs.Count < 2)
            {
                IC_TrendTabs.Visibility = Visibility.Collapsed;
                return;
            }

            var tabs = new List<TrendTab>
            {
                new()
                {
                    Label = "全体",
                    ToolTip = "耐久ごとに1つのグラフで見比べます",
                    IsSelected = _trendFocusId == null
                }
            };

            tabs.AddRange(graphs.Select(x => new TrendTab
            {
                Id = x.DurabilityId,
                Label = $"{x.SlotName} {x.DurabilityId}".Trim(),
                ToolTip = "この耐久だけを、測定値の種類ごとのグラフで詳しく見ます",
                IsSelected = x.DurabilityId == _trendFocusId
            }));

            IC_TrendTabs.ItemsSource = tabs;
            IC_TrendTabs.Visibility = Visibility.Visible;
        }

        private void TrendTab_Click(object sender, RoutedEventArgs e)
        {
            string id = (sender as FrameworkElement)?.Tag as string ?? "";
            SetTrendFocus(id == "" ? null : id);
        }

        // 「全体」でグラフの見出しの耐久IDを押すと、その耐久のタブへ移る
        private void TrendGraphTitle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_trendFocusId == null &&
                IC_TrendTabs.Visibility == Visibility.Visible &&
                (sender as FrameworkElement)?.DataContext is TrendGraphItem graph)
            {
                SetTrendFocus(graph.DurabilityId);
            }
        }

        private void SetTrendFocus(string? durabilityId)
        {
            if (_trendFocusId == durabilityId)
                return;

            _trendFocusId = durabilityId;
            RefreshTrendGraph();
        }

        // 1耐久のグラフを、測定値の種類と特徴量ごとのグラフに分ける
        private List<TrendGraphItem> SplitByQuantity(TrendGraphItem graph)
        {
            var quantities = _activeQuantities.ToList();

            if (!string.IsNullOrWhiteSpace(_selectedFeature))
                quantities.Add(_selectedFeature);

            if (quantities.Count == 0)
                return new List<TrendGraphItem> { graph };

            return quantities
                .Select(quantity => new TrendGraphItem
                {
                    SlotName = graph.SlotName,
                    Title = GetQuantityLabel(quantity),
                    DurabilityId = graph.DurabilityId,
                    Purpose = graph.Purpose,
                    Env = graph.Env,
                    Model = graph.Model,
                    DeveloperChanges = graph.DeveloperChanges,
                    Memos = graph.Memos,
                    Points = graph.Points.Where(p => p.Quantity == quantity).ToList()
                })
                .ToList();
        }

        private ToggleButton GetQuantityToggle(string quantity) => quantity switch
        {
            "QM" => TG_QM,
            "MA" => TG_MA,
            _ => TG_Vb
        };

        // 画面に出す名前（DBの列名 QM・MA は Q/M・M/A と表記する）
        private static string GetQuantityLabel(string quantity) => quantity switch
        {
            "QM" => "Q/M",
            "MA" => "M/A",
            _ => quantity
        };

        private void SetTrendQuantities(IEnumerable<string> quantities)
        {
            bool wasUpdating = _isUpdating;
            _isUpdating = true;
            try
            {
                foreach (var quantity in TrendQuantities)
                    GetQuantityToggle(quantity).IsChecked = quantities.Contains(quantity);
            }
            finally
            {
                _isUpdating = wasUpdating;
            }
        }

        // 選択中の種類×色を、DBの列に対応付ける。機種で色の順番が変わる場合はここを機種別にする
        private List<TrendMeasurement> CreateTrendMeasurements()
        {
            var colors = TonerColors
                .Select((color, index) => (color, index))
                .Where(x => GetTonerToggle(x.color).IsChecked == true)
                .ToList();

            return _activeQuantities
                .SelectMany(q => colors.Select(c => new TrendMeasurement(q, c.color, $"{q}{c.index + 1}")))
                .ToList();
        }

        private ToggleButton GetTonerToggle(string color) => color switch
        {
            "Y" => TG_Y,
            "M" => TG_M,
            "C" => TG_C,
            _ => TG_K
        };

        private void InitializeTrendMeasurements()
        {
            SetTrendQuantities(new[] { "Vb" });
        }

        // パターンの色に合わせて表示する色を切り替える（Y/M/C/K は1色、Color は4色）
        // 戻り値：表示する色が変わったか
        private bool ApplyPatternColor(string? patternColor)
        {
            bool isToner = TonerColors.Contains(patternColor);

            if ((!isToner && patternColor != "Color") || patternColor == _lastPatternColor)
                return false;

            _lastPatternColor = patternColor;

            bool changed = false;
            bool wasUpdating = _isUpdating;
            _isUpdating = true;
            try
            {
                foreach (var color in TonerColors)
                {
                    bool show = !isToner || color == patternColor;
                    var toggle = GetTonerToggle(color);

                    changed |= (toggle.IsChecked == true) != show;
                    toggle.IsChecked = show;
                }
            }
            finally
            {
                _isUpdating = wasUpdating;
            }

            return changed;
        }

        private void TrendOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _isUpdating)
                return;

            RefreshTrendGraph();
        }

        // 既定のY軸範囲を読み込む。読めなくても自動調整で動くようにする
        private Dictionary<string, TrendAxisRange> GetTrendAxisRanges()
        {
            if (_trendAxisRanges != null)
                return _trendAxisRanges;

            try
            {
                _trendAxisRanges = SettingsService.Load<Dictionary<string, TrendAxisRange>>(
                    Path.Combine(settings.Local!.ExeSettingsDir, "trendAxisRanges.json"));
            }
            catch (Exception ex)
            {
                _trendAxisRanges = new();
                ShowStatus($"Y軸の既定範囲を読み込めませんでした：{ex.Message}");
            }

            return _trendAxisRanges;
        }

        // 軸範囲を決める。Y軸は「固定」かつ既定があれば既定の範囲、それ以外はデータの範囲
        private void CalculateTrendRanges(List<TrendPoint> points)
        {
            double minX = points.Count > 0 ? points.Min(x => x.Pages) : 0;
            double maxX = points.Count > 0 ? points.Max(x => x.Pages) : 1;

            if (maxX == minX) maxX += 1;

            _trendXRange = (minX, maxX);

            bool useFixed = CB_TrendFixedY.IsChecked == true;
            var fixedRanges = GetTrendAxisRanges();

            _trendYRanges = new();

            foreach (var group in points.GroupBy(x => x.Quantity))
            {
                if (useFixed &&
                    fixedRanges.TryGetValue(group.Key, out var range) &&
                    range.Max > range.Min)
                {
                    _trendYRanges[group.Key] = (range.Min, range.Max);
                    continue;
                }

                double min = group.Min(x => x.Value);
                double max = group.Max(x => x.Value);

                _trendYRanges[group.Key] = (min, max > min ? max : min + 1);
            }
        }

        private (double Min, double Max) GetYRange(string quantity)
        {
            return _trendYRanges.TryGetValue(quantity, out var range) ? range : (0, 1);
        }

        private static string FormatAxisValue(double value, double span)
        {
            return value.ToString(span < 10 ? "F2" : span < 100 ? "F1" : "F0");
        }

        // 測定値の種類ごとのマーカー：丸・ひし形・三角（特徴量は四角）
        private static Shape CreateQuantityMarker(int quantityIndex)
        {
            return quantityIndex switch
            {
                1 => new Polygon { Points = PointCollection.Parse("0.5,0 1,0.5 0.5,1 0,0.5"), Stretch = Stretch.Fill },
                2 => new Polygon { Points = PointCollection.Parse("0.5,0 1,1 0,1"), Stretch = Stretch.Fill },
                _ => new Ellipse()
            };
        }

        // 凡例：マーカーの形と種類ごとの範囲。左軸の目盛りは1つ目の測定値、右軸は特徴量
        private void UpdateTrendLegend()
        {
            WP_TrendLegend.Children.Clear();

            void Add(Shape marker, string quantity, string axis)
            {
                marker.Width = 8;
                marker.Height = 8;
                marker.Fill = Brushes.DimGray;
                marker.VerticalAlignment = VerticalAlignment.Center;
                marker.Margin = new Thickness(0, 0, 4, 0);

                var (min, max) = GetYRange(quantity);
                var text = new TextBlock
                {
                    Text = $"{GetQuantityLabel(quantity)}{axis} {FormatAxisValue(min, max - min)}～{FormatAxisValue(max, max - min)}",
                    FontSize = 11,
                    Foreground = Brushes.DimGray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };

                WP_TrendLegend.Children.Add(marker);
                WP_TrendLegend.Children.Add(text);
            }

            for (int i = 0; i < _activeQuantities.Count; i++)
            {
                if (_trendYRanges.ContainsKey(_activeQuantities[i]))
                    Add(CreateQuantityMarker(i), _activeQuantities[i], i == 0 && _trendStacked != true ? "（左軸）" : "");
            }

            if (_selectedFeature != null && _trendYRanges.ContainsKey(_selectedFeature))
                Add(new Rectangle(), _selectedFeature, _trendStacked == true ? "" : "（右軸）");
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

            // 複数の耐久を重ねているときは、画像を選んでも特徴量を自動では付けない
            selectFirst &= _trendStacked != false;

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

        private void ClearSelectedFeature()
        {
            bool wasUpdating = _isUpdating;
            _isUpdating = true;
            try
            {
                CB_Feature.SelectedItem = "";
            }
            finally
            {
                _isUpdating = wasUpdating;
            }

            _selectedFeature = null;
        }

        private void CB_Feature_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            _selectedFeature =
                CB_Feature.SelectedItem?.ToString();

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

        // 耐久1件分のグラフを描く。軸範囲は全グラフ共通
        // 測定値：線の色はトナー色、線種とマーカーは種類ごと（左軸）、特徴量：赤の破線・四角（右軸）
        // 軸の範囲外の値は端に寄せて白抜きのマーカーで示す（ツールチップは実際の値）
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

            var (minX, maxX) = _trendXRange;

            double ToX(double pages) => leftMargin + (pages - minX) / (maxX - minX) * width;

            double ToY(TrendPoint p)
            {
                var (min, max) = GetYRange(p.Quantity);
                return topMargin + height - (Math.Clamp(p.Value, min, max) - min) / (max - min) * height;
            }

            bool IsOutOfRange(TrendPoint p)
            {
                var (min, max) = GetYRange(p.Quantity);
                return p.Value < min || p.Value > max;
            }

            // 左軸は1つ目の測定値、右軸は特徴量。種類ごとに分けたグラフは、その種類を左軸に出す
            string? leftAxis = graph.HasTitle
                ? graph.Points.FirstOrDefault()?.Quantity
                : _activeQuantities.FirstOrDefault(_trendYRanges.ContainsKey);
            string? rightAxis = graph.HasTitle ? null : graph.Points.FirstOrDefault(x => x.IsFeature)?.Quantity;
            Brush leftAxisBrush = graph.Points[0].IsFeature && graph.HasTitle ? Brushes.DarkRed : Brushes.DimGray;

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

                if (leftAxis != null)
                {
                    var (min, max) = GetYRange(leftAxis);
                    var leftTb = new TextBlock
                    {
                        Text = FormatAxisValue(max - (max - min) / 4 * i, max - min),
                        FontSize = 10,
                        Foreground = leftAxisBrush
                    };
                    Canvas.SetLeft(leftTb, 4);
                    Canvas.SetTop(leftTb, y - 8);
                    canvas.Children.Add(leftTb);
                }

                if (rightAxis != null)
                {
                    var (min, max) = GetYRange(rightAxis);
                    var rightTb = new TextBlock
                    {
                        Text = FormatAxisValue(max - (max - min) / 4 * i, max - min),
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
            DrawMemoMarkers(canvas, graph, ToX);

            // 折れ線
            foreach (var series in graph.Points.GroupBy(x => x.SeriesName))
            {
                var first = series.First();

                var polyline = new Polyline
                {
                    Stroke = TonerBrushes.GetValueOrDefault(first.TonerColor) ?? Brushes.DarkRed,
                    StrokeThickness = 1.5
                };

                if (first.IsFeature)
                    polyline.StrokeDashArray = new DoubleCollection { 5, 3 };
                else if (_activeQuantities.IndexOf(first.Quantity) == 1)
                    polyline.StrokeDashArray = new DoubleCollection { 6, 3 };
                else if (_activeQuantities.IndexOf(first.Quantity) == 2)
                    polyline.StrokeDashArray = new DoubleCollection { 2, 2 };

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

                Brush color = TonerBrushes.GetValueOrDefault(p.TonerColor) ?? Brushes.DarkRed;

                Shape marker = p.IsFeature
                    ? new Rectangle()
                    : CreateQuantityMarker(_activeQuantities.IndexOf(p.Quantity));

                string name = p.IsFeature ? p.Quantity : $"{GetQuantityLabel(p.Quantity)} {p.TonerColor}";

                marker.Tag = p;
                marker.ToolTip = string.IsNullOrEmpty(p.ImageName)
                    ? $"Pages={p.Pages}\n{name}={p.Value}"
                    : $"Pages={p.Pages}\n{name}={p.Value}\n{p.ImageName}";
                marker.MouseLeftButtonDown += TrendPoint_MouseLeftButtonDown;
                marker.Width = isSelected ? 12 : 7;
                marker.Height = isSelected ? 12 : 7;
                marker.Fill = isSelected || IsOutOfRange(p) ? Brushes.White : color;
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
            UpdateHoverLine(canvas);
        }

        // 現像器を交換した位置に縦の破線と色の札を描く（マーカーより背面）。表示中の色の交換だけを出す
        // 札は色ごとに高さを固定して、交換が近くても重ならず、色ごとに横へ追えるようにする
        // 線の色は1色の交換ならその色、複数同時なら灰色
        private void DrawDeveloperChanges(Canvas canvas, TrendGraphItem graph, Func<double, double> toX, double top, double height)
        {
            const double BadgeSize = 11;

            double right = canvas.ActualWidth - GraphRightMargin;

            foreach (var change in graph.DeveloperChanges)
            {
                var shown = change.Developers
                    .Select((color, index) => (color, detail: change.Details[index]))
                    .Where(x => GetTonerToggle(x.color).IsChecked == true)
                    .ToList();

                if (shown.Count == 0)
                    continue;

                double x = toX(change.Pages);
                string tooltip = $"現像器交換：{change.Pages:#,0}枚\n{string.Join("\n", shown.Select(s => s.detail))}";

                canvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = top,
                    Y2 = top + height,
                    Stroke = shown.Count == 1 ? TonerBrushes[shown[0].color] : Brushes.Gray,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    ToolTip = tooltip
                });

                foreach (var (color, _) in shown)
                {
                    var badge = new Border
                    {
                        Width = BadgeSize,
                        Height = BadgeSize,
                        Background = TonerBrushes[color],
                        ToolTip = tooltip,
                        Child = new TextBlock
                        {
                            Text = color,
                            FontSize = 8,
                            FontWeight = FontWeights.Bold,
                            Foreground = color == "Y" ? Brushes.Black : Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                    // 右端では線の左側に出す
                    Canvas.SetLeft(badge, x + BadgeSize + 2 > right ? x - BadgeSize - 1 : x + 1);
                    Canvas.SetTop(badge, top + Array.IndexOf(TonerColors, color) * (BadgeSize + 1));
                    canvas.Children.Add(badge);
                }
            }
        }

        // メモのある枚数に、グラフの上端へ橙色の三角を出す（マウスを乗せると内容が出る）
        private void DrawMemoMarkers(Canvas canvas, TrendGraphItem graph, Func<double, double> toX)
        {
            var (minX, maxX) = _trendXRange;

            foreach (var (pages, text) in graph.Memos)
            {
                if (pages < minX || pages > maxX)
                    continue;

                double x = toX(pages);

                var marker = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x - 5, 0),
                        new Point(x + 5, 0),
                        new Point(x, GraphTopMargin)
                    },
                    Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    ToolTip = text
                };
                ToolTipService.SetInitialShowDelay(marker, 200);
                canvas.Children.Add(marker);
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

            SelectTrendPoint(point);
        }

        // グラフの点を選ぶ：対応する画像を選択し、点を強調する
        private void SelectTrendPoint(TrendPoint point)
        {
            string durabilityId = point.SeriesName.Split(':')[0];
            var icon = FindIconForTrendPoint(point, durabilityId);

            if (icon != null)
            {
                // 左のアイコンも同じ画像を選択して、そこまでスクロールする
                RevealIcon(icon.ImageId);

                // グラフ上の操作なので、表示中の色は変えない
                ShowImageDetails(icon, followColor: false);
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
        // （選択中の画像と同じパターン → クリックした線と同じ色 → 表示順の順に優先）
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
                .ThenBy(x => x.Color == point.TonerColor ? 0 : 1)
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

            var (minX, maxX) = _trendXRange;

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

        #region マウスを乗せた位置の枚数（画像とグラフの連動）

        private const string HoverLineTag = "HoverLine";
        private const string HoverLabelTag = "HoverLabel";

        // マウスを乗せている枚数。DurabilityId が null のときは全グラフに出す（グラフ上のマウス位置）
        private (string? DurabilityId, double Pages)? _trendHover;

        private void SetTrendHover(string? durabilityId, double? pages)
        {
            _trendHover = pages.HasValue ? (durabilityId, pages.Value) : null;

            foreach (var canvas in FindVisualChildren<Canvas>(IC_TrendGraphs))
                UpdateHoverLine(canvas);
        }

        // 画像にマウスを乗せると、その耐久のグラフの同じ枚数に縦線を出す
        private void IconItem_MouseEnter(object sender, MouseEventArgs e)
        {
            var data = (sender as ListBoxItem)?.DataContext;
            var icon = data as IconItem ?? (data as CompareCell)?.Icon;

            if (icon?.Pages is double pages)
                SetTrendHover(icon.DurabilityId, pages);
        }

        private void IconItem_MouseLeave(object sender, MouseEventArgs e)
        {
            SetTrendHover(null, null);
        }

        private void TrendCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            SetTrendHover(null, null);
        }

        // 縦線と枚数の表示だけを動かす（マウスが動くたびにグラフ全体を描き直さない）
        private void UpdateHoverLine(Canvas canvas)
        {
            var line = canvas.Children.OfType<Line>().FirstOrDefault(x => HoverLineTag.Equals(x.Tag));
            var label = canvas.Children.OfType<TextBlock>().FirstOrDefault(x => HoverLabelTag.Equals(x.Tag));

            double width = canvas.ActualWidth - GraphLeftMargin - GraphRightMargin;
            double height = canvas.ActualHeight - GraphTopMargin - GraphBottomMargin;
            var (minX, maxX) = _trendXRange;

            bool show = _trendHover is { } hover &&
                canvas.DataContext is TrendGraphItem graph &&
                graph.Points.Count > 0 &&
                width > 0 && height > 0 &&
                (hover.DurabilityId == null || hover.DurabilityId == graph.DurabilityId) &&
                hover.Pages >= minX && hover.Pages <= maxX;

            if (!show)
            {
                if (line != null) line.Visibility = Visibility.Collapsed;
                if (label != null) label.Visibility = Visibility.Collapsed;
                return;
            }

            if (line == null || label == null)
            {
                line = new Line
                {
                    Tag = HoverLineTag,
                    Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0x0D, 0x2E, 0xA0)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                label = new TextBlock
                {
                    Tag = HoverLabelTag,
                    FontSize = 10,
                    Padding = new Thickness(3, 0, 3, 0),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0D, 0x2E, 0xA0)),
                    IsHitTestVisible = false
                };
                canvas.Children.Add(line);
                canvas.Children.Add(label);
            }

            double pages = _trendHover!.Value.Pages;
            double x = GraphLeftMargin + (pages - minX) / (maxX - minX) * width;

            line.X1 = line.X2 = x;
            line.Y1 = GraphTopMargin;
            line.Y2 = GraphTopMargin + height;
            line.Visibility = Visibility.Visible;

            label.Text = $"{pages:#,0}枚";
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // 右端では線の左側に出す
            double labelWidth = label.DesiredSize.Width;
            Canvas.SetLeft(label, x + 3 + labelWidth > GraphLeftMargin + width ? x - 3 - labelWidth : x + 3);
            Canvas.SetTop(label, GraphTopMargin + height - 14);
            label.Visibility = Visibility.Visible;
        }

        #endregion

        #region グラフをクリック／ドラッグして枚数を移動

        private void TrendCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Canvas canvas)
                return;

            e.Handled = true;

            // グラフのどこを押しても点を選ぶ。点の近くならその点、離れていれば縦線の位置（同じ枚数）の点
            // ドラッグを続けた場合は、その枚数へ画像の表示位置を移動する
            var position = e.GetPosition(canvas);
            var nearest = FindNearestTrendPoint(canvas, position);

            canvas.CaptureMouse();

            if (nearest != null)
                SelectTrendPoint(nearest);
            else
                NavigateFromGraph(canvas, position.X);
        }

        // 点から この距離以内を押したときは、その点を優先する
        private const double TrendPointHitRadius = 14;

        private static TrendPoint? FindNearestTrendPoint(Canvas canvas, Point position)
        {
            var markers = canvas.Children
                .OfType<Shape>()
                .Where(x => x.Tag is TrendPoint)
                .Select(x => (
                    Point: (TrendPoint)x.Tag,
                    X: Canvas.GetLeft(x) + x.Width / 2,
                    Y: Canvas.GetTop(x) + x.Height / 2))
                .ToList();

            if (markers.Count == 0)
                return null;

            double Distance((TrendPoint Point, double X, double Y) m) =>
                Math.Sqrt(Math.Pow(m.X - position.X, 2) + Math.Pow(m.Y - position.Y, 2));

            var close = markers.MinBy(Distance);

            if (Distance(close) <= TrendPointHitRadius)
                return close.Point;

            // 離れているときは、横位置（枚数）が最も近い点。同じ枚数に複数あれば、縦位置が近いもの
            return markers
                .OrderBy(m => Math.Round(Math.Abs(m.X - position.X)))
                .ThenBy(m => Math.Abs(m.Y - position.Y))
                .First()
                .Point;
        }

        private void TrendCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Canvas canvas)
                return;

            double x = e.GetPosition(canvas).X;

            // マウス位置の枚数を、同じ横軸の全グラフに縦線で示す
            double width = canvas.ActualWidth - GraphLeftMargin - GraphRightMargin;

            if (width > 0 && x >= GraphLeftMargin && x <= GraphLeftMargin + width)
            {
                var (minX, maxX) = _trendXRange;
                SetTrendHover(null, minX + (x - GraphLeftMargin) / width * (maxX - minX));
            }
            else
            {
                SetTrendHover(null, null);
            }

            if (canvas.IsMouseCaptured)
                NavigateFromGraph(canvas, x);
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

            var (minX, maxX) = _trendXRange;
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
