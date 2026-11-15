using SampleViewer.Models;
using SampleViewer.Repositories;
using SampleViewer.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SampleViewer.Windows
{
    // 限度見本との比較：選んだ画像と、同じパターンの限度見本を比較ウィンドウに並べる
    public partial class MainWindow
    {
        private List<LimitSample>? _limitSamples;
        private bool _limitSamplesAreTest;

        // 限度見本の一覧。DBに limit_sample テーブルがあればそれを、無ければテスト用の定義ファイルを使う
        private List<LimitSample> GetLimitSamples()
        {
            if (_limitSamples != null)
                return _limitSamples;

            var repo = new LimitSampleRepository();

            try
            {
                if (repo.HasTable())
                {
                    _limitSamples = repo.GetFromDatabase();
                }
                else
                {
                    _limitSamples = SettingsService.Load<List<LimitSample>>(
                        Path.Combine(settings.Local!.ExeSettingsDir, "limitSamplesTest.json"));
                    repo.FillPatternNames(_limitSamples);
                    _limitSamplesAreTest = true;
                }
            }
            catch (Exception ex)
            {
                _limitSamples = new List<LimitSample>();
                ShowStatus($"限度見本を読み込めませんでした：{ex.Message}");
            }

            return _limitSamples;
        }

        // 画像に合う見本を特徴量ごとにまとめる：同じパターンで、全機種共通か、この画像の機種向けの見本
        private List<IGrouping<string, LimitSample>> FindLimitSamples(IconItem icon)
        {
            var samples = GetLimitSamples().Where(x => x.PatternName == icon.Pattern).ToList();

            if (samples.Count == 0)
                return new List<IGrouping<string, LimitSample>>();

            string model = SearchService.GetDurabilityInfo(new[] { icon.DurabilityId })
                .GetValueOrDefault(icon.DurabilityId).Model ?? "";

            return samples
                .Where(x => x.ModelName == "" || model == x.ModelName || model.EndsWith(" " + x.ModelName))
                .GroupBy(x => x.FeatureName)
                .ToList();
        }

        // 右クリックのメニューを開いた時点で、見本が無ければ選べないようにし、複数の特徴量にあれば下の階層で選ばせる
        private void IconContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menuItem = (sender as ContextMenu)?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(x => x.Tag as string == "LimitSample");

            if (menuItem == null)
                return;

            menuItem.Items.Clear();

            var icon = GetSelectedIcons().FirstOrDefault() ?? _selectedIcon;
            var features = icon == null ? null : FindLimitSamples(icon);

            if (icon == null || features == null || features.Count == 0)
            {
                menuItem.IsEnabled = false;
                menuItem.Header = icon == null
                    ? "限度見本と比較"
                    : $"限度見本と比較（「{icon.Pattern}」の見本は未登録）";
                return;
            }

            menuItem.IsEnabled = true;

            if (features.Count == 1)
            {
                menuItem.Header = $"限度見本と比較（{features[0].Key}）";
                return;
            }

            menuItem.Header = "限度見本と比較";

            foreach (var feature in features)
            {
                var samples = feature.ToList();
                var child = new MenuItem { Header = $"{feature.Key}（{samples.Count}件）" };

                child.Click += (_, args) =>
                {
                    // 親の項目のクリックとして処理されないようにする
                    args.Handled = true;
                    OpenLimitSampleCompare(icon, feature.Key, samples);
                };

                menuItem.Items.Add(child);
            }
        }

        private void CompareWithLimitSample_MenuClick(object sender, RoutedEventArgs e)
        {
            // 特徴量を下の階層で選ぶ場合は、そちらで開く
            if (sender is MenuItem { HasItems: true })
                return;

            var icon = GetSelectedIcons().FirstOrDefault() ?? _selectedIcon;

            if (icon == null)
                return;

            var features = FindLimitSamples(icon);

            if (features.Count == 0)
            {
                ShowStatus($"パターン「{icon.Pattern}」の限度見本は登録されていません");
                return;
            }

            OpenLimitSampleCompare(icon, features[0].Key, features[0].ToList());
        }

        // 左端に対象の画像、その右に見本を登録順（Lv.1 → Lv.4 → 特別救済など）で並べる
        private void OpenLimitSampleCompare(IconItem icon, string featureName, List<LimitSample> samples)
        {
            var items = CreateCompareItems(new[] { icon }, resolve: false);

            foreach (var item in items)
                item.Badge = "対象";

            var sampleItems = CreateCompareItems(
                samples.Select(x => (x.DurabilityId, x.ImageName, (double?)null, icon.Pattern)), resolve: false);

            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var sampleItem = sampleItems[i];

                sampleItem.Badge = string.IsNullOrWhiteSpace(sample.Grade) ? "限度見本" : $"限度 {sample.Grade}";
                sampleItem.IsLimitSample = true;
                sampleItem.Purpose = string.Join("  ", new[]
                {
                    sample.ModelName == "" ? "" : $"{sample.ModelName} 専用",
                    sample.Comment
                }.Where(x => x != ""));

                items.Add(sampleItem);
            }

            OpenCompareWindow(items,
                $"限度見本との比較 - {featureName}" + (_limitSamplesAreTest ? "（テスト用の見本）" : ""));

            if (_limitSamplesAreTest)
                ShowStatus("テスト用の限度見本を表示しています（本番の見本ではありません）");
        }
    }
}
