using SampleViewer.Models;
using SampleViewer.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SampleViewer.Windows
{
    public partial class MainWindow
    {
        #region 通常表示の見出し（選択中の耐久）

        private const int MaxNormalHeaders = 8;

        // 耐久IDの一覧で選んだ耐久の環境・機種・評価目的を、画像一覧の上に出す
        private void UpdateNormalHeader()
        {
            var selected = LB_taikyu_ID.SelectedItems.OfType<DurabilityDisplayItem>().ToList();

            if (IsCompareMode || selected.Count == 0)
            {
                IC_NormalHeader.ItemsSource = null;
                IC_NormalHeader.Visibility = Visibility.Collapsed;
                return;
            }

            var shown = selected.Take(MaxNormalHeaders).ToList();
            var infos = SearchService.GetDurabilityInfo(shown.Select(x => x.Id));

            var headers = shown
                .Select(x => new DurabilityImageGroup
                {
                    DurabilityId = x.Id,
                    Purpose = x.EvaluationPurpose,
                    Env = infos.GetValueOrDefault(x.Id).Env ?? "",
                    Model = infos.GetValueOrDefault(x.Id).Model ?? ""
                })
                .ToList();

            foreach (var header in headers)
                ApplyMemos(header);

            IC_NormalHeader.ItemsSource = headers;
            IC_NormalHeader.Visibility = Visibility.Visible;
        }

        #endregion

        #region 画像情報

        private static readonly Brush InfoLabelBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
        private static readonly Brush InfoValueBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
        private static readonly Brush InfoLineBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

        private string _imageInfoText = "";

        // 選択した画像の情報を、まとまりごとに表示する（画像・耐久・測定・シリアル・特徴量）
        private void DisplayImageInformation(string imageId)
        {
            var lines = SearchService.GetImageInfo(imageId);

            _imageInfoText = string.Join(Environment.NewLine, lines);
            SP_ImageInfo.Children.Clear();
            BT_CopyImageInfo.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (lines.Count == 0)
            {
                SP_ImageInfo.Children.Add(new TextBlock
                {
                    Text = "画像を選ぶと情報が表示されます",
                    Foreground = InfoLabelBrush,
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            // 「列名: 値」の並び。空行より後ろは特徴量
            var values = new Dictionary<string, string>();
            var features = new List<(string Name, string Value)>();
            bool inFeatures = false;

            foreach (var line in lines)
            {
                int index = line.IndexOf(": ", StringComparison.Ordinal);

                if (index < 0)
                {
                    inFeatures = true;
                    continue;
                }

                string name = line[..index];
                string value = line[(index + 2)..].Trim();

                if (inFeatures)
                    features.Add((name, value));
                else
                    values[name] = value;
            }

            string Get(string name) => values.GetValueOrDefault(name) ?? "";

            SP_ImageInfo.Children.Add(new TextBlock
            {
                Text = Get("image_name"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = InfoValueBrush,
                TextWrapping = TextWrapping.Wrap
            });

            string pages = double.TryParse(Get("pages"), out var p) ? $"{p:#,0}枚" : "";

            AddInfoSection("画像");
            AddInfoRow("パターン", JoinNonEmpty(" / ", Get("pattern_name"), Get("color")));
            AddInfoRow("枚数", JoinNonEmpty("  ", pages, Get("cold_hot")));
            AddInfoRow("測定日時", Get("measurement_datetime"));
            AddInfoRow("画像ID", Get("image_id"));

            AddInfoSection("耐久");
            AddInfoRow("耐久ID", JoinNonEmpty("  ", Get("taikyu_code"), Get("env")));
            AddInfoRow("機種", JoinNonEmpty(" ", Get("maker"), Get("model_name")));
            AddInfoRow("評価目的", Get("purpose_name"));

            AddInfoSection("測定値");
            AddTonerTable(new[] { ("Vb", "Vb"), ("Q/M", "QM"), ("M/A", "MA") }, Get);

            var serials = new List<(string Label, string Value)>();

            foreach (var (label, column) in new[] { ("現像器", "TN"), ("ドラム", "DRM") })
            {
                for (int i = 0; i < TonerColors.Length; i++)
                {
                    string serial = Get($"{column}{i + 1}");

                    if (serial != "")
                        serials.Add(($"{label} {TonerColors[i]}", serial));
                }
            }

            if (serials.Count > 0)
            {
                AddInfoSection("シリアル");

                foreach (var (label, value) in serials)
                    AddInfoRow(label, value);
            }

            if (features.Count > 0)
            {
                AddInfoSection("特徴量");

                foreach (var (name, value) in features)
                    AddInfoRow(name, value, labelWidth: 130);
            }
        }

        private static string JoinNonEmpty(string separator, params string[] parts)
        {
            return string.Join(separator, parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private void AddInfoSection(string title)
        {
            SP_ImageInfo.Children.Add(new Border
            {
                BorderBrush = InfoLineBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 8, 0, 2),
                Padding = new Thickness(0, 5, 0, 0),
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("BrandBrush")
                }
            });
        }

        private void AddInfoRow(string label, string value, double labelWidth = 56)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };

            var labelText = new TextBlock
            {
                Text = label,
                Width = labelWidth,
                FontSize = 10.5,
                Foreground = InfoLabelBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = label
            };
            DockPanel.SetDock(labelText, Dock.Left);
            row.Children.Add(labelText);

            row.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 11,
                Foreground = InfoValueBrush,
                TextWrapping = TextWrapping.Wrap
            });

            SP_ImageInfo.Children.Add(row);
        }

        // 色ごとの測定値を Y/M/C/K の4列の表にする。0 と空欄は欠測として「–」
        private void AddTonerTable((string Label, string Column)[] rows, Func<string, string> get)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            for (int i = 0; i < TonerColors.Length; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i <= rows.Length; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void Add(UIElement element, int row, int column)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }

            for (int i = 0; i < TonerColors.Length; i++)
            {
                Add(new Border
                {
                    Height = 3,
                    Margin = new Thickness(2, 0, 2, 3),
                    CornerRadius = new CornerRadius(1.5),
                    Background = TonerBrushes[TonerColors[i]],
                    ToolTip = TonerColors[i]
                }, 0, i + 1);
            }

            for (int r = 0; r < rows.Length; r++)
            {
                Add(new TextBlock { Text = rows[r].Label, FontSize = 10.5, Foreground = InfoLabelBrush, Margin = new Thickness(0, 1, 0, 1) }, r + 1, 0);

                for (int i = 0; i < TonerColors.Length; i++)
                {
                    string value = get($"{rows[r].Column}{i + 1}");
                    bool isNumber = double.TryParse(value, out var number);
                    bool missing = value == "" || (isNumber && number == 0);

                    // 列に収まるよう、桁数に応じて丸める（正確な値はツールチップとコピーで確認できる）
                    string text = !isNumber ? value
                        : Math.Abs(number) >= 100 ? number.ToString("0")
                        : Math.Abs(number) >= 10 ? number.ToString("0.#")
                        : number.ToString("0.###");

                    Add(new TextBlock
                    {
                        Text = missing ? "–" : text,
                        ToolTip = missing ? null : value,
                        FontSize = 11,
                        Foreground = missing ? InfoLabelBrush : InfoValueBrush,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }, r + 1, i + 1);
                }
            }

            SP_ImageInfo.Children.Add(grid);
        }

        private void CopyImageInfo_Click(object sender, RoutedEventArgs e)
        {
            if (_imageInfoText == "")
                return;

            try
            {
                Clipboard.SetText(_imageInfoText);
                ShowStatus("画像情報をコピーしました");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowStatus("コピーできませんでした。もう一度お試しください");
            }
        }

        #endregion
    }
}
