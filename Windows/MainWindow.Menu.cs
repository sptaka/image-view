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
        #region 設定メニュー

        private void MenuImageSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            settings.User.IsUseLargeImage = item.Tag?.ToString() == "jpg";
            ApplyUserSettings();
        }

        private void MenuSearchLimit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;

            if (!int.TryParse(item.Tag?.ToString(),out int limit))
            {
                return;
            }

            settings.User.SearchLimit = limit;

            ApplyUserSettings();

            UpdateSearchResult(CollectSearchConditionsFromUI());
        }

        private void MenuDownloadMode_click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item)
                return;

            if (Enum.TryParse<ImageAccessMode>(
            item.Tag?.ToString(),
            out var mode))
            {
                settings.User.AccessMode = mode;

                ApplyUserSettings();
            }
        }

        private void DeveloperMode_click(object sender, RoutedEventArgs e)
        {
#if DEBUG

            if (MenuDeveloperMode.IsChecked)
            {
                DeveloperModeService.Enable(
                settings.Local!.BaseDir);
            }
            else
            {
                DeveloperModeService.Disable(
                settings.Local!.BaseDir);
            }

            MessageBox.Show(
            "次回起動時から反映されます。");

#endif
        }

        #region ローカルの画像（PCごとの設定）

        private void UpdateLocalImageMenu()
        {
            var machine = settings.Machine;
            bool hasFolder = !string.IsNullOrWhiteSpace(machine.LocalLargeImagePath) ||
                             !string.IsNullOrWhiteSpace(machine.LocalOriginalImagePath);

            MenuUseLocal.IsChecked = settings.User!.UseLocalImages;
            MenuLocalLarge.Header = $"中画像（jpg）のフォルダ：{DescribeLocalFolder(machine.LocalLargeImagePath)}";
            MenuLocalOriginal.Header = $"元画像（png）のフォルダ：{DescribeLocalFolder(machine.LocalOriginalImagePath)}";
            MenuLocalImages.Header = !hasFolder
                ? "ローカルの画像 (未設定)"
                : settings.User.UseLocalImages ? "ローカルの画像 (使用中)" : "ローカルの画像 (オフ)";
        }

        private static string DescribeLocalFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return "未設定…";

            return Directory.Exists(folder) ? folder : $"{folder}（見つかりません）";
        }

        private string GetLocalImageStatus()
        {
            string Describe(string? folder) =>
                string.IsNullOrWhiteSpace(folder) ? "未設定" : Directory.Exists(folder) ? "OK" : "NG（フォルダなし）";

            return settings.User!.UseLocalImages
                ? $"中画像 {Describe(settings.Machine.LocalLargeImagePath)} / 元画像 {Describe(settings.Machine.LocalOriginalImagePath)}"
                : "オフ";
        }

        private void MenuUseLocal_Click(object sender, RoutedEventArgs e)
        {
            settings.User!.UseLocalImages = MenuUseLocal.IsChecked;
            ApplyUserSettings();
            ShowStatus(settings.User.UseLocalImages
                ? "このPCにある画像を優先して使います"
                : $"ローカルの画像を使わず、{settings.User.AccessMode} で取得します");
        }

        // 耐久IDのフォルダが並んでいるフォルダを選ぶ（例：60_LargeJpg）
        private void MenuLocalFolder_Click(object sender, RoutedEventArgs e)
        {
            bool isLarge = (sender as MenuItem)?.Tag as string == "Large";
            string? current = isLarge ? settings.Machine.LocalLargeImagePath : settings.Machine.LocalOriginalImagePath;

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = isLarge
                    ? "中画像（jpg）のフォルダを選択（この下に耐久IDのフォルダがある場所）"
                    : "元画像（png）のフォルダを選択（この下に耐久IDのフォルダがある場所）"
            };

            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) != true)
                return;

            SetLocalImageFolder(isLarge, dialog.FolderName);
        }

        private void MenuClearLocalFolders_Click(object sender, RoutedEventArgs e)
        {
            settings.Machine.LocalLargeImagePath = null;
            settings.Machine.LocalOriginalImagePath = null;
            SaveMachineSettings();
        }

        private void SetLocalImageFolder(bool isLarge, string folder)
        {
            if (isLarge)
                settings.Machine.LocalLargeImagePath = folder;
            else
                settings.Machine.LocalOriginalImagePath = folder;

            SaveMachineSettings();
        }

        // PCの全ユーザーに共通の設定として保存する
        private void SaveMachineSettings()
        {
            try
            {
                SettingsService.Save(settings.Local!.MachineSettingsPath, settings.Machine);
                ShowStatus("ローカルの画像フォルダの設定を保存しました（このPCの全ユーザーに共通）");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定を保存できませんでした。\r\n{ex.Message}");
            }

            UpdateLocalImageMenu();
        }

        #endregion

        private void ShowNetworkStatus_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                $"Developer : {(settings.IsDeveloperMode ? "ON" : "OFF")}\r\n" +
                $"Server : {(systemStatus.ServerAvailable ? "OK" : "NG")}\r\n" +
                $"Query : {(systemStatus.QueryAvailable ? "OK" : "NG")}\r\n" +
                $"Box : {(systemStatus.BoxAvailable ? "OK" : "NG")}\r\n" +
                $"Mode : {settings.User!.AccessMode}\r\n" +
                $"Local : {GetLocalImageStatus()}"
            );
        }

        private async void RefreshNetworkStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var initializer = new AppInitializer();

                systemStatus =
                    await initializer.CheckNetworkAsync(
                        settings,
                        new Progress<ProgressInfo>());

                AppInitializer.ApplyAccessModeFallback(settings, systemStatus);

                SetUserSettingsToUI();

                MessageBox.Show(
                    $"通信状態を更新しました。\r\n\r\n" +
                    $"Server : {(systemStatus.ServerAvailable ? "OK" : "NG")}\r\n" +
                    $"Query  : {(systemStatus.QueryAvailable ? "OK" : "NG")}\r\n" +
                    $"Box    : {(systemStatus.BoxAvailable ? "OK" : "NG")}\r\n" +
                    $"Mode   : {settings.User!.AccessMode}",
                    "通信状態更新");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "通信状態更新エラー");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        #endregion

        #region フォルダ・DBを開く

        // DB Browser for SQLite を探す。見つからなければ、.db に関連付けられたアプリで開く
        private static string? FindDbBrowser()
        {
            const string exe = @"DB Browser for SQLite\DB Browser for SQLite.exe";

            return new[]
                {
                    Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86,
                    Environment.SpecialFolder.LocalApplicationData
                }
                .Select(x => Path.Combine(Environment.GetFolderPath(x), exe))
                .FirstOrDefault(File.Exists);
        }

        private static void OpenWithDbBrowser(string? dbFilePath)
        {
            if (!File.Exists(dbFilePath))
            {
                MessageBox.Show("DBファイルが見つかりません: " + dbFilePath);
                return;
            }

            try
            {
                string? browser = FindDbBrowser();

                Process.Start(browser != null
                    ? new ProcessStartInfo { FileName = browser, Arguments = $"\"{dbFilePath}\"", UseShellExecute = false }
                    : new ProcessStartInfo { FileName = dbFilePath, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(
                    "DBを開けませんでした。DB Browser for SQLite がインストールされているか確認してください。\r\n" +
                    dbFilePath);
            }
        }

        private static void OpenFolder(string? folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("フォルダが存在しません: " + folderPath);
            }
        }

        private void OpenLocalDBbyDbBrowser(object sender, RoutedEventArgs e)
        {
            OpenWithDbBrowser(settings.Local!.LocalDbPath);
        }

        private void OpenSvrDBbyDbBrowser(object sender, RoutedEventArgs e)
        {
            OpenWithDbBrowser(settings.Server!.serverDatabasePath);
        }

        private void OpenSvrIconCatalogbyDbBrowser(object sender, RoutedEventArgs e)
        {
            OpenWithDbBrowser(settings.Server!.serverIconCatalogPath);
        }

        private void OpenLocalFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(settings.Local!.IconsDir);
        }

        private void OpenQueryFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(settings.Server!.serverQueryPath);
        }

        private void OpenResponseFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(settings.Server!.serverResponsePath);
        }

        #endregion

        #region ヘルプ・バージョン

        private void ShowHelpWindow(object sender, RoutedEventArgs e)
        {
            var txtPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "readme.txt");
            Process.Start(new ProcessStartInfo(txtPath) { UseShellExecute = true });
        }     

        private void ShowVersionInfo_Click(object sender, RoutedEventArgs e)
        {
            string appVersion = VersionComparer.GetAppVersion();

            string dbVersion =AppInitializer.GetDatabaseVersion(settings.Local!.LocalDbPath);

            string templateVersion =File.Exists(Path.Combine(settings.Local!.IconsTemplateDir, "version.txt"))
                ? File.ReadAllText(Path.Combine( settings.Local!.IconsTemplateDir, "version.txt")).Trim()
                : "Unknown";

            MessageBox.Show(
            $"SampleViewer\n\n" +
            $"App Version : {appVersion}\n" +
            $"DB Version : {dbVersion}\n" +
            $"Template Version : {templateVersion}",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        }

        private void InstallBoxDrive_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo{
                FileName =　settings.Server.BoxDriveInstallUrl,UseShellExecute = true});
        }

        #endregion

        #region アイコン管理

        private async void ManageIcons_Click(object sender,RoutedEventArgs e)
        {
            var window = new UpdateConfirmationWindow(settings);

            window.Owner = this;

            if (window.ShowDialog() != true)
            {
                return;
            }

            if (window.SelectedDurabilityIds.Count == 0)
            {
                return;
            }

            var updater = new IconUpdateService();

            try
            {
                _isDownloadingIcons = true;

                StatusText.Text = $"アイコンをダウンロード中... ({window.SelectedDurabilityIds.Count}件)";
                var progress = new Progress<ProgressInfo>(p =>{
                    StatusText.Text =p.StatusMessage;
                    DownloadProgress.Value = p.Percent;
                });
                await updater.UpdateIconsAsync(window.SelectedDurabilityIds, settings,progress);

                var localService = new IconLocalStateService(settings.Local!.IconStateDbPath);

                settings.InstalledIconIds = localService.GetAll()
                        .Where(x => !string.IsNullOrEmpty(x.Value.LocalVersion))
                        .Select(x => x.Key)
                        .ToHashSet();

                UpdateSearchResult(CollectSearchConditionsFromUI());
            }
            finally
            {
                _isDownloadingIcons = false;

                StatusText.Text = "アイコン更新完了";
                DownloadProgress.Value = 0;
            }
        }

        #endregion

        #region 画像ダウンロード

        private async void DownloadImages_MenuClick(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingImages && settings.User!.AccessMode == ImageAccessMode.Query)
            {
                MessageBox.Show("クエリ方式では、前回のダウンロードが未完了状態での追加ダウンロードは出来ません。");
                return;
            }
            if (settings.User!.AccessMode == ImageAccessMode.Query)
            {
                _isDownloadingImages = true;
            }
            try
            {
                var selectedItems = GetSelectedIcons();

                if (selectedItems.Count == 0)
                    return;

                StatusText.Text = $"画像ダウンロード中... (0/{selectedItems.Count})";

                string downloadDir =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        "Downloads");

                Directory.CreateDirectory(downloadDir);

                // ローカル・Box にある画像はそのままコピーし、残りをクエリで要求する
                int totalCount = selectedItems.Count;
                var pathService = new ImagePathService(settings);
                var queryItems = new List<IconItem>();

                for (int index = 0; index < totalCount; index++)
                {
                    var icon = selectedItems[index];
                    var (sourcePath, isDirect) = pathService.Resolve(icon.DurabilityId, icon.ImageName);

                    if (!isDirect)
                    {
                        queryItems.Add(icon);
                        continue;
                    }

                    StatusText.Text = $"画像ダウンロード中... ({index + 1}/{totalCount})";
                    DownloadProgress.Value = (index + 1) * 100.0 / totalCount;
                    await Dispatcher.Yield( DispatcherPriority.Background);

                    CopyToDownloads(sourcePath, downloadDir, icon);
                }

                if (queryItems.Count == 0)
                {
                    DownloadProgress.Value = 0;
                    StatusText.Text =$"画像ダウンロード完了 ({totalCount}件)";
                    return;
                }

                selectedItems = queryItems;

                // Query
                ClearResponseFolder();

                var compareItems = CreateCompareItems(selectedItems);

                var downloader = new ImageDownloadService(settings);

                downloader.RequestImages(compareItems);

                for (int index = 0; index < selectedItems.Count; index++)
                {
                    var icon = selectedItems[index];
                    StatusText.Text = $"画像ダウンロード中... ({index + 1}/{selectedItems.Count})";
                    DownloadProgress.Value = (index + 1) * 100.0 / selectedItems.Count;
                    await Dispatcher.Yield( DispatcherPriority.Background);

                    string responsePath = Path.Combine(
                            settings.Server!.serverResponsePath!,
                            icon.ImageName + ImageExtension);

                    for (int i = 0; i < 100; i++)
                    {
                        if (File.Exists(responsePath))
                            break;

                        await Task.Delay(100);
                    }

                    if (File.Exists(responsePath))
                    {
                        CopyToDownloads(responsePath, downloadDir, icon);
                    }
                }
                ClearResponseFolder();
                DownloadProgress.Value = 0;
                StatusText.Text = $"画像ダウンロード完了 ({totalCount}件)";
            }
            finally
            {
                _isDownloadingImages = false;
            }
        }

        private string ImageExtension => settings.User!.IsUseLargeImage ? ".jpg" : ".png";

        private string CreateDownloadFileName(IconItem icon)
        {
            return $"{icon.DurabilityId}_{icon.ImageName}{ImageExtension}";
        }

        // ダウンロードフォルダへコピーし、日時を現在にする（エクスプローラーで新しい順に並ぶように）
        private void CopyToDownloads(string sourcePath, string downloadDir, IconItem icon)
        {
            string destPath = Path.Combine(downloadDir, CreateDownloadFileName(icon));
            File.Copy(sourcePath, destPath, true);

            var now = DateTime.Now;
            File.SetLastWriteTime(destPath, now);
            File.SetCreationTime(destPath, now);
        }

        #endregion

        #region 比較ウィンドウ

        private void CompareSelectedImages_MenuClick(object sender, RoutedEventArgs e)
        {

            ShowCompareWindow();
        }

        // 選択中の画像を比較ウィンドウで開く
        private void ShowCompareWindow()
        {
            OpenCompareWindow(CreateCompareItems(GetSelectedIcons(), resolve: false));
        }

        // 比較ウィンドウを開き直す
        // ウィンドウと枠を先に出し、画像の読み込み元の確認（休止中のドライブや Box だと数秒かかることがある）は裏で行う
        private async void OpenCompareWindow(List<CompareItem> items, string? title = null)
        {
            if (items.Count == 0)
                return;

            _compare?.Close();

            var window = new CompareWindow
            {
                Owner = this,
                SeriesProvider = GetCompareSeries,
                LoadRequested = LoadSteppedCompareItems
            };

            if (title != null)
                window.Title = title;

            bool closed = false;
            window.Closed += (_, _) => closed = true;

            _compare = window;
            _compareSession = null;

            window.CreatePlaceholders(items);
            window.Show();
            window.Activate();

            try
            {
                await Task.Run(() =>
                {
                    ClearResponseFolder();
                    ResolveCompareSources(items);
                });
            }
            catch (Exception ex)
            {
                if (!closed)
                {
                    foreach (var item in items)
                        window.ShowImageError(item.Key, ex.Message);
                }

                return;
            }

            // 待っている間に閉じられた・別の比較が開かれた
            if (closed || _compare != window)
                return;

            // 直接読める画像（ローカル・Box）はすぐ読み込み、残りだけクエリで要求する
            foreach (var item in items.Where(x => x.IsDirect))
                _ = window.SetImageAsync(item.Key, item.SourcePath!);

            var remote = items.Where(x => !x.IsDirect).ToList();

            if (remote.Count == 0)
                return;

            window.IsRemoteLoading = true;

            if (_isDownloadingImages)
            {
                foreach (var item in remote)
                    window.ShowImageError(item.Key, "クエリ方式では、画像ダウンロード中は比較表示ができません");

                return;
            }

            StartCompareWatcher();
            _compareSession = new ImageDownloadService(settings).RequestImages(remote);
        }

        // 画像の読み込み元を決める（ファイルの有無を調べるので、裏で呼ぶ）
        private void ResolveCompareSources(IEnumerable<CompareItem> items)
        {
            var imagePathService = new ImagePathService(settings);

            foreach (var item in items)
            {
                var source = imagePathService.Resolve(item.DurabilityId, item.DisplayName);

                item.SourcePath = source.Path;
                item.IsDirect = source.IsDirect;
            }
        }

        private List<CompareItem> CreateCompareItems(IEnumerable<IconItem> selectedItems, bool resolve = true)
        {
            return CreateCompareItems(selectedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.ImagePath))
                .Select(x => (x.DurabilityId, x.ImageName, x.Pages, x.Pattern)), resolve);
        }

        // resolve: false は読み込み元を決めずに返す（比較ウィンドウを開くときは、開いたあと裏で決める）
        private List<CompareItem> CreateCompareItems(
            IEnumerable<(string DurabilityId, string ImageName, double? Pages, string Pattern)> images,
            bool resolve = true)
        {
            var list = images.ToList();
            var infos = SearchService.GetDurabilityInfo(list.Select(x => x.DurabilityId).Distinct());

            var items = list
                .Select(x =>
                {
                    var info = infos.GetValueOrDefault(x.DurabilityId);

                    return new CompareItem
                    {
                        Key = Guid.NewGuid().ToString(),
                        DisplayName = x.ImageName,
                        DurabilityId = x.DurabilityId,
                        Pages = x.Pages,
                        Pattern = x.Pattern,
                        Env = info.Env ?? "",
                        Model = info.Model ?? "",
                        Purpose = info.Purpose ?? ""
                    };
                })
                .ToList();

            if (resolve)
                ResolveCompareSources(items);

            return items;
        }

        private void ClearResponseFolder()
        {
            try
            {
                foreach (var file in Directory.GetFiles(
                    settings.Server.serverResponsePath))
                {
                    var ext =
                        Path.GetExtension(file)
                            .ToLowerInvariant();

                    if (ext != ".jpg" &&
                        ext != ".png")
                    {
                        continue;
                    }

                    File.Delete(file);
                }
            }
            catch
            {
                // 無視
            }
        }

        private void ShowMura_MenuClick(object sender, RoutedEventArgs e)
        {
            var icon = GetSelectedIcons().FirstOrDefault();

            if (icon != null)
                OpenMuraWindow(icon);
        }

        private void OpenMuraWindow(IconItem icon)
        {
            var pathService = new ImagePathService(settings);
            string durabilityId = icon.DurabilityId;

            new MuraWindow(
                durabilityId,
                icon.Pattern,
                icon.Color,
                SearchService.GetPatternSeries(durabilityId, icon.Pattern),
                name => Path.Combine(settings.IconRootPath!, durabilityId, name + ".jpg"),
                settings.User!.AccessMode == ImageAccessMode.Query && pathService.LocalFolder == null
                    ? null
                    : name => pathService.GetSourcePath(durabilityId, name))
            {
                Owner = this
            }.Show();
        }

        // 枚数送り：その画像と同じ耐久・同じパターンの画像を枚数順に返す
        private IReadOnlyList<CompareItem> GetCompareSeries(CompareItem item)
        {
            return CreateCompareItems(
                SearchService.GetPatternSeries(item.DurabilityId, item.Pattern)
                    .Select(x => (item.DurabilityId, x.ImageName, (double?)x.Pages, item.Pattern)));
        }

        // 枚数送りで移動した先の画像を読み込む
        private void LoadSteppedCompareItems(IReadOnlyList<CompareItem> items)
        {
            // 直接読める画像（ローカル・Box）はすぐ読み込む
            foreach (var item in items.Where(x => x.IsDirect))
            {
                if (File.Exists(item.SourcePath))
                    _ = _compare?.SetImageAsync(item.Key, item.SourcePath!);
                else
                    _compare?.ShowImageError(item.Key, "画像がありません");
            }

            items = items.Where(x => !x.IsDirect).ToList();

            if (items.Count == 0)
                return;

            if (_isDownloadingImages)
            {
                foreach (var item in items)
                    _compare?.ShowImageError(item.Key, "画像ダウンロード中は取得できません");

                return;
            }

            // 枚数送りで初めてクエリが必要になった場合に備えて、届いた画像の監視を始めておく
            if (_imageWatcher == null)
                StartCompareWatcher();

            // 一度取得した画像は手元のファイルを使い、無い分だけ要求する
            var requests = new List<CompareItem>();

            foreach (var item in items)
            {
                string local = Path.Combine(
                    settings.Server!.serverResponsePath!,
                    Path.GetFileName(item.SourcePath ?? ""));

                if (File.Exists(local))
                {
                    // 取得中だった前の枚数が遅れて届いても上書きしない
                    _compareSession?.RemoveKey(item.Key);
                    _ = _compare?.SetImageAsync(item.Key, local);
                }
                else
                {
                    requests.Add(item);
                }
            }

            if (requests.Count == 0)
                return;

            var session = new ImageDownloadService(settings).RequestImages(requests);

            if (_compareSession == null)
                _compareSession = session;
            else
                _compareSession.Merge(session);
        }

        private void StartCompareWatcher()
        {
            _imageWatcher?.Dispose();

            _imageWatcher =
                new CompareImageWatcher(
                    settings.Server!.serverResponsePath!);

            _imageWatcher.ImageCreated +=
                async path =>
                {
                    if (_compare == null)
                        return;

                    if (_compareSession == null)
                        return;

                    if (!_compareSession.TryGetKey(
                            path,
                            out var key))
                        return;

                    await Dispatcher.InvokeAsync(async () =>
                    {
                        // 書き込み中のファイルは開けないので、読めるようになるまで待つ（最大約4秒）
                        for (int i = 0; i < 20; i++)
                        {
                            try
                            {
                                using var fs =
                                    new FileStream(
                                        path,
                                        FileMode.Open,
                                        FileAccess.Read,
                                        FileShare.Read);

                                break;
                            }
                            catch
                            {
                                await Task.Delay(200);
                            }
                        }

                        await _compare.SetImageAsync(
                            key,
                            path);
                    });
                };
        }

        #endregion

        #region 比較用リスト

        private void AddCompareTarget_MenuClick(object sender, RoutedEventArgs e)
        {
            foreach (var icon in GetSelectedIcons())
            {
                string durabilityId = icon.DurabilityId;
                string imageId = icon.ImageId;

                if (CompareTargets.Any(x =>
                    x.ImageId == imageId))
                {
                    continue;
                }

                CompareTargets.Add(
                    new CompareTargetItem
                    {
                        DurabilityId = durabilityId,
                        ImageId = imageId,
                        ImageName = icon.ImageName,
                        Pages = icon.Pages,
                        Pattern = icon.Pattern,
                        ImagePath = icon.ImagePath,
                        Item1 = icon.Item1,
                        Item2 = icon.Item2,
                        Item3 = icon.Item3
                    });
            }
        }

        private void RemoveCompareTargetItem_Click(object sender, RoutedEventArgs e)
        {
            string imageId = (sender as FrameworkElement)?.Tag as string ?? "";
            var target = CompareTargets.FirstOrDefault(x => x.ImageId == imageId);

            if (target != null)
                CompareTargets.Remove(target);
        }

        private void ClearCompareTargets_Click(object sender, RoutedEventArgs e)
        {
            CompareTargets.Clear();
        }

        private void ShowCompareTargetWindow_Click( object sender, RoutedEventArgs e)
        {
            OpenCompareWindow(CreateCompareItems(
                CompareTargets.Select(x => (x.DurabilityId, x.ImageName, x.Pages, x.Pattern)), resolve: false));
        }

        #endregion
    }
}
