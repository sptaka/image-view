using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Utils;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Threading;
using static SampleViewer.Models.UserSettings;
using Settings = SampleViewer.Models.Settings;

namespace SampleViewer.Services
{
    public class AppInitializer
    {
        private string warningMessage = "";

        public Models.Settings LoadSettings()
        {
            var paths = PathFactory.Create();
            var server = SettingsService.Load<ServerSettings>(paths.ServerSettingsPath);
            var user = SettingsService.Load<UserSettings>(paths.UserSettingsPath);

            // =============================
            // ServerSettings Version確認
            // =============================
            var defaultServer = SettingsService.Load<ServerSettings>(
                    Path.Combine(paths.ExeSettingsDir,"serverSettings.json"));

            if (server.SettingsVersion != defaultServer.SettingsVersion)
            {
                File.Copy(Path.Combine(paths.ExeSettingsDir, "serverSettings.json"), paths.ServerSettingsPath, true);
                server =SettingsService.Load<ServerSettings>( paths.ServerSettingsPath);
            }

            // =============================
            // UserSettings Version確認
            // =============================
            var defaultUser =SettingsService.Load<UserSettings>(Path.Combine( paths.ExeSettingsDir, "userSettingsDefault.json"));

            if (user.SettingsVersion !=defaultUser.SettingsVersion)
            {
                File.Copy(Path.Combine(paths.ExeSettingsDir, "userSettingsDefault.json"),paths.UserSettingsPath,true);

                user =SettingsService.Load<UserSettings>( paths.UserSettingsPath);
            }




            server.serverResponsePath = Path.Combine(server.serverClientPath!, Environment.UserName);
            //imagePCの場合
            string iconRootPath;
            var isImagePc = Environment.MachineName.Equals(server.ImagePcName,StringComparison.OrdinalIgnoreCase);
            if (isImagePc)
            {
                user.AccessMode = ImageAccessMode.Local;
            }
            
            iconRootPath = paths.IconsDir;
            
            if (user.IsTestMode) warningMessage = "テストモードで実行中";



            bool isDeveloperMode =  DeveloperModeService.IsEnabled(paths.BaseDir);
            var localIconState = new IconLocalStateService(paths.IconStateDbPath);

            var installedIds = localIconState.GetAll()
                .Where(x =>!string.IsNullOrEmpty(x.Value.LocalVersion))
                .Select(x => x.Key)
                .ToHashSet();
            return new Models.Settings
            {
                IsDeveloperMode = isDeveloperMode,
                IsImagePc = isImagePc,
                InstalledIconIds = installedIds,
                IconRootPath = iconRootPath,
                User = user,
                Server = server,
                Local = paths
            };
        }


        public async Task<bool> CheckFirstTimeAndCreateLocalEnvAsync(IProgress<ProgressInfo> progress)
        {
            return await Task.Run(() =>
            {
                var paths = PathFactory.Create();

                if (!Directory.Exists(paths.SettingsDir))
                {
                    if (Directory.Exists(paths.BaseDir))
                        DirectoryUtils.ClearDirectory(paths.BaseDir);

                    Directory.CreateDirectory(paths.SettingsDir);
                    Directory.CreateDirectory(paths.LogsDir);
                    Directory.CreateDirectory(paths.IconsDir);
                    Directory.CreateDirectory(paths.ImageCacheDir);
                    Directory.CreateDirectory(paths.UploaderDir);
                    Directory.CreateDirectory(paths.DocsDir);
                    Directory.CreateDirectory(paths.IconsTemplateDir);

                    var exeDir = paths.ExeSettingsDir;
                    
                    File.Copy(Path.Combine(exeDir, "serverSettings.json"), paths.ServerSettingsPath);
                    File.Copy(Path.Combine(exeDir, "userSettingsDefault.json"), paths.UserSettingsPath);

                    CopyDbUsingBackup(
                        Path.Combine(exeDir, "IconLocalStateDefault.db"),
                        paths.IconStateDbPath);
                }

                progress.Report(new ProgressInfo
                {
                    Percent = 10,
                    StatusMessage = "ローカル環境の構築中",
                    WarningMessage = warningMessage
                });
                return true;
            });


        }
        public async Task<SystemStatus> CheckNetworkAsync(Settings settings, IProgress<ProgressInfo> progress)
        {
            if (settings.IsDeveloperMode)
            {
                return new SystemStatus
                {
                    ServerAvailable = true,
                    QueryAvailable = true,
                    BoxAvailable = true,
                    SystemInfo = "Developer Mode"
                };
            }
            progress.Report(new ProgressInfo
            {
                Percent = 5,
                StatusMessage = "接続ネットワーク検証中",
                WarningMessage = warningMessage
            });
            var systemStatus = new SystemStatus();

            //サーバー接続テスト & 環境構築
            try
            {
                Directory.CreateDirectory(settings.Server!.serverResponsePath!);
                systemStatus.ServerAvailable = true;
                if (File.Exists(settings.Server.serverInfoFilePath))
                {
                    var str = File.ReadAllText(settings.Server.serverInfoFilePath);
                    systemStatus.SystemInfo = str;
                }
            }
            catch (Exception)                    
            {
                systemStatus.ServerAvailable = false;
                MessageBox.Show("sugiサーバーに接続できません。\r\n" +
                        "オフラインモードで起動します。\r\n" +
                        "（ローカルに残っているキャッシュデータのみ参照可能です）\r\n"+
                        "ネットワーク改善後にアプリを再起動してください。");
                return systemStatus;
            }

            //Query接続テスト
            if (settings.IsImagePc)
            {
                systemStatus.QueryAvailable = true;
            }
            else
            {
                systemStatus.QueryAvailable = await QueryFileCopyService.IsQuerySyncerRunningAsync(
                    Environment.UserName, settings.Server!.serverQueryPath!, 
                    settings.Server!.serverResponsePath!, settings.Server!.ImagePcQueryTestFile!);

            }

            //Box接続テスト
            string boxRoot = PathFactory.GetBoxRootPath(settings.Server);
            systemStatus.BoxAvailable = Directory.Exists(boxRoot);


            if (!settings.IsImagePc && !systemStatus.BoxAvailable && !systemStatus.QueryAvailable)
            {
                MessageBox.Show(
                "Box と Query の両方が利用できません。\r\n" +
                "画像表示は利用できません。");
            }
            return systemStatus;
        }


        public static bool ApplyAccessModeFallback(Settings settings, SystemStatus status)
        {
            var user = settings.User;

            if (user == null || settings.IsImagePc)
                return false;

            if (user.AccessMode == ImageAccessMode.Query && !status.QueryAvailable && status.BoxAvailable)
            {
                user.AccessMode = ImageAccessMode.Box;
                return true;
            }

            if (user.AccessMode == ImageAccessMode.Box && !status.BoxAvailable && status.QueryAvailable)
            {
                user.AccessMode = ImageAccessMode.Query;
                return true;
            }

            return false;
        }
        public async Task<UpdateStatus> CheckUpdateAsync(SystemStatus status, Settings settings, IProgress<ProgressInfo> progress)
        {
            if (settings.IsDeveloperMode)
            {
                return new UpdateStatus();
            }
            return await Task.Run(() =>
            {
                progress.Report(new ProgressInfo
                {
                    Percent = 10,
                    StatusMessage = "更新チェック中",
                    WarningMessage = warningMessage
                });

                UpdateStatus updateStatus = new UpdateStatus();
                if (!status.ServerAvailable) return updateStatus;
                
                //AppVersionチェック
                string serverAppVersion = VersionComparer.GetFileVersion(
                    Path.Combine(settings.Server!.serverApplicationDirectoryPath!, "SampleViewer.exe"));
                string localAppVersion = VersionComparer.GetAppVersion();

                if (VersionComparer.CompareVersion(localAppVersion, serverAppVersion) < 0)
                {
                    updateStatus.needAppUpdate = true;
                }

                //Dbバージョンチェック
                string localDbVersion = GetDatabaseVersion(settings.Local!.LocalDbPath);

                string serverDbVersion = GetDatabaseVersion( settings.Server.serverDatabasePath!);

                if (VersionComparer.CompareVersion(localDbVersion, serverDbVersion) != 0)
                {
                    updateStatus.needDBUpdate = true;
                }

                //テンプレートのバージョンチェック
                updateStatus.needTemplateUpdate = NeedTemplateUpdate(settings);

                //アイコンのアップデート
                var catalogService = new IconCatalogService(settings.Server!.serverIconCatalogPath!);

                var localService = new IconLocalStateService(settings.Local!.IconStateDbPath);

                var candidates =catalogService.GetUpdateCandidates(localService);

                updateStatus.needIconsUpdate = candidates.Any();

                //app Verで大きな変化が有った場合、環境フォルダをクリーンアップする。
                if (VersionComparer.CompareVersion(localAppVersion, "0.1.0") < 0)
                {
                    updateStatus.needClearLocalEnv = true;
                }
            
              
                return updateStatus;
            });
        }
        public async Task ExecuteUpdatesAsync(UpdateStatus status, Settings settings, IProgress<ProgressInfo> progress, Func<List<string>> selectIconsFunc)
        {

            // App更新
            if (status.needAppUpdate)
            {
                await UpdateAppAsync(status, settings, progress);
                return;
            }

            // DB更新
            if (status.needDBUpdate)
            {
                await UpdateDatabaseAsync(settings, progress);
            }
          
            // Icon（UI必要なのでコールバック）
            if (status.needIconsUpdate)
            {
                var ids = selectIconsFunc.Invoke();
                
                if (ids != null && ids.Count > 0)
                {
                    var updater = new IconUpdateService();

                    await updater.UpdateIconsAsync(ids, settings, progress);
                }
            }

            // Iconテンプレートをダウンロード
            if (status.needTemplateUpdate)
            {
                DirectoryUtils.ClearDirectory(
                settings.Local!.IconsTemplateDir);

                DirectoryUtils.CopyDirectory(
                settings.Server!.serverIconTemplatePath!,
                settings.Local!.IconsTemplateDir,
                true);
            }


            progress.Report(new ProgressInfo
            {
                Percent = 100,
                StatusMessage = "MainWindow準備中",
                WarningMessage = warningMessage
            });
            await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
       
        public static Task<bool> UpdateAppAsync(UpdateStatus updateStatus, Settings settings,IProgress<ProgressInfo> progress)
        {
            MessageBox.Show("ビューワーが更新されました。新しいプログラムに更新します");    
            
            if (updateStatus.needClearLocalEnv)
            {
                if (Directory.Exists(settings.Local!.BaseDir))
                {
                    DirectoryUtils.ClearDirectory(settings.Local.BaseDir);
                }
            }
            DirectoryUtils.CopyDirectory(settings.Server!.serverApploaderPath!, settings.Local!.UploaderDir);

            var psi = new ProcessStartInfo
            {
                FileName = settings.Local.UploaderExePath,
                Arguments = $"{settings.Local.AppDir} {settings.Server.serverApplicationDirectoryPath} SampleViewer\\tmp SampleViewer.exe",
                UseShellExecute = true
            };
    
            Process.Start(psi);
            Environment.Exit(0);

            return Task.FromResult(true);
        }
        public async Task<bool> UpdateDatabaseAsync(Settings settings,IProgress<ProgressInfo> progress)
        {
            return await Task.Run(() =>
            {
                progress.Report(new ProgressInfo
                {
                    Percent = 15,
                    StatusMessage = "データベースの更新中・・・",
                    WarningMessage = warningMessage
                });
                CopyDbUsingBackup(settings.Server!.serverDatabasePath!, settings.Local!.LocalDbPath);
                return true;
            });
        }

        private bool NeedTemplateUpdate(Settings settings)
        {
            string serverVersionPath =
            Path.Combine(
            settings.Server!.serverIconTemplatePath!,
            "version.txt");

            string localVersionPath =
            Path.Combine(
            settings.Local!.IconsTemplateDir,
            "version.txt");

            string serverVersion =
            File.ReadAllText(serverVersionPath)
            .Trim();

            string localVersion =
            File.Exists(localVersionPath)
            ? File.ReadAllText(localVersionPath)
            .Trim()
            : "";

            return serverVersion != localVersion;
        }
        private void CopyDbUsingBackup(string src, string dist)
        {
            using var source = new SqliteConnection($"Data Source={src};Mode=ReadOnly");
            source.Open();

            var localDir = Path.GetDirectoryName(dist);
            if (!Directory.Exists(localDir))
                Directory.CreateDirectory(localDir!);

            using var dest = new SqliteConnection($"Data Source={dist}");
            dest.Open();

            source.BackupDatabase(dest);
        }

        public static string GetDatabaseVersion(string dbPath)
        {

            try
            {
                using var conn = new SqliteConnection(
                    $"Data Source={dbPath};Mode=ReadOnly");

                conn.Open();

                if (!ExistsSystemInfoTable(conn))
                    return "0.0.0";

                var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT DataVersion FROM SystemInfo LIMIT 1";

                return cmd.ExecuteScalar()?.ToString() ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }


        private static bool ExistsSystemInfoTable(SqliteConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table'
                    AND name='SystemInfo'";

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }


    }









}
