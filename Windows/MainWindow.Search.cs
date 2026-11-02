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
        #region 検索条件

        private void ConditionResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetCondition();
        }

        private void Condition_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;
            RefreshSearchControls();
        }

        private void InfoLabel_Changed(object sender, EventArgs e)
        {
            UpdateSearchResult(CollectSearchConditionsFromUI());
        }


        private void RefreshSearchControls()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;

            try
            {
                var conditions =
                    CollectSearchConditionsFromUI();

                foreach (var (combo, field) in searchConditionControls)
                {
                    UpdateComboBox(
                        combo,
                        field,
                        conditions);
                }

                UpdateListBox(
                    LB_taikyu_ID,
                    Columns.DurabilityId,
                    conditions);

                UpdateSearchResult(
                    conditions);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private Dictionary<string, object> CollectSearchConditionsFromUI()
        {
            var dict = new Dictionary<string, object>();
            foreach (var (combo, field) in searchConditionControls)
            {
                dict[field] = combo.SelectedItem?.ToString() ?? "";
            }

            // 表示用オブジェクトから耐久IDのみ抽出
            var selectedIds = LB_taikyu_ID.SelectedItems
                .Cast<object>()
                .Select(x => x is DurabilityDisplayItem ddi ? ddi.Id : x?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if(DurabilityCompareTargets.Count == 0)
            {
                dict[Columns.DurabilityId] = selectedIds;
            }

            if (TryParsePages(TB_PagesMin.Text, out var pagesMin))
                dict[SearchConditionBuilder.PagesMinKey] = pagesMin;

            if (TryParsePages(TB_PagesMax.Text, out var pagesMax))
                dict[SearchConditionBuilder.PagesMaxKey] = pagesMax;
            return dict;
        }

        private void UpdateComboBox(ComboBox comboBox, string fieldName, Dictionary<string, object> conditions)
        {
            var current =
                comboBox.SelectedItem?.ToString();

            var items =
                SearchService.GetDistinctValues(
                    fieldName,
                    conditions);

            items.Insert(0, "");

            bool keepSelection =
                !string.IsNullOrEmpty(current) &&
                items.Contains(current);

            comboBox.ItemsSource = null;
            comboBox.ItemsSource = items;

            if (keepSelection)
            {
                comboBox.SelectedItem = current;
            }
            else
            {
                comboBox.SelectedIndex = 0;
            }


        }

        private void UpdateListBox(ListBox listBox, string fieldName, Dictionary<string, object> conditions)
        {
            if (fieldName == Columns.DurabilityId)
            {
                // 1) 現在の選択IDを退避
                var prevSelectedIds = listBox.SelectedItems
                    .Cast<object>()
                    .Select(x => x is DurabilityDisplayItem ddi ? ddi.Id : x?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToHashSet();

                // 2) 表示データを作成
                var items = SearchService.GetDurabilityDisplayItems(conditions);

                // 3) ItemsSource を更新し、退避IDで構築

                _isUpdating = true;

                try
                {
                    listBox.ItemsSource = items;

                    foreach (var item in items)
                    {
                        if (prevSelectedIds.Contains(item.Id))
                        {
                            listBox.SelectedItems.Add(item);
                        }
                    }

                    // 選択を復元してから絞り込む（選択中の項目は絞り込みでも残す）
                    ApplyDurabilityFilter();
                }
                finally
                {
                    _isUpdating = false;
                }

               
            }
            else
            {
                var items = SearchService.GetDistinctValues(fieldName, conditions);
                listBox.ItemsSource = items;
            }
        }

        private void ResetCondition()
        {
            _isUpdating = true;

            try
            {
                LB_taikyu_ID.UnselectAll();

                foreach (var (combo, field) in searchConditionControls)
                {
                    combo.SelectedIndex = 0;
                }

                TB_TaikyuCode.Text = "";
                TB_SerialNo.Text = "";
                TB_PagesMin.Text = "";
                TB_PagesMax.Text = "";
                _appliedPagesRange = ("", "");
            }
            finally
            {
                _isUpdating = false;
            }

            RefreshSearchControls();
        }

        #endregion

        #region 検索結果

        private void UpdateSearchResult(Dictionary<string, object> conditions)
        {
            int hitCount;
            List<IconItem> items;

            if (IsCompareMode)
            {
                // 比較モードは耐久ごとの検索だけを行い、全件検索はしない
                hitCount = RefreshDurabilityGroups();
                items = DurabilityImageGroups.SelectMany(g => g.Icons).ToList();
            }
            else
            {
                DurabilityImageGroups.Clear();
                _visibleRanges.Clear();

                var result = SearchIcons(conditions);

                hitCount = result.HitCount;
                items = result.Items;
            }

            // 1件ずつAddすると件数分の変更通知が飛ぶため、コレクションごと差し替える
            IconItems = new ObservableCollection<IconItem>(items);
            LB_NormalIcons.ItemsSource = IconItems;

            ResultCount.Text =
                $"画像 {hitCount:N0}件\n" +
                $"耐久 {LB_taikyu_ID.Items.Count}件";
            UpdateDisplayMode();

            // 表示対象の耐久やパターンが変わり得るので、特徴量の候補とグラフも更新する
            UpdateFeatureList(selectFirst: false);
            RefreshTrendGraph();
        }

        // 画面の表示項目・検索上限で画像を検索する
        private SearchResult SearchIcons(Dictionary<string, object> conditions)
        {
            var result = SearchService.GetSearchResult(
                conditions,
                CB_InfoLabel1.Text,
                CB_InfoLabel2.Text,
                CB_InfoLabel3.Text,
                settings.User!.SearchLimit,
                settings.IconRootPath!,
                settings.InstalledIconIds);

            if (CB_FirstImageOnly.IsChecked == true)
            {
                int before = result.Items.Count;
                result.Items = KeepFirstOfSamePattern(result.Items);
                result.HitCount -= before - result.Items.Count;
            }

            return result;
        }

        // 同じ耐久・同じ枚数・同じパターンの画像は、画像IDが最も小さい1枚だけ残す（表示順は保つ）
        // 枚数が不明な画像はまとめられないのですべて残す
        private static List<IconItem> KeepFirstOfSamePattern(List<IconItem> items)
        {
            var firstIds = items
                .Where(x => x.Pages.HasValue)
                .GroupBy(x => (x.DurabilityId, x.Pages, x.Pattern))
                .Select(g => g.MinBy(x => long.TryParse(x.ImageId, out var id) ? id : long.MaxValue)!.ImageId)
                .ToHashSet();

            return items
                .Where(x => !x.Pages.HasValue || firstIds.Contains(x.ImageId))
                .ToList();
        }

        private void FirstImageOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _isUpdating)
                return;

            settings.User!.IsFirstImageOnly = CB_FirstImageOnly.IsChecked == true;
            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        #endregion

        #region 耐久ID一覧（選択・絞り込み）

        private void LB_taikyu_ID_Changed(object sender,SelectionChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            // グラフの更新は UpdateSearchResult 内で行う
            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        private void TB_TaikyuCode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            string taikyuCode =
                TB_TaikyuCode.Text.Trim();

            if (string.IsNullOrEmpty(taikyuCode))
            {
                return;
            }

            string searchCode = NormalizeDurabilityText(taikyuCode);

            var target =
                LB_taikyu_ID.Items
                    .Cast<DurabilityDisplayItem>()
                    .FirstOrDefault(x => NormalizeDurabilityText(x.Id) == searchCode);

            if (target == null)
            {
                SelectSingleCandidate($"耐久IDが見つかりません：{taikyuCode}");
                e.Handled = true;
                return;
            }

            // 正式な耐久ID表記へ補正
            TB_TaikyuCode.Text =
                target.Id;

            SelectDurability(target);

            e.Handled = true;
        }

        // シリアル番号：Enterで候補が1件ならそれを選択する
        private void TB_SerialNo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || NormalizeSerialText(TB_SerialNo.Text).Length == 0)
                return;

            SelectSingleCandidate($"シリアル番号が見つかりません：{TB_SerialNo.Text.Trim()}");
            e.Handled = true;
        }

        // 絞り込み後の候補が1件だけならそれを選択する。0件・複数件はステータスバーで知らせる
        private void SelectSingleCandidate(string notFoundMessage)
        {
            if (LB_taikyu_ID.Items.Count == 1 && LB_taikyu_ID.Items[0] is DurabilityDisplayItem single)
            {
                SelectDurability(single);
                return;
            }

            ShowStatus(LB_taikyu_ID.Items.Count == 0
                ? notFoundMessage
                : $"候補が {LB_taikyu_ID.Items.Count} 件あります。一覧から選択してください");
        }

        // 耐久を1件だけ選択して一覧の表示位置へスクロールする
        private void SelectDurability(DurabilityDisplayItem target)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var items =
                        LB_taikyu_ID.Items
                            .Cast<DurabilityDisplayItem>()
                            .ToList();

                    int index =
                        items.FindIndex(
                            x => x.Id == target.Id);

                    if (index < 0)
                    {
                        return;
                    }

                    LB_taikyu_ID.UnselectAll();

                    LB_taikyu_ID.SelectedIndex =
                        index;

                    LB_taikyu_ID.ScrollIntoView(
                        LB_taikyu_ID.Items[index]);

                    LB_taikyu_ID.Focus();
                }),
                DispatcherPriority.Background);
        }

        private static string NormalizeDurabilityText(string text)
        {
            return text.Replace("_", "").Trim().ToUpperInvariant();
        }

        // シリアル番号は区切り記号・空白・大文字小文字の違いを無視して比較する
        private static string NormalizeSerialText(string text)
        {
            return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        // taikyu_id → その耐久で使われたシリアル番号（正規化済み）。初回の絞り込み時に読み込む
        private Dictionary<int, List<string>>? _durabilitySerials;

        private Dictionary<int, List<string>> DurabilitySerials =>
            _durabilitySerials ??= SearchService.GetDurabilitySerials()
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(NormalizeSerialText).Where(s => s.Length > 0).Distinct().ToList());

        private void TB_TaikyuCode_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyDurabilityFilter();
        }

        private void TB_SerialNo_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyDurabilityFilter();
        }

        // 耐久ID・評価目的、シリアル番号（現像器・ドラム・本体）の入力文字を含むものだけ表示する
        // 両方入力した場合は両方に当てはまるもの。選択中の項目は常に残す
        private void ApplyDurabilityFilter()
        {
            if (LB_taikyu_ID.ItemsSource == null)
                return;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(LB_taikyu_ID.ItemsSource);
            string keyword = NormalizeDurabilityText(TB_TaikyuCode.Text);
            string serial = NormalizeSerialText(TB_SerialNo.Text);

            if (keyword.Length == 0 && serial.Length == 0)
            {
                view.Filter = null;
                return;
            }

            var serials = serial.Length > 0 ? DurabilitySerials : null;

            view.Filter = obj =>
                obj is DurabilityDisplayItem item &&
                (LB_taikyu_ID.SelectedItems.Contains(item) ||
                 (MatchesKeyword(item, keyword) && MatchesSerial(item, serial, serials)));
        }

        private static bool MatchesKeyword(DurabilityDisplayItem item, string keyword)
        {
            return keyword.Length == 0 ||
                   NormalizeDurabilityText(item.Id).Contains(keyword) ||
                   NormalizeDurabilityText(item.EvaluationPurpose).Contains(keyword);
        }

        private static bool MatchesSerial(DurabilityDisplayItem item, string serial, Dictionary<int, List<string>>? serials)
        {
            return serial.Length == 0 ||
                   (serials != null &&
                    serials.TryGetValue(item.TaikyuId, out var list) &&
                    list.Any(s => s.Contains(serial)));
        }

        #endregion

        #region 印字枚数の範囲指定

        // 最後に検索へ反映した入力値（フォーカスが外れるたびに再検索しないため）
        private (string Min, string Max) _appliedPagesRange = ("", "");

        // "50000" "50,000" "50k" を受け付ける。空欄・不正値は false
        private static bool TryParsePages(string text, out double pages)
        {
            pages = 0;
            string t = text.Replace(",", "").Trim().ToLowerInvariant();

            if (t.Length == 0)
                return false;

            double scale = 1;
            if (t.EndsWith("k"))
            {
                scale = 1000;
                t = t[..^1];
            }

            if (!double.TryParse(t, out var value))
                return false;

            pages = value * scale;
            return true;
        }

        private void ApplyPagesRange()
        {
            var current = (TB_PagesMin.Text.Trim(), TB_PagesMax.Text.Trim());
            if (current == _appliedPagesRange)
                return;

            foreach (var tb in new[] { TB_PagesMin, TB_PagesMax })
            {
                bool invalid = tb.Text.Trim().Length > 0 && !TryParsePages(tb.Text, out _);
                tb.BorderBrush = invalid ? Brushes.OrangeRed : (Brush)FindResource("BorderBrushStrong");
            }

            _appliedPagesRange = current;
            RefreshSearchControls();
        }

        private void PagesRange_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            ApplyPagesRange();
            e.Handled = true;
        }

        private void PagesRange_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyPagesRange();
        }

        #endregion
    }
}
