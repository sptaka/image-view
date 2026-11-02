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
        #region 比較対象の管理（A～D）

        private const int MaxCompareTargets = 4;

        private List<string> GetDisplayDurabilityIds()
        {
            var result =
                DurabilityCompareTargets
                    .Select(x => x.DurabilityId)
                    .ToList();

            var current =
                CurrentBrowsingDurabilityId;

            if (!string.IsNullOrEmpty(current) &&
                !result.Contains(current))
            {
                if (result.Count < MaxCompareTargets)
                {
                    result.Add(current);
                }
            }

            return result;
        }

        private void LB_taikyu_ID_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
           
            DependencyObject? source =
                e.OriginalSource as DependencyObject;

            while (source != null &&
                   source is not ListBoxItem)
            {
                source =
                    VisualTreeHelper.GetParent(source);
            }

            if (source is ListBoxItem item &&
                item.DataContext is DurabilityDisplayItem target)
            {
                AddDurabilityCompareTarget(target);
                e.Handled = true;
            }
        }

        private void AddDurabilityCompareTarget(DurabilityDisplayItem item)
        {
            if (item == null)
                return;

            if (DurabilityCompareTargets.Any(
                x => x.DurabilityId == item.Id))
            {
                return;
            }

            var newTarget = new DurabilityCompareTarget
            {
                DurabilityId = item.Id,
                EvaluationPurpose = item.EvaluationPurpose
            };

            if (DurabilityCompareTargets.Count >= MaxCompareTargets)
            {
                // 満杯なら最後の枠と入れ替える
                var replaced = DurabilityCompareTargets[^1];
                DurabilityCompareTargets[^1] = newTarget;
                ShowStatus($"比較対象は最大{MaxCompareTargets}件のため、{replaced.DurabilityId} を {item.Id} に入れ替えました");
            }
            else
            {
                DurabilityCompareTargets.Add(newTarget);
                ShowStatus($"{item.Id} を比較対象に追加しました");
            }

            RenumberCompareSlots();
            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        // 削除・入れ替え後に A～D を振り直す
        private void RenumberCompareSlots()
        {
            for (int i = 0; i < DurabilityCompareTargets.Count; i++)
            {
                DurabilityCompareTargets[i].SlotName = ((char)('A' + i)).ToString();
            }
        }

        private void RemoveCompareTarget(string durabilityId)
        {
            var target = DurabilityCompareTargets.FirstOrDefault(x => x.DurabilityId == durabilityId);
            if (target == null)
                return;

            DurabilityCompareTargets.Remove(target);
            RenumberCompareSlots();
            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        private void ClearAllCompareTargets()
        {
            if (DurabilityCompareTargets.Count == 0)
                return;

            DurabilityCompareTargets.Clear();
            UpdateSearchResult(CollectSearchConditionsFromUI());
            ShowStatus("比較対象をすべて外しました");
        }

        private void ClearAllCompareTargets_Click(object sender, RoutedEventArgs e)
        {
            ClearAllCompareTargets();
        }

        // 閲覧行の「比較に追加」
        private void PinBrowsingDurability_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            string id = btn.Tag?.ToString() ?? "";

            var item = LB_taikyu_ID.Items
                .OfType<DurabilityDisplayItem>()
                .FirstOrDefault(x => x.Id == id);

            if (item != null)
                AddDurabilityCompareTarget(item);
        }

        private void RemoveCompareDurability_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            RemoveCompareTarget(btn.Tag?.ToString() ?? "");
        }

        #endregion

        #region 比較行の作成

        // 比較モード時の各耐久行の横リスト（画面上の並び順）
        private IEnumerable<ListBox> CompareRowLists => FindVisualChildren<ListBox>(IC_CompareRows);

        // 表示対象の耐久ごとに検索して行を作り直す。戻り値は全行のヒット件数の合計
        private int RefreshDurabilityGroups()
        {
            DurabilityImageGroups.Clear();
            int totalHitCount = 0;

            var durabilityIds = GetDisplayDurabilityIds();
            var infos = SearchService.GetDurabilityInfo(durabilityIds);

            foreach (var durabilityId in durabilityIds)
            {
                var conditions =
                    CreateConditionsForDurability(
                        durabilityId);

                var result = SearchIcons(conditions);

                totalHitCount += result.HitCount;

                var compareTarget =
                    DurabilityCompareTargets
                        .FirstOrDefault(
                            x => x.DurabilityId == durabilityId);

                DurabilityImageGroups.Add(
                    new DurabilityImageGroup
                    {
                        DurabilityId = durabilityId,
                        SlotName = compareTarget?.SlotName ?? "閲覧",
                        IsPinned = compareTarget != null,
                        Purpose =
                            compareTarget?.EvaluationPurpose
                            ?? (LB_taikyu_ID.SelectedItem as DurabilityDisplayItem)?.EvaluationPurpose
                            ?? "",
                        Env = infos.GetValueOrDefault(durabilityId).Env ?? "",
                        Model = infos.GetValueOrDefault(durabilityId).Model ?? "",
                        Icons = result.Items
                    });
            }

            ApplyCompareLayout(keepPosition: false);
            Dispatcher.BeginInvoke(UpdateAutoIconSize, DispatcherPriority.Loaded);

            return totalHitCount;
        }

        private Dictionary<string, object> CreateConditionsForDurability(string durabilityId)
        {
            var conditions =
                CollectSearchConditionsFromUI();

            conditions[Columns.DurabilityId] =
                new List<string> { durabilityId };

            return conditions;
        }

        #endregion

        #region 表示モード（独立／シンクロ、共通枚数のみ）

        private bool _isSyncingCompareScroll;
        private bool CompareSyncMode => RB_CompareSync.IsChecked == true;
        private bool CompareCommonOnly => CB_CompareCommonOnly.IsChecked == true;

        // 近似の許容差（null は制限なし）
        private double? CompareTolerance =>
            (CB_CompareTolerance.SelectedItem as ComboBoxItem)?.Tag is string tag &&
            double.TryParse(tag, out var value)
                ? value
                : null;

        private void CompareOption_Changed(object sender, RoutedEventArgs e)
        {
            // InitializeComponent 中（初期値設定）のイベントは無視
            if (!IsInitialized)
                return;

            // 許容差はシンクロかつ共通枚数のみOFFのときだけ意味を持つ
            CB_CompareTolerance.IsEnabled = CompareSyncMode && !CompareCommonOnly;

            ApplyCompareLayout(keepPosition: true);
        }

        // 検索結果（Icons）からモードに応じた表示用マス（Cells）を作り直す。再検索はしない
        private void ApplyCompareLayout(bool keepPosition)
        {
            if (DurabilityImageGroups.Count == 0)
                return;

            double? anchorPages = keepPosition ? GetCompareAnchorPages() : null;

            var rows = CompareAligner.Build(
                DurabilityImageGroups.Select(g => (IReadOnlyList<IconItem>)g.Icons).ToList(),
                CompareSyncMode,
                CompareCommonOnly,
                CompareTolerance);

            for (int i = 0; i < rows.Count; i++)
            {
                DurabilityImageGroups[i].Cells = rows[i];
            }

            if (anchorPages.HasValue)
            {
                // 行の再生成後に、切替前に見ていた枚数の位置へ戻す
                Dispatcher.BeginInvoke(
                    () => ScrollCompareRowsToPages(anchorPages.Value),
                    DispatcherPriority.Loaded);
            }
        }

        // 先頭行の左端に表示されている枚数
        private double? GetCompareAnchorPages()
        {
            var lb = CompareRowLists.FirstOrDefault();
            if (lb == null)
                return null;

            var sv = GetScrollViewer(lb);
            if (sv == null || lb.Items.Count == 0)
                return null;

            int index = Math.Clamp((int)sv.HorizontalOffset, 0, lb.Items.Count - 1);

            return (lb.Items[index] as CompareCell)?.PositionPages;
        }

        #endregion

        #region 横スクロール

        // ホイール1ノッチあたりに横送りする画像枚数（CanContentScroll=True のため単位は「件」）
        private const int CompareRowItemsPerNotch = 3;

        // 比較行の上でのホイール：その行だけを左右にスクロールする（Shift+ホイールは全体の縦スクロール）
        private void CompareRow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ListBox listBox)
                return;

            e.Handled = true;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                SV_CompareRows.ScrollToVerticalOffset(SV_CompareRows.VerticalOffset - e.Delta);
                return;
            }

            var sv = GetScrollViewer(listBox);
            if (sv == null)
                return;

            double notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;

            // ホイール奥（Delta>0）で左へ、手前で右へ。シンクロ時は ScrollChanged で他の行が追従する
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - notches * CompareRowItemsPerNotch);
        }

        // シンクロ時：どれか1行が横スクロール（ホイール・スクロールバー操作とも）したら他の行を同じ位置へ
        private void CompareRow_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // スクロール・リサイズ・表示内容の変更のいずれでもグラフの帯を追従させる
            UpdateVisibleRanges();

            if (!CompareSyncMode || _isSyncingCompareScroll || e.HorizontalChange == 0)
                return;

            if (e.OriginalSource is not ScrollViewer source)
                return;

            _isSyncingCompareScroll = true;

            try
            {
                foreach (var lb in CompareRowLists)
                {
                    var sv = GetScrollViewer(lb);

                    if (sv != null && sv != source)
                        sv.ScrollToHorizontalOffset(source.HorizontalOffset);
                }
            }
            finally
            {
                _isSyncingCompareScroll = false;
            }
        }

        private void ScrollCompareRowsToPages(double pages, bool center = false)
        {
            ScrollCompareRowsToPages(CompareRowLists, pages, center);
        }

        private void ScrollCompareRowsToPages(IEnumerable<ListBox> rows, double pages, bool center)
        {
            _isSyncingCompareScroll = true;

            try
            {
                foreach (var lb in rows)
                {
                    var sv = GetScrollViewer(lb);
                    if (sv == null || lb.DataContext is not DurabilityImageGroup group || group.Cells.Count == 0)
                        continue;

                    int index = FindNearestCellIndex(group.Cells, pages);

                    // center=true なら指定枚数が表示範囲の中央に来るようにする（単位は「件」）
                    double offset = center ? index - sv.ViewportWidth / 2 + 0.5 : index;

                    sv.ScrollToHorizontalOffset(Math.Max(0, offset));
                }
            }
            finally
            {
                _isSyncingCompareScroll = false;
            }
        }

        // 枚数順に並んだマスから、指定枚数に最も近いマスの位置を二分探索で求める
        private static int FindNearestCellIndex(List<CompareCell> cells, double pages)
        {
            int lo = 0, hi = cells.Count - 1;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if ((cells[mid].PositionPages ?? double.MaxValue) < pages)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            // lo は「pages 以上の最初のマス」。1つ手前の方が近ければそちらを採用
            if (lo > 0 &&
                cells[lo - 1].PositionPages is double prev &&
                (cells[lo].PositionPages is not double cur || pages - prev < cur - pages))
            {
                return lo - 1;
            }

            return lo;
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject obj) => FindVisualChild<ScrollViewer>(obj);

        #endregion

        #region キーボード操作

        // ←→：3件ずつ、PageUp/PageDown：1画面ずつ、Home/End：先頭・末尾
        // シンクロ時は全行、独立時はフォーカスまたはマウスのある行（無ければ全行）
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsCompareMode || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem)
                return;

            // 左の耐久ID一覧の上下移動などは妨げない
            if (e.OriginalSource is DependencyObject d && VisualTreeUtils.IsDescendantOf(d, LB_taikyu_ID))
                return;

            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown))
                return;

            foreach (var lb in GetKeyboardTargetRows())
            {
                var sv = GetScrollViewer(lb);
                if (sv == null)
                    continue;

                double page = Math.Max(1, Math.Floor(sv.ViewportWidth) - 1);

                double offset = e.Key switch
                {
                    Key.Left => sv.HorizontalOffset - CompareRowItemsPerNotch,
                    Key.Right => sv.HorizontalOffset + CompareRowItemsPerNotch,
                    Key.PageUp => sv.HorizontalOffset - page,
                    Key.PageDown => sv.HorizontalOffset + page,
                    Key.Home => 0,
                    _ => sv.ExtentWidth
                };

                sv.ScrollToHorizontalOffset(offset);
            }

            e.Handled = true;
        }

        private List<ListBox> GetKeyboardTargetRows()
        {
            var rows = CompareRowLists.ToList();

            // シンクロ時は1行動かせば ScrollChanged で全行が追従する
            if (CompareSyncMode)
                return rows.Take(1).ToList();

            var focused = rows.FirstOrDefault(lb => lb.IsKeyboardFocusWithin);
            if (focused != null)
                return new List<ListBox> { focused };

            var hovered = rows.FirstOrDefault(lb => lb.IsMouseOver);
            if (hovered != null)
                return new List<ListBox> { hovered };

            return rows;
        }

        #endregion
    }
}
