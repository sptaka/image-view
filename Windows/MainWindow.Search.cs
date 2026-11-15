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


        private void RefreshSearchControls()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;

            try
            {
                var conditions =
                    CollectSearchConditionsFromUI();

                // メーカー・機種・環境は耐久IDの一覧を絞るための条件なので、一覧での選択には左右させない
                // パターンと色は選んだ耐久の中の画像を絞る条件なので、選択中の耐久に合わせて後で更新する（RefreshPatternChoices）
                // 印字枚数も画像を絞る条件。一覧には効かせない（範囲を狭めても、選択中の耐久が一覧から消えないように）
                var listConditions = new Dictionary<string, object>(conditions);
                listConditions.Remove(SearchFields.DurabilityId);
                listConditions.Remove(SearchConditionBuilder.PagesMinKey);
                listConditions.Remove(SearchConditionBuilder.PagesMaxKey);

                foreach (var (combo, field) in searchConditionControls)
                {
                    if (combo == CB_SamplePatternTop || combo == CB_colorTop)
                        continue;

                    UpdateComboBox(
                        combo,
                        field,
                        listConditions);
                }

                UpdateListBox(
                    LB_taikyu_ID,
                    SearchFields.DurabilityId,
                    listConditions);

                // 一覧から外れた耐久は選択も外れるので、条件を取り直す
                conditions = CollectSearchConditionsFromUI();

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
                dict[SearchFields.DurabilityId] = selectedIds;
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
            if (fieldName == SearchFields.DurabilityId)
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

        private string _patternChoiceKey = "";

        private static string CreateConditionKey(Dictionary<string, object> conditions)
        {
            return string.Join("|", conditions.Select(x =>
                $"{x.Key}={(x.Value is List<string> list ? string.Join(",", list) : x.Value)}"));
        }

        // パターンと色の候補を、表示対象の耐久（選択中＋比較に追加した耐久）にあるものだけにする
        // 複数の耐久があるときは、どれか1つにでもあれば候補に出す
        // 戻り値：候補から外れて、選択中のパターンか色が解除されたか
        private bool RefreshPatternChoices(Dictionary<string, object> conditions)
        {
            var scoped = new Dictionary<string, object>(conditions);

            if (IsCompareMode)
                scoped[SearchFields.DurabilityId] = GetDisplayDurabilityIds();

            // 条件が前回と同じなら候補も同じなので、問い合わせを省く
            string key = CreateConditionKey(scoped);

            if (key == _patternChoiceKey)
                return false;

            _patternChoiceKey = key;

            bool changed = false;
            bool wasUpdating = _isUpdating;
            _isUpdating = true;
            try
            {
                foreach (var (combo, field) in searchConditionControls)
                {
                    if (combo != CB_SamplePatternTop && combo != CB_colorTop)
                        continue;

                    string before = combo.SelectedItem?.ToString() ?? "";
                    UpdateComboBox(combo, field, scoped);
                    changed |= before != (combo.SelectedItem?.ToString() ?? "");
                }
            }
            finally
            {
                _isUpdating = wasUpdating;
            }

            return changed;
        }

        private void UpdateSearchResult(Dictionary<string, object> conditions)
        {
            if (RefreshPatternChoices(conditions))
                conditions = CollectSearchConditionsFromUI();

            // これより前に始めた検索の結果は使わない
            int version = ++_searchVersion;

            if (IsCompareMode)
            {
                // 比較モードは耐久ごとの検索だけを行い、全件検索はしない
                int hitCount = RefreshDurabilityGroups();
                ApplySearchResult(hitCount, DurabilityImageGroups.SelectMany(g => g.Icons).ToList());
                return;
            }

            DurabilityImageGroups.Clear();
            _visibleRanges.Clear();

            var request = CreateSearchRequest();

            // 耐久を選んでいるときは件数が少ないのですぐ表示する
            bool hasDurability = conditions.TryGetValue(SearchFields.DurabilityId, out var ids) && ids is List<string> { Count: > 0 };

            if (hasDurability)
            {
                var result = SearchIcons(conditions, request);
                ApplySearchResult(result.HitCount, result.Items);
                return;
            }

            // 耐久を選んでいない検索は件数が多く時間がかかるので、裏で実行して画面を止めない
            _ = SearchInBackgroundAsync(conditions, request, version);
        }

        private int _searchVersion;
        private const string SearchingText = "検索中…";

        private async Task SearchInBackgroundAsync(Dictionary<string, object> conditions, SearchRequest request, int version)
        {
            StatusText.Text = SearchingText;
            ResultCount.Text = SearchingText;

            SearchResult result;

            try
            {
                result = await Task.Run(() => SearchIcons(conditions, request));
            }
            catch (Exception ex)
            {
                if (version == _searchVersion)
                    ShowStatus($"検索に失敗しました：{ex.Message}");

                return;
            }

            // 待っている間に別の検索が始まっていたら、その結果に任せる
            if (version != _searchVersion)
                return;

            ApplySearchResult(result.HitCount, result.Items);
        }

        private void ApplySearchResult(int hitCount, List<IconItem> items)
        {
            // 1件ずつAddすると件数分の変更通知が飛ぶため、コレクションごと差し替える
            IconItems = new ObservableCollection<IconItem>(items);
            LB_NormalIcons.ItemsSource = IconItems;

            ResultCount.Text =
                $"画像 {hitCount:N0}件\n" +
                $"耐久 {LB_taikyu_ID.Items.Count}件";
            UpdateDisplayMode();
            UpdateNormalHeader();
            UpdatePagesSlider();

            // 表示対象の耐久やパターンが変わり得るので、特徴量の候補とグラフも更新する
            ApplyPatternColor(CB_colorTop.SelectedItem?.ToString());
            UpdateFeatureList(selectFirst: false);
            RefreshTrendGraph();

            // 裏の検索が終わった、または別の検索で置き換わった
            if (StatusText.Text == SearchingText)
                StatusText.Text = "Ready.";
        }

        // 検索に使う画面の設定。裏で検索するときに画面へ触れないよう、先に読み取っておく
        private record SearchRequest(
            string Label1, string Label2, string Label3, int Limit, bool FirstImageOnly,
            bool MemoOnly, AnnotationIndex Annotations);

        private SearchRequest CreateSearchRequest()
        {
            return new SearchRequest(
                GetInfoLabel(0),
                GetInfoLabel(1),
                GetInfoLabel(2),
                settings.User!.SearchLimit,
                CB_FirstImageOnly.IsChecked == true,
                CB_MemoOnly.IsChecked == true,
                _annotations);
        }

        // 画面の表示項目・検索上限で画像を検索する（画面には触れない）
        private SearchResult SearchIcons(Dictionary<string, object> conditions, SearchRequest request)
        {
            var result = SearchService.GetSearchResult(
                conditions,
                request.Label1,
                request.Label2,
                request.Label3,
                request.Limit,
                settings.IconRootPath!,
                settings.InstalledIconIds);

            ApplyMemos(result.Items, request.Annotations);

            // メモの絞り込みを先に行う（「1枚のみ」で、メモの無い方の画像が残らないように）
            if (request.MemoOnly)
            {
                result.Items = result.Items.Where(x => x.HasMemo).ToList();
                result.HitCount = result.Items.Count;
            }

            if (request.FirstImageOnly)
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

        private string? _pagesSliderKey;

        // スライダーの両端を、表示対象の耐久（選択中＋比較に追加）の最小～最大枚数に合わせる。未選択なら全耐久
        private void UpdatePagesSlider()
        {
            var ids = IsCompareMode
                ? GetDisplayDurabilityIds()
                : LB_taikyu_ID.SelectedItems.OfType<DurabilityDisplayItem>().Select(x => x.Id).ToList();

            string key = string.Join(",", ids);

            if (key != _pagesSliderKey)
            {
                _pagesSliderKey = key;

                var (min, max) = SearchService.GetPagesRange(ids);
                double span = max - min;

                RS_Pages.Step = span >= 20000 ? 1000 : span >= 2000 ? 100 : 10;
                RS_Pages.SetRange(min, max);
            }

            // 入力欄の値をつまみの位置に反映する（空欄は端）
            RS_Pages.SetValues(
                TryParsePages(TB_PagesMin.Text, out var lower) ? lower : RS_Pages.Minimum,
                TryParsePages(TB_PagesMax.Text, out var upper) ? upper : RS_Pages.Maximum);
        }

        // ドラッグ中は入力欄の表示だけ更新し、検索はつまみを離したときに行う。端にあるときは制限なし（空欄）
        private void PagesSlider_RangeChanged(object? sender, EventArgs e)
        {
            TB_PagesMin.Text = RS_Pages.IsLowerAtMinimum ? "" : FormatPages(RS_Pages.LowerValue);
            TB_PagesMax.Text = RS_Pages.IsUpperAtMaximum ? "" : FormatPages(RS_Pages.UpperValue);
        }

        private void PagesSlider_RangeCommitted(object? sender, EventArgs e)
        {
            ApplyPagesRange();
        }

        private static string FormatPages(double pages)
        {
            return pages >= 1000 && pages % 1000 == 0 ? $"{pages / 1000:0}k" : pages.ToString("0");
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
