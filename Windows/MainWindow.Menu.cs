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

        private void ShowNetworkStatus_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                $"Developer : {(settings.IsDeveloperMode ? "ON" : "OFF")}\r\n" +
                $"Server : {(systemStatus.ServerAvailable ? "OK" : "NG")}\r\n" +
                $"Query : {(systemStatus.QueryAvailable ? "OK" : "NG")}\r\n" +
                $"Box : {(systemStatus.BoxAvailable ? "OK" : "NG")}\r\n" +
                $"Mode : {settings.User!.AccessMode}"
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

        // 開発用（DEBUGビルドのメニューからのみ使用）
        private const string DbBrowserPath = @"C:\Users\yasudata\Work\01_CurrentProject\DB化\画像データDB\DB.Browser.for.SQLite-v3.13.1-win64\DB Browser for SQLite.exe";

        private static void OpenWithDbBrowser(string? dbFilePath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DbBrowserPath,
                Arguments = $"\"{dbFilePath}\"",
                UseShellExecute = false
            });
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

                // Local / Box
                if (settings.User!.AccessMode != ImageAccessMode.Query)
                {
                    var pathService = new ImagePathService(settings);

                    for (int index =0; index<selectedItems.Count; index++)
                    {
                        var icon = selectedItems[index];
                        StatusText.Text = $"画像ダウンロード中... ({index + 1}/{selectedItems.Count})";
                        DownloadProgress.Value = (index + 1) * 100.0 / selectedItems.Count;
                        await Dispatcher.Yield( DispatcherPriority.Background);

                        string sourcePath = pathService.GetSourcePath( icon.DurabilityId, icon.ImageName);
                        CopyToDownloads(sourcePath, downloadDir, icon);
                    }
                    DownloadProgress.Value = 0;
                    StatusText.Text =$"画像ダウンロード完了 ({selectedItems.Count}件)";
                    return;
                }

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
                StatusText.Text = $"画像ダウンロード完了 ({selectedItems.Count}件)";
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
            OpenCompareWindow(CreateCompareItems(GetSelectedIcons()));
        }

        // 比較ウィンドウを開き直す（クエリ方式はダウンロード要求、それ以外は直接読み込み）
        private void OpenCompareWindow(List<CompareItem> items)
        {
            if (_isDownloadingImages && settings.User!.AccessMode == ImageAccessMode.Query)
            {
                MessageBox.Show("クエリ方式では、画像ダウンロード中は比較表示ができません");
                return;
            }

            if (items.Count == 0)
                return;

            _compare?.Close();
            _compare = new CompareWindow
            {
                Owner = this
            };

            ClearResponseFolder();

            if (settings.User!.AccessMode == ImageAccessMode.Query)
            {
                OpenCompareWindowByQuery(items);
            }
            else
            {
                OpenCompareWindowDirect(items);
            }
        }

        private List<CompareItem> CreateCompareItems(IEnumerable<IconItem> selectedItems)
        {
            return CreateCompareItems(selectedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.ImagePath))
                .Select(x => (x.DurabilityId, x.ImageName, x.Pages, x.Pattern)));
        }

        private List<CompareItem> CreateCompareItems(
            IEnumerable<(string DurabilityId, string ImageName, double? Pages, string Pattern)> images)
        {
            var imagePathService = new ImagePathService(settings);

            var list = images.ToList();
            var infos = SearchService.GetDurabilityInfo(list.Select(x => x.DurabilityId).Distinct());

            return list
                .Select(x =>
                {
                    var info = infos.GetValueOrDefault(x.DurabilityId);

                    return new CompareItem
                    {
                        Key = Guid.NewGuid().ToString(),
                        DisplayName = x.ImageName,
                        SourcePath = imagePathService.GetSourcePath(x.DurabilityId, x.ImageName),
                        DurabilityId = x.DurabilityId,
                        Pages = x.Pages,
                        Pattern = x.Pattern,
                        Env = info.Env ?? "",
                        Model = info.Model ?? "",
                        Purpose = info.Purpose ?? ""
                    };
                })
                .ToList();
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

        private void OpenCompareWindowByQuery( List<CompareItem> items)
        {
            _compare.CreatePlaceholders(items);

            StartCompareWatcher();

            _compare.Show();
            _compare.Activate();

            var downloader =
                new ImageDownloadService(settings);

            _compareSession =
                downloader.RequestImages(items);
        }

        private void OpenCompareWindowDirect( List<CompareItem> items)
        {
            _compare.Show();
            _compare.Activate();

            _compare.AddItems(items);
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
                        Item1 = icon.Item1,
                        Item2 = icon.Item2,
                        Item3 = icon.Item3
                    });
            }
        }

        private void ClearCompareTargets_Click(object sender, RoutedEventArgs e)
        {
            CompareTargets.Clear();
        }

        private void ShowCompareTargetWindow_Click( object sender, RoutedEventArgs e)
        {
            OpenCompareWindow(CreateCompareItems(
                CompareTargets.Select(x => (x.DurabilityId, x.ImageName, x.Pages, x.Pattern))));
        }

        #endregion
    }
}
