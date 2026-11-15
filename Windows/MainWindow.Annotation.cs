using SampleViewer.Models;
using SampleViewer.Repositories;
using SampleViewer.Services;
using System.IO;
using System.Windows;

namespace SampleViewer.Windows
{
    // メモ（画像・測定・耐久に付ける気づき）
    public partial class MainWindow
    {
        private static readonly string[] DefaultAnnotationCategories = { "気づき", "要因", "スキャン", "分類", "QM" };

        private AnnotationRepository? _annotationRepo;
        private AnnotationIndex _annotations = new(Array.Empty<Annotation>());
        private List<string> _annotationCategories = DefaultAnnotationCategories.ToList();

        // 最後に選んだ画像（メモの一覧と、メモを付ける対象）
        private IconItem? _selectedIcon;

        private void InitializeAnnotations()
        {
            try
            {
                // カテゴリはアプリのフォルダの設定ファイルで増減できる
                var categories = SettingsService.Load<List<string>>(
                    Path.Combine(settings.Local!.ExeSettingsDir, "annotationCategories.json"));

                if (categories.Any(x => !string.IsNullOrWhiteSpace(x)))
                    _annotationCategories = categories.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

                _annotationRepo = new AnnotationRepository(settings.Local.AnnotationDbPath);
                _annotations = new AnnotationIndex(_annotationRepo.GetAll());
            }
            catch (Exception ex)
            {
                // メモが使えなくても、閲覧はできるようにする
                _annotationRepo = null;
                ShowStatus($"メモを読み込めませんでした：{ex.Message}");
            }

            UpdateMemoPanel();
        }

        #region 表示への反映

        // 画像に、その画像と測定に付いたメモの件数と内容を付ける（検索は裏でも動くので、画面には触れない）
        private static void ApplyMemos(IEnumerable<IconItem> icons, AnnotationIndex annotations)
        {
            foreach (var icon in icons)
            {
                var memos = long.TryParse(icon.ImageId, out var imageId)
                    ? annotations.ForImage(imageId, icon.MeasurementId)
                    : new List<Annotation>();

                icon.MemoCount = memos.Count;
                icon.MemoToolTip = string.Join("\n", memos.Select(x => x.Summary));
            }
        }

        // 耐久の見出し（通常表示・比較行）に、耐久そのものに付いたメモを付ける
        private void ApplyMemos(DurabilityImageGroup group)
        {
            var memos = _annotations.DurabilityLevel(group.DurabilityId);

            group.MemoCount = memos.Count;
            group.MemoToolTip = string.Join("\n", memos.Select(x => x.Summary));
        }

        // 左の「メモ」欄：選択中の画像に関係するメモ（画像・測定・耐久）
        private void UpdateMemoPanel()
        {
            var memos = new List<Annotation>();

            if (_selectedIcon != null)
            {
                if (long.TryParse(_selectedIcon.ImageId, out var imageId))
                    memos.AddRange(_annotations.ForImage(imageId, _selectedIcon.MeasurementId));

                memos.AddRange(_annotations.DurabilityLevel(_selectedIcon.DurabilityId));
            }

            IC_Memos.ItemsSource = memos;
            BT_AddMemo.IsEnabled = _selectedIcon != null && _annotationRepo != null;
            TB_NoMemo.Text = _selectedIcon == null ? "画像を選ぶとメモが表示されます" : "メモはありません";
            TB_NoMemo.Visibility = memos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // メモを変更したあと、表示中の画像・見出し・グラフに反映する（並びやスクロール位置は変えない）
        private void RefreshAfterAnnotationChange()
        {
            if (_annotationRepo == null)
                return;

            _annotations = new AnnotationIndex(_annotationRepo.GetAll());

            // 「メモあり」で絞り込んでいるときは、対象が変わるので検索し直す
            if (CB_MemoOnly.IsChecked == true)
            {
                UpdateSearchResult(CollectSearchConditionsFromUI());
            }
            else
            {
                ApplyMemos(IconItems, _annotations);

                foreach (var group in DurabilityImageGroups)
                    ApplyMemos(group);

                UpdateNormalHeader();
                RefreshTrendGraph();
            }

            UpdateMemoPanel();
        }

        #endregion

        #region 追加・編集・削除

        private void AddMemo_MenuClick(object sender, RoutedEventArgs e)
        {
            var icon = GetSelectedIcons().FirstOrDefault() ?? _selectedIcon;

            if (icon != null)
                AddMemo(icon);
        }

        private void AddMemoForSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIcon != null)
                AddMemo(_selectedIcon);
        }

        private void AddMemo(IconItem icon)
        {
            if (_annotationRepo == null)
            {
                ShowStatus("メモの保存先を開けないため、メモを付けられません");
                return;
            }

            var dialog = new AnnotationWindow(icon, _annotationCategories) { Owner = this };

            if (dialog.ShowDialog() == true && dialog.Result != null)
                SaveAnnotation(dialog.Result, "メモを保存しました");
        }

        private void EditMemo_Click(object sender, RoutedEventArgs e)
        {
            var annotation = FindAnnotation(sender);

            if (annotation == null || !annotation.IsMine)
                return;

            var dialog = new AnnotationWindow(annotation, _annotationCategories) { Owner = this };

            if (dialog.ShowDialog() == true && dialog.Result != null)
                SaveAnnotation(dialog.Result, "メモを更新しました");
        }

        private void DeleteMemo_Click(object sender, RoutedEventArgs e)
        {
            var annotation = FindAnnotation(sender);

            if (annotation == null || !annotation.IsMine)
                return;

            var answer = MessageBox.Show(
                $"このメモを削除します。よろしいですか？\n\n{annotation.Summary}",
                "メモの削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                _annotationRepo!.Delete(annotation.Id);
                ShowStatus("メモを削除しました");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"メモを削除できませんでした。\n{ex.Message}");
            }

            RefreshAfterAnnotationChange();
        }

        private Annotation? FindAnnotation(object sender)
        {
            string id = (sender as FrameworkElement)?.Tag as string ?? "";
            return _annotations.All.FirstOrDefault(x => x.Id == id);
        }

        private void SaveAnnotation(Annotation annotation, string message)
        {
            try
            {
                _annotationRepo!.Save(annotation);
                ShowStatus(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"メモを保存できませんでした。\n{ex.Message}");
            }

            RefreshAfterAnnotationChange();
        }

        private void MemoOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _isUpdating)
                return;

            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        #endregion
    }
}
