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
    public partial class MainWindow : Window
    {
        #region フィールド・共通プロパティ

        private bool _isDownloadingIcons;
        private bool _isDownloadingImages;
        private CompareImageWatcher? _imageWatcher;
        private CompareSession? _compareSession;
        private Settings settings;
        private SystemStatus systemStatus;
        private bool _isUpdating = false;
        private List<(ComboBox Combo, string FieldName)> searchConditionControls;
        private CompareWindow? _compare;
        public ObservableCollection<IconItem> IconItems { get; set; }
        public ObservableCollection<CompareTargetItem> CompareTargets { get; set; } = new();
        public ObservableCollection<DurabilityCompareTarget> DurabilityCompareTargets { get; set; } = new();
        public ObservableCollection<DurabilityImageGroup> DurabilityImageGroups{ get; set; } = new();
        private string? CurrentBrowsingDurabilityId => (LB_taikyu_ID.SelectedItem as DurabilityDisplayItem)?.Id;
        private bool IsCompareMode => DurabilityCompareTargets.Count > 0;

        #endregion

        #region 初期化・終了処理

        public MainWindow(Settings _settings,SystemStatus systemStatus)
        {
            InitializeComponent();       
            settings = _settings;
            this.systemStatus = systemStatus;
            DbHelper.Init(settings.Local!.LocalDbPath);
            InitializeAnnotations();
            IconItems = new ObservableCollection<IconItem>();
            this.DataContext = this;

            //タイトルのver設定
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"SampleViewer ver.{version}";

            // 検索のコンボボックスと、検索する項目の対応
            searchConditionControls = new()
            {
                (CB_maker, SearchFields.Maker),
                (CB_model, SearchFields.Model),
                (CB_env, SearchFields.Env),
                (CB_SamplePatternTop, SearchFields.Pattern),
                (CB_colorTop, SearchFields.Color)
            };
            RefreshSearchControls();
            SetUserSettingsToUI();

            InitializeDeveloperModeUI();
            InitializeInfoLabels();
            UpdateSearchResult(CollectSearchConditionsFromUI());
            UpdateDisplayMode();
            InitializeTrendMeasurements();

            UpdateFeatureList(selectFirst: false);

            RestoreViewState();

            _isUpdating = true;　//レンダリング時のコンボボックスでRefreshイベント発火を防ぐ。ContentRenderedイベントでフラグOFF
            Closing += MainWindow_Closing;
        }

        private void SetUserSettingsToUI()
        {
            MenuLargeImage.IsChecked = settings.User!.IsUseLargeImage;
            MenuOOriginalImage.IsChecked = !settings.User.IsUseLargeImage;

            //モードの設定
            MenuBoxMode.IsChecked = false;
            MenuQueryMode.IsChecked = false;

            switch (settings.User.AccessMode)
            {
                case ImageAccessMode.Box:
                    MenuBoxMode.IsChecked = true;
                    break;

                case ImageAccessMode.Query:
                    MenuQueryMode.IsChecked = true;
                    break;
            }
            
            MenuBoxMode.IsEnabled = systemStatus.BoxAvailable;
            MenuQueryMode.IsEnabled = systemStatus.QueryAvailable;

            UpdateLocalImageMenu();

            //検索上限数の設定
            MenuLimit1000.IsChecked = false;
            MenuLimit10000.IsChecked = false;
            MenuLimit100000.IsChecked = false;
            MenuLimitUnlimited.IsChecked = false;

            switch (settings.User.SearchLimit)
            {
                case 1000:
                    MenuLimit1000.IsChecked = true;
                    break;
                case 10000:
                    MenuLimit10000.IsChecked = true;
                    break;
                case 100000:
                    MenuLimit100000.IsChecked = true;
                    break;

                default:
                    MenuLimitUnlimited.IsChecked = true;
                    break;
            }

            IsOpenLeftPane.IsChecked = settings.User.IsLeftPaneOpen;
            IsOpenRightPane.IsChecked = settings.User.IsRightPaneOpen;

            RefreshPaneState();
            UpdateMenuCaptions();
        }

        private void InitializeDeveloperModeUI()
        {
#if DEBUG
            MenuDeveloper.Visibility = Visibility.Visible;
            MenuDeveloperMode.IsCheckable = true;
            MenuDeveloperMode.IsChecked = settings.IsDeveloperMode;

#else
            MenuDeveloper.Visibility = Visibility.Collapsed;
#endif
        }

        // 画像の下に表示する項目（0～3個、追加した順）
        private const int MaxInfoLabels = 3;
        private readonly ObservableCollection<string> _infoLabels = new();
        private List<string> _infoLabelFields = new();

        private void InitializeInfoLabels()
        {
            _infoLabelFields = DisplayFieldRepository.InfoLabelFields
                .Select(x => x.DisplayName)
                .ToList();

            var saved = settings.User!.InfoLabels ?? _infoLabelFields;

            foreach (var name in saved.Where(_infoLabelFields.Contains).Distinct().Take(MaxInfoLabels))
                _infoLabels.Add(name);

            IC_InfoLabels.ItemsSource = _infoLabels;
            ApplyInfoLabels();
        }

        private string GetInfoLabel(int index) => index < _infoLabels.Count ? _infoLabels[index] : "";

        // 使わない行は高さごと消して、その分だけ画像を詰めて並べる
        private void ApplyInfoLabels()
        {
            for (int i = 0; i < MaxInfoLabels; i++)
                Resources[$"InfoLabel{i + 1}Visibility"] = i < _infoLabels.Count ? Visibility.Visible : Visibility.Collapsed;

            TB_NoInfoLabel.Visibility = _infoLabels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BT_AddInfoLabel.Visibility =
                _infoLabels.Count < MaxInfoLabels && _infoLabelFields.Except(_infoLabels).Any()
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            settings.User!.InfoLabels = _infoLabels.ToList();
        }

        private void AddInfoLabel_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = BT_AddInfoLabel, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

            foreach (var name in _infoLabelFields.Except(_infoLabels))
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => ChangeInfoLabels(() => _infoLabels.Add(name));
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void RemoveInfoLabel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string name)
                ChangeInfoLabels(() => _infoLabels.Remove(name));
        }

        private void ChangeInfoLabels(Action change)
        {
            change();
            ApplyInfoLabels();
            UpdateSearchResult(CollectSearchConditionsFromUI());
            UpdateAutoIconSize();
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            _isUpdating = false;
        }

        private void UpdateMenuCaptions()
        {
            MenuImageSize.Header =$"画像サイズ ({(settings.User.IsUseLargeImage ? "JPG" : "PNG")})";

            MenuDownloadMode.Header =$"ダウンロード方式 ({settings.User.AccessMode})";

            string limitText = settings.User.SearchLimit >= 999999999 ? "無制限" : settings.User.SearchLimit.ToString("N0");

            MenuSearchLimit.Header = $"検索上限 ({limitText})";
        }

        private void MainWindow_Closing( object? sender,System.ComponentModel.CancelEventArgs e)
        {
            if (!_isDownloadingIcons)
            {
                return;
            }

            var result =
                MessageBox.Show(
                    "現在アイコンをダウンロード中です。\n\n終了しますか？",
                    "確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void ApplyUserSettings()
        {
            SetUserSettingsToUI();
            SaveUserSettings();
        }

        private void SaveUserSettings()
        {
            SettingsService.Save(settings.Local.UserSettingsPath, settings.User);
        }

        #endregion

        #region ペイン操作

        private void RefreshPaneState()
        {
            var z = new GridLength(0);
            LeftCol.Width = settings.User.IsLeftPaneOpen ? new GridLength(Math.Max(settings.User.LeftPaneWidth, 180)) : z;
            RightCol.Width = settings.User.IsRightPaneOpen ? new GridLength(Math.Max(settings.User.RightPaneWidth, 300)) : z;

            Button_OpenLeftPane.Visibility = settings.User.IsLeftPaneOpen ? Visibility.Hidden : Visibility.Visible;
            Button_OpenRightPane.Visibility = settings.User.IsRightPaneOpen ? Visibility.Hidden : Visibility.Visible;
        }

        private void ChangeLeftPane_Click(object sender, RoutedEventArgs e)
        {
            CapturePaneWidths();
            settings.User.IsLeftPaneOpen = !settings.User.IsLeftPaneOpen;
            ApplyUserSettings();
        }

        private void ChangeRightPane_Click(object sender, RoutedEventArgs e)
        {
            CapturePaneWidths();
            settings.User.IsRightPaneOpen = !settings.User.IsRightPaneOpen;
            ApplyUserSettings();
        }

        private void ChangePane_Click(object sender, RoutedEventArgs e)
        {
            CapturePaneWidths();
            settings.User.IsLeftPaneOpen =IsOpenLeftPane.IsChecked == true;
            settings.User.IsRightPaneOpen =IsOpenRightPane.IsChecked == true;

            ApplyUserSettings();
        }

        private void InitializeParameter_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "設定を初期値に戻します。よろしいですか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // 個人の設定を既定値へ戻す（PCごとのローカル画像フォルダの設定は変えない）
            var paths = settings.Local!;

            File.Copy(paths.UserSettingsDefaultPath, paths.UserSettingsPath, true);
            settings.User = SettingsService.Load<UserSettings>(paths.UserSettingsPath);

            // UIへ反映
            SetUserSettingsToUI();

            MessageBox.Show(
                "設定を初期値へ戻しました。",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RefreshSearchControls();
        }

        #endregion

        #region 画面状態の保存・復元

        // 開いているペインの現在幅を記録（スプリッターで変えた幅を次に開くときも使う）
        private void CapturePaneWidths()
        {
            if (settings.User!.IsLeftPaneOpen && LeftCol.ActualWidth > 50)
                settings.User.LeftPaneWidth = LeftCol.ActualWidth;

            if (settings.User.IsRightPaneOpen && RightCol.ActualWidth > 50)
                settings.User.RightPaneWidth = RightCol.ActualWidth;
        }

        private void RestoreViewState()
        {
            var user = settings.User!;

            // 表示モード（CompareOption_Changed は行が無ければ何もしない）
            RB_CompareSync.IsChecked = user.CompareSyncMode;
            RB_CompareIndependent.IsChecked = !user.CompareSyncMode;
            CB_CompareCommonOnly.IsChecked = user.CompareCommonOnly;
            CB_CompareTolerance.SelectedIndex = Math.Clamp(user.CompareToleranceIndex, 0, CB_CompareTolerance.Items.Count - 1);
            CB_CompareTolerance.IsEnabled = CompareSyncMode && !CompareCommonOnly;

            // 画像サイズ
            SetIconSize(user.IconSize);
            CB_IconSizeAuto.IsChecked = user.IsIconSizeAuto;
            SL_IconSize.IsEnabled = !user.IsIconSizeAuto;

            // 検索結果に影響するため、ここでは再検索させない（このあと必要なら検索する）
            _isUpdating = true;
            CB_FirstImageOnly.IsChecked = user.IsFirstImageOnly;
            _isUpdating = false;

            // 比較中だった耐久（一覧に無いものは飛ばす）
            var available = LB_taikyu_ID.Items.OfType<DurabilityDisplayItem>().ToDictionary(x => x.Id);

            foreach (var id in user.CompareTargetIds.Take(MaxCompareTargets))
            {
                if (available.TryGetValue(id, out var item))
                {
                    DurabilityCompareTargets.Add(new DurabilityCompareTarget
                    {
                        DurabilityId = item.Id,
                        EvaluationPurpose = item.EvaluationPurpose
                    });
                }
            }

            // 前回選んでいた耐久（一覧に無いものは飛ばす）
            _isUpdating = true;

            foreach (var id in user.SelectedDurabilityIds)
            {
                if (available.TryGetValue(id, out var item))
                    LB_taikyu_ID.SelectedItems.Add(item);
            }

            _isUpdating = false;

            if (LB_taikyu_ID.SelectedItem != null)
                LB_taikyu_ID.ScrollIntoView(LB_taikyu_ID.SelectedItem);

            if (DurabilityCompareTargets.Count > 0)
                RenumberCompareSlots();

            if (DurabilityCompareTargets.Count > 0 || LB_taikyu_ID.SelectedItems.Count > 0)
                UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            var user = settings.User!;

            CapturePaneWidths();
            user.CompareSyncMode = CompareSyncMode;
            user.CompareCommonOnly = CompareCommonOnly;
            user.CompareToleranceIndex = CB_CompareTolerance.SelectedIndex;
            user.CompareTargetIds = DurabilityCompareTargets.Select(x => x.DurabilityId).ToList();
            user.SelectedDurabilityIds = LB_taikyu_ID.SelectedItems.OfType<DurabilityDisplayItem>().Select(x => x.Id).ToList();

            try
            {
                SaveUserSettings();
            }
            catch
            {
                // 終了時の保存失敗は無視（次回は既定値で起動する）
            }
        }

        #endregion

        #region キーボード（ウィンドウ全体）

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            // 処理したキーだけ Handled にする（リストの矢印キー操作などを妨げない）
            switch (e.Key)
            {
                case Key.V:
                    ShowCompareWindow();
                    break;
                case Key.R:
                    ResetCondition();
                    break;
                case Key.Escape when IsCompareMode:
                    ClearAllCompareTargets();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        #endregion

        #region 画像の選択・表示

        // 比較モードでは全行の選択を合算する（該当なしのマスは除外、近似で重複表示された画像は1件にまとめる）
        private List<IconItem> GetSelectedIcons()
        {
            return IsCompareMode
                ? CompareRowLists
                    .SelectMany(lb => lb.SelectedItems.OfType<CompareCell>())
                    .Select(c => c.Icon)
                    .OfType<IconItem>()
                    .Distinct()
                    .ToList()
                : LB_NormalIcons.SelectedItems.OfType<IconItem>().ToList();
        }

        private void ClearIconSelection()
        {
            if (IsCompareMode)
            {
                foreach (var lb in CompareRowLists)
                    lb.UnselectAll();
            }
            else
            {
                LB_NormalIcons.UnselectAll();
            }
        }

        // 画像IDに一致するアイコンを選択して表示位置までスクロールする
        private void RevealIcon(string imageId)
        {
            ClearIconSelection();

            if (!IsCompareMode)
            {
                var match = LB_NormalIcons.Items.OfType<IconItem>().FirstOrDefault(x => x.ImageId == imageId);
                if (match == null)
                    return;

                LB_NormalIcons.SelectedItem = match;
                LB_NormalIcons.ScrollIntoView(match);
                return;
            }

            foreach (var lb in CompareRowLists)
            {
                // 近似で複数マスに出ている場合は完全一致のマスを優先する
                var match = lb.Items.OfType<CompareCell>()
                    .Where(c => c.Icon?.ImageId == imageId)
                    .OrderBy(c => c.IsApproximate)
                    .FirstOrDefault();
                if (match == null)
                    continue;

                lb.SelectedItem = match;
                lb.ScrollIntoView(match);
            }
        }

        private void LB_Icons_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = sender as ListBoxItem;
            if (item == null) return;

            if (!item.IsSelected)
            {
                ClearIconSelection();
                item.IsSelected = true;
            }
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var clickedImage = sender as Image;
            var iconitem = clickedImage?.DataContext as IconItem;

            if (iconitem == null ||
                string.IsNullOrEmpty(iconitem.ImagePath))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                e.Handled = true;
                return;
            }

            ShowImageDetails(iconitem);
        }

        // ダブルクリック：この1枚を比較ウィンドウで詳しく見る
        private void IconItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var data = (sender as ListBoxItem)?.DataContext;
            var icon = data as IconItem ?? (data as CompareCell)?.Icon;

            if (icon == null)
                return;

            e.Handled = true;

            // マウス操作の処理が終わってからウィンドウを開く（途中で開くとフォーカスを奪われる）
            Dispatcher.BeginInvoke(
                () => OpenCompareWindow(CreateCompareItems(new[] { icon }, resolve: false)),
                DispatcherPriority.Background);
        }

        // 選択した画像の情報を表示し、グラフの強調表示と特徴量の候補をその画像に合わせる
        // followColor=true（アイコンを選んだとき）は、グラフの色も画像の色に合わせる
        private void ShowImageDetails(IconItem icon, bool followColor = true)
        {
            _selectedImageId = long.TryParse(icon.ImageId, out var id) ? id : null;
            _selectedMeasurementId = icon.MeasurementId;
            _selectedPattern = icon.Pattern;
            _selectedIcon = icon;
            UpdateMemoPanel();

            TB_SelectedImage.Text = $"Image : {icon.ImageName}";
            DisplayImageInformation(icon.ImageId);

            bool colorChanged = followColor && ApplyPatternColor(icon.Color);
            bool featureChanged = UpdateFeatureList(selectFirst: true);

            // 色か特徴量が変わったときだけデータを取得し直し、それ以外は強調表示の描き直しだけにする
            if (colorChanged || featureChanged)
                RefreshTrendGraph();
            else
                RedrawTrendGraphs();
        }

        private void UpdateDisplayMode()
        {
            NormalArea.Visibility = IsCompareMode ? Visibility.Collapsed : Visibility.Visible;
            CompareArea.Visibility = IsCompareMode ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region 画像サイズ

        private bool _isApplyingIconSize;

        private void IconSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsInitialized)
                return;

            SetIconSize(e.NewValue);
        }

        private void SetIconSize(double size)
        {
            size = Math.Round(Math.Clamp(size, SL_IconSize.Minimum, SL_IconSize.Maximum));

            Resources["IconSize"] = size;
            TB_IconSize.Text = size.ToString("0");
            settings.User!.IconSize = size;

            if (!_isApplyingIconSize && Math.Abs(SL_IconSize.Value - size) > 0.5)
            {
                _isApplyingIconSize = true;
                SL_IconSize.Value = size;
                _isApplyingIconSize = false;
            }
        }

        private void IconSizeAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            bool auto = CB_IconSizeAuto.IsChecked == true;
            settings.User!.IsIconSizeAuto = auto;
            SL_IconSize.IsEnabled = !auto;

            UpdateAutoIconSize();
        }

        private void CompareRows_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
                UpdateAutoIconSize();
        }

        // 1行あたりの画像以外の高さ（カードの余白・見出し・スクロールバー・選択枠＋表示項目の行）
        private double CompareRowOverhead => 80 + new[] { 19, 13, 13 }.Take(_infoLabels.Count).Sum();

        // 自動ON かつ比較モードのとき、全行が縦に収まる画像サイズにする
        private void UpdateAutoIconSize()
        {
            if (CB_IconSizeAuto.IsChecked != true || !IsCompareMode)
                return;

            int rows = DurabilityImageGroups.Count;
            double height = SV_CompareRows.ActualHeight;

            if (rows == 0 || height <= 0)
                return;

            SetIconSize(height / rows - CompareRowOverhead);
        }

        #endregion

        #region ステータスバー

        // 一時的な通知はメッセージボックスではなくステータスバーに出す
        private DispatcherTimer? _statusTimer;

        private void ShowStatus(string message)
        {
            StatusText.Text = message;

            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _statusTimer.Tick += (_, _) =>
            {
                _statusTimer?.Stop();
                if (StatusText.Text == message)
                    StatusText.Text = "Ready.";
            };
            _statusTimer.Start();
        }

        #endregion
    }
}
