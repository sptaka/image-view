using SampleViewer.Models;
using SampleViewer.Utils;
using SampleViewer.Windows;
using System.Windows;
using static SampleViewer.Models.UserSettings;


namespace SampleViewer.Services
{
    public class AppBootstrapper
    {
        public async Task<MainWindow> RunAsync(Application app)
        {
            // =============================================
            // Phase 0: Splash 表示
            // =============================================
            var splash = new SplashWindow();
            splash.Show();

            var initializer = new AppInitializer();

            var progress = new Progress<ProgressInfo>(p =>
            {
                splash.SetProgress(p.Percent, p.StatusMessage);
                splash.SetMode(p.WarningMessage);
            });

            // =============================================
            // Phase 1: ローカル環境の初期化
            // =============================================
            var result = await initializer.CheckFirstTimeAndCreateLocalEnvAsync(progress);


            // =============================================
            // Phase 2: 設定の読込
            // =============================================
            var settings = initializer.LoadSettings();

            // =============================================
            // Phase 3: ネットワーク状態の確認
            // =============================================
            var systemStatus = await initializer.CheckNetworkAsync(settings, progress);

            if (AppInitializer.ApplyAccessModeFallback(settings, systemStatus))
            {
                MessageBox.Show($"接続状態によりアクセスモードを {settings.User!.AccessMode} に変更しました。");
                SettingsService.Save(settings.Local!.UserSettingsPath,settings.User);
            }


            // =============================================
            // Phase 4: 更新有無のチェック
            // =============================================
            var updateStatus = await initializer.CheckUpdateAsync(systemStatus, settings, progress);

            // =============================================
            // ☆必要情報をAppContextに集約
            // =============================================

            var context = new Models.AppContext
            {
                Settings = settings,
                SystemStatus = systemStatus,
                UpdateStatus = updateStatus
            };

            await initializer.ExecuteUpdatesAsync(
                context.UpdateStatus,
                context.Settings,
                progress,
                () =>
                {
                    var download = new UpdateConfirmationWindow(settings);
                    CenterWindow(download, splash);
                    download.ShowDialog();
                    return download.SelectedDurabilityIds;
                });


            // =============================================
            // Phase 5: MainWindow生成
            // =============================================

            var main = new MainWindow(context.Settings,context.SystemStatus);
            // 通常サイズ時の位置はスプラッシュと同じディスプレイの中央。そのディスプレイで最大化して表示する
            CenterWindow(main, splash);
            main.WindowState = WindowState.Maximized;


            // =============================================
            // Phase 6: Splash終了　→　メイン画面
            // =============================================
            splash.Close();
            return main;
        }


        // スプラッシュが表示されているディスプレイの中央に配置する（ドラッグで移動された場合も追従）
        private void CenterWindow(
    Window target,
    Window baseWindow)
        {
            WindowPlacement.CenterOnSameMonitor(target, baseWindow);
        }

    }
}
