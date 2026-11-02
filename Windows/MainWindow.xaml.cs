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
            IconItems = new ObservableCollection<IconItem>();
            this.DataContext = this;

            //タイトルのver設定
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"SampleViewer ver.{version}";

            searchConditionControls = CreateControlList();
            RefreshSearchControls();
            SetUserSettingsToUI();

            InitializeDeveloperModeUI();
            InitializeInfoLabels();
            UpdateSearchResult(CollectSearchConditionsFromUI());
            UpdateDisplayMode();
            CB_Measurement.ItemsSource =new[]
            {
                "Vb1","Vb2","Vb3","Vb4",
                "TN1","TN2","TN3","TN4",
                "DRM1","DRM2","DRM3","DRM4",
                "QM1","QM2","QM3","QM4",
                "MA1","MA2","MA3","MA4"
            };

            CB_Measurement.SelectedItem = "Vb1";

            UpdateFeatureList(selectFirst: false);

            RestoreViewState();

            _isUpdating = true;　//レンダリング時のコンボボックスでRefreshイベント発火を防ぐ。ContentRenderedイベントでフラグOFF
            Closing += MainWindow_Closing;
        }

        private ComboBox? FindSearchComboBox(string controlName)
        {
            return controlName switch
            {
                "CB_maker" => CB_maker,
                "CB_model" => CB_model,
                "CB_env" => CB_env,
                "CB_samplePattern" => CB_SamplePatternTop,
                "CB_color" => CB_colorTop,
                _ => null
            };
        }

        private List<(ComboBox Combo, string FieldName)> CreateControlList()
        {
            var repo =
                new DisplayFieldRepository();

            var fields =
                repo.GetSearchControls();

            var result =
                new List<(ComboBox, string)>();

            foreach (var field in fields)
            {
                var combo =
                    FindSearchComboBox(
                        field.ControlName);

                if (combo != null)
                {
                    result.Add(
                        (
                            combo,
                            field.DisplayName
                        ));
                }
            }

            return result;
        }

        private void SetUserSettingsToUI()
        {
            MenuLargeImage.IsChecked = settings.User!.IsUseLargeImage;
            MenuOOriginalImage.IsChecked = !settings.User.IsUseLargeImage;

            //モードの設定
            MenuLocalMode.IsChecked = false;
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

                case ImageAccessMode.Local:
                    MenuLocalMode.IsChecked = true;
                    break;
            }
            
            MenuLocalMode.IsEnabled = settings.IsImagePc;
            MenuBoxMode.IsEnabled = !settings.IsImagePc && systemStatus.BoxAvailable;
            MenuQueryMode.IsEnabled = !settings.IsImagePc && systemStatus.QueryAvailable;

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

        private void InitializeInfoLabels()
        {
            _isUpdating = true;

            try
            {
                CB_InfoLabel1.Items.Clear();
                CB_InfoLabel2.Items.Clear();
                CB_InfoLabel3.Items.Clear();

                var repo =
                    new DisplayFieldRepository();

                var fields =
                    repo.GetInfoLabelFields();

                foreach (var field in fields)
                {
                    CB_InfoLabel1.Items.Add(field.DisplayName);
                    CB_InfoLabel2.Items.Add(field.DisplayName);
                    CB_InfoLabel3.Items.Add(field.DisplayName);
                }

                if (CB_InfoLabel1.Items.Count > 0)
                    CB_InfoLabel1.SelectedIndex = 0;

                if (CB_InfoLabel2.Items.Count > 1)
                    CB_InfoLabel2.SelectedIndex = 1;

                if (CB_InfoLabel3.Items.Count > 2)
                    CB_InfoLabel3.SelectedIndex = 2;
            }
            finally
            {
                _isUpdating = false;
            }
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

            var paths = PathFactory.Create();
            var exeDir = paths.ExeSettingsDir;

            // 設定ファイルを初期値へ戻す
            File.Copy(Path.Combine(exeDir, "serverSettings.json"),paths.ServerSettingsPath,true);

            File.Copy(Path.Combine(exeDir, "userSettingsDefault.json"),paths.UserSettingsPath,true);

            // 再読み込み
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

            if (DurabilityCompareTargets.Count > 0)
            {
                RenumberCompareSlots();
                UpdateSearchResult(CollectSearchConditionsFromUI());
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            var user = settings.User!;

            CapturePaneWidths();
            user.CompareSyncMode = CompareSyncMode;
            user.CompareCommonOnly = CompareCommonOnly;
            user.CompareToleranceIndex = CB_CompareTolerance.SelectedIndex;
            user.CompareTargetIds = DurabilityCompareTargets.Select(x => x.DurabilityId).ToList();

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

        // 選択した画像の情報を表示し、グラフの強調表示と特徴量の候補をその画像に合わせる
        private void ShowImageDetails(IconItem icon)
        {
            _selectedImageId = long.TryParse(icon.ImageId, out var id) ? id : null;
            _selectedMeasurementId = icon.MeasurementId;
            _selectedPattern = icon.Pattern;

            TB_SelectedImage.Text = $"Image : {icon.ImageName}";
            DisplayImageInformation(icon.ImageId);

            // 特徴量が変わったときだけデータを取得し直し、それ以外は強調表示の描き直しだけにする
            if (UpdateFeatureList(selectFirst: true))
                RefreshTrendGraph();
            else
                RedrawTrendGraphs();
        }

        private void DisplayImageInformation(string imageID)
        {
            var values = SearchService.GetImageInfo( imageID);

            LB_ImageInfo.ItemsSource = values;
        }

        private void UpdateDisplayMode()
        {
            LB_NormalIcons.Visibility = IsCompareMode ? Visibility.Collapsed : Visibility.Visible;
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

        // 1行あたりの画像以外の高さ（カードの余白・見出し・表示項目3行・スクロールバー・選択枠）
        private const double CompareRowOverhead = 125;

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
