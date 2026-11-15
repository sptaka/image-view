using SampleViewer.Models;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SampleViewer.Services
{
    // PCの全ユーザーで共有するデータ（DB・アイコン・画像キャッシュ）の置き場所を用意する
    // 個人の設定とログは、従来どおりユーザーごとのフォルダに置く
    public static class SharedDataService
    {
        private const string LockFileName = "update.lock";

        // 共有フォルダを作れるか、書き込めるかを確かめる。使えないPCではユーザーごとのフォルダに戻す
        public static bool CanUse(string sharedDir)
        {
            try
            {
                Directory.CreateDirectory(sharedDir);

                string probe = Path.Combine(sharedDir, $"probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        // 共有データのフォルダを整える。戻り値：ユーザーごとのフォルダから移したか
        public static bool Prepare(LocalPaths paths, string defaultIconStateDbPath)
        {
            bool migrated = false;

            if (paths.IsShared && !File.Exists(paths.IconStateDbPath))
            {
                // このPCで最初に起動した人のデータを共有フォルダへ移す（2人目以降は共有側をそのまま使う）
                migrated = MoveFromUserFolder(paths);
                GrantModifyToUsers(paths.DataDir);
            }

            Directory.CreateDirectory(paths.IconsDir);
            Directory.CreateDirectory(paths.IconsTemplateDir);

            RemoveLegacyItems(paths);

            if (!File.Exists(paths.IconStateDbPath))
                File.Copy(defaultIconStateDbPath, paths.IconStateDbPath);

            return migrated;
        }

        // 以前の版が作った、今は使わないものを片付ける（空のフォルダと、サーバー設定のコピー）
        private static void RemoveLegacyItems(LocalPaths paths)
        {
            try
            {
                string oldServerSettings = Path.Combine(paths.SettingsDir, "serverSettings.json");

                if (File.Exists(oldServerSettings))
                    File.Delete(oldServerSettings);

                foreach (var dir in new[]
                {
                    Path.Combine(paths.BaseDir, "logs"),
                    Path.Combine(paths.BaseDir, "docs"),
                    Path.Combine(paths.BaseDir, "imageCache"),
                    Path.Combine(paths.DataDir, "imageCache")
                })
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 片付けられなくても動作には影響しない
            }
        }

        private static bool MoveFromUserFolder(LocalPaths paths)
        {
            string oldState = Path.Combine(paths.BaseDir, Path.GetFileName(paths.IconStateDbPath));

            if (!File.Exists(oldState))
                return false;

            // アイコンの取得状況（LocalIconState.db）とアイコンはセットで移す
            MoveDirectory(Path.Combine(paths.BaseDir, "icons"), paths.IconsDir);
            MoveFile(Path.Combine(paths.BaseDir, Path.GetFileName(paths.LocalDbPath)), paths.LocalDbPath);
            MoveFile(oldState, paths.IconStateDbPath);
            return true;
        }

        private static void MoveDirectory(string from, string to)
        {
            if (!Directory.Exists(from))
                return;

            if (Directory.Exists(to))
                Directory.Delete(to, recursive: true);

            if (Path.GetPathRoot(from) == Path.GetPathRoot(to))
            {
                Directory.Move(from, to);
                return;
            }

            // 別ドライブへは移動できないのでコピーする
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(from, to));

            Directory.CreateDirectory(to);

            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(from, to), true);

            Directory.Delete(from, recursive: true);
        }

        private static void MoveFile(string from, string to)
        {
            if (File.Exists(from))
                File.Move(from, to, overwrite: true);
        }

        // 他のユーザーも更新できるよう、フォルダ以下に「Users：変更」の権限を付ける
        // 移動したファイルは元の場所の権限を持ったままなので、移動のあとに実行して付け直す
        private static void GrantModifyToUsers(string dir)
        {
            try
            {
                var info = new DirectoryInfo(dir);
                var security = info.GetAccessControl();

                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                info.SetAccessControl(security);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
            {
                // 権限を変えられないPCでは、作成者以外は更新できない（閲覧はできる）
            }
        }

        // 他のユーザーが同時にDBやアイコンを更新しないよう、更新中は目印のファイルを開いたままにする
        // 待っても取れなければ null（今回は更新を見送る）
        public static async Task<IDisposable?> AcquireUpdateLockAsync(LocalPaths paths, int timeoutSeconds = 60)
        {
            string path = Path.Combine(paths.DataDir, LockFileName);

            for (int i = 0; i <= timeoutSeconds; i++)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    await Task.Delay(1000);
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
