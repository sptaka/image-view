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
        public string? ImagePcRootPath {  get; set; }
        public string? ImagePcQueryTestFile { get; set; }
        public string? BoxRootFolder {  get; set; }
        public string? BoxDriveInstallUrl {  get; set; }
        public string? OriginalImageFolderName { get; set; }
        public string? CorrectedImageFolderName { get; set; }
        public string? LargeImageFolderName { get; set; }



    }

    public class LocalPaths
    {

        public string BaseDir { get; init; } = "";

        // ===== ディレクトリ =====
        public string SettingsDir => Path.Combine(BaseDir, "settings");
        public string LogsDir => Path.Combine(BaseDir, "logs");
        public string IconsDir => Path.Combine(BaseDir, "icons");
        public string ImageCacheDir => Path.Combine(BaseDir, "imageCache");
        public string UploaderDir => Path.Combine(BaseDir, "uploader");
        public string UploaderExePath => Path.Combine(UploaderDir, "AppUploader.exe");
        public string DocsDir => Path.Combine(BaseDir, "docs");

        public string IconsTemplateDir => Path.Combine(IconsDir, "_templates");

        // ===== ファイル =====
        public string ServerSettingsPath => Path.Combine(SettingsDir, "serverSettings.json");
        public string UserSettingsPath => Path.Combine(SettingsDir, "userSettings.json");

        public string LocalDbPath => Path.Combine(BaseDir, "SampleDB.db");
        public string IconStateDbPath => Path.Combine(BaseDir, "LocalIconState.db");

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

        // ===== 画面状態（終了時に保存し、次回起動時に復元） =====
        public double LeftPaneWidth { get; set; } = 260;
        public double RightPaneWidth { get; set; } = 420;
        public bool CompareSyncMode { get; set; }
        public bool CompareCommonOnly { get; set; }
        public int CompareToleranceIndex { get; set; } = 1;
        public List<string> CompareTargetIds { get; set; } = new();
        public double IconSize { get; set; } = 150;
        public bool IsIconSizeAuto { get; set; }
        public bool IsFirstImageOnly { get; set; }

        public enum ImageAccessMode
        {
            Box,Query,Local
        }
    }


}
