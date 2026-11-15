using System.IO;

namespace SampleViewer.Models
{
    public class Settings
    {       
        public UserSettings? User { get; set; }
        public ServerSettings? Server { get; set; }
        public LocalPaths? Local {  get; set; }
        public bool IsImagePc { get; set; }
        public bool IsDeveloperMode {  get; set; }
        public string? IconRootPath {  get; set; }
        public HashSet<string> InstalledIconIds { get; set; } = new();

        // このPCの設定（ローカルの画像フォルダ）
        public MachineSettings Machine { get; set; } = new();
    }

    // PCごとの設定（全ユーザー共通）。画像がこのPCにある場合のフォルダを持つ
    // 例：D:\GazouDB02\BackUp\60_LargeJpg（この下に 耐久ID\画像名.jpg）
    public class MachineSettings
    {
        public string? LocalLargeImagePath { get; set; }
        public string? LocalOriginalImagePath { get; set; }
    }

    public class ServerSettings
    {
        public int SettingsVersion {  get; set; }
        public string? serverIconTemplatePath {  get; set; }
        public string? serverBasePath { get; set; }
        public string? serverIconCatalogPath { get; set; }
        public string? serverQueryPath { get; set; }
        public string? serverClientPath { get; set; }
        public string? serverResponsePath { get; set; }
        public string? serverInfoFilePath { get; set; }
        public string? serverIconPath { get; set; }
        public string? serverDatabasePath { get; set; }
        public string? serverApplicationDirectoryPath { get; set; }
        public string? serverApploaderPath { get; set; }
        public string? ImagePcName { get; set; }
        // クエリの要求先（画像PC上の画像の場所）。各PCのローカルの画像フォルダは MachineSettings で持つ
        public string? ImagePcRootPath {  get; set; }

        // 画像PCで中画像（jpg）だけ別の場所に置く場合のルート。未設定なら ImagePcRootPath と同じ
        public string? ImagePcLargeRootPath { get; set; }
        public string? ImagePcQueryTestFile { get; set; }
        public string? BoxRootFolder {  get; set; }
        public string? BoxDriveInstallUrl {  get; set; }
        public string? OriginalImageFolderName { get; set; }
        public string? CorrectedImageFolderName { get; set; }
        public string? LargeImageFolderName { get; set; }



    }

    public class LocalPaths
    {

        // 設定とデータの置き場所は、役割ごとに1か所にする
        //   アプリのフォルダ（AppDir\settings）   ：配布される設定。アプリ更新で丸ごと入れ替わる。読むだけ
        //   ユーザーごと（BaseDir）               ：個人の設定（userSettings.json）
        //   PCごと（DataDir、全ユーザー共通）     ：DB・アイコン・ローカルの画像フォルダの設定

        // ユーザーごとのフォルダ
        public string BaseDir { get; init; } = "";

        // PCの全ユーザーで共有するフォルダ。空ならユーザーごとのフォルダを使う（共有フォルダを作れないPC）
        public string SharedDir { get; init; } = "";
        public bool IsShared => SharedDir != "";
        public string DataDir => IsShared ? SharedDir : BaseDir;

        // ===== ディレクトリ =====
        public string SettingsDir => Path.Combine(BaseDir, "settings");
        public string IconsDir => Path.Combine(DataDir, "icons");
        public string UploaderDir => Path.Combine(BaseDir, "uploader");
        public string UploaderExePath => Path.Combine(UploaderDir, "AppUploader.exe");

        public string IconsTemplateDir => Path.Combine(IconsDir, "_templates");

        // ===== ファイル =====
        public string ServerSettingsPath => Path.Combine(ExeSettingsDir, "serverSettings.json");
        public string UserSettingsDefaultPath => Path.Combine(ExeSettingsDir, "userSettingsDefault.json");
        public string UserSettingsPath => Path.Combine(SettingsDir, "userSettings.json");
        public string MachineSettingsPath => Path.Combine(DataDir, "settings", "machineSettings.json");

        public string LocalDbPath => Path.Combine(DataDir, "SampleDB.db");
        public string IconStateDbPath => Path.Combine(DataDir, "LocalIconState.db");

        // メモ。メインのDBは配布で丸ごと入れ替わるので、別のDBに持つ
        public string AnnotationDbPath => Path.Combine(DataDir, "Annotation.db");

        // ===== 実行環境 =====
        public string AppDir => System.AppContext.BaseDirectory;
        public string ExeSettingsDir => Path.Combine(AppDir, "settings");

    }



    public class UserSettings
    {
        public int SettingsVersion { get; set; }
        public int SearchLimit { get; set; }
        public bool IsUseLargeImage { get; set; }
        public bool IsTestMode { get; set; }
        public bool IsLeftPaneOpen {  get; set; }
        public bool IsRightPaneOpen { get; set; }
        public ImageAccessMode AccessMode { get; set; }

        // このPCのローカルフォルダに画像があれば、そちらを優先して使う（確認用に切れる）
        public bool UseLocalImages { get; set; } = true;

        // ===== 画面状態（終了時に保存し、次回起動時に復元） =====
        public double LeftPaneWidth { get; set; } = 260;
        public double RightPaneWidth { get; set; } = 420;
        public bool CompareSyncMode { get; set; }
        public bool CompareCommonOnly { get; set; }
        public int CompareToleranceIndex { get; set; } = 1;
        public List<string> CompareTargetIds { get; set; } = new();

        // 前回、耐久IDの一覧で選んでいた耐久（次回起動時に続きから表示する）
        public List<string> SelectedDurabilityIds { get; set; } = new();
        public double IconSize { get; set; } = 150;
        public bool IsIconSizeAuto { get; set; }
        public bool IsFirstImageOnly { get; set; }

        // 画像の下に表示する項目（0～3個）。null は未設定（先頭の3項目を表示）
        public List<string>? InfoLabels { get; set; }

        public enum ImageAccessMode
        {
            Box,Query,Local
        }
    }


}
