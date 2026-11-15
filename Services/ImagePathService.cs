using SampleViewer.Models;
using System.IO;

namespace SampleViewer.Services
{
    // 画像の読み込み元を決める
    // このPCのローカルフォルダに画像があればそれを使い、無ければ選択中の方式（Box／クエリ）で取得する
    public class ImagePathService
    {
        private readonly Settings settings;

        public ImagePathService(Settings settings)
        {
            this.settings = settings;
        }

        private bool UseLargeImage => settings.User!.IsUseLargeImage;
        private string Extension => UseLargeImage ? ".jpg" : ".png";

        // 表示中の画像サイズ（中画像／元画像）に対応する、このPCのローカルフォルダ。使わない・未設定・存在しない場合は null
        public string? LocalFolder
        {
            get
            {
                if (!settings.User!.UseLocalImages)
                    return null;

                string? folder = UseLargeImage
                    ? settings.Machine.LocalLargeImagePath
                    : settings.Machine.LocalOriginalImagePath;

                return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;
            }
        }

        // 読み込み元のパスと、直接読めるか（false はクエリで要求する必要がある）
        public (string Path, bool IsDirect) Resolve(string durabilityId, string imageName)
        {
            string? folder = LocalFolder;

            if (folder != null)
            {
                string local = Path.Combine(folder, durabilityId, imageName + Extension);

                if (File.Exists(local))
                    return (local, true);
            }

            return settings.User!.AccessMode == UserSettings.ImageAccessMode.Box
                ? (GetBoxImagePath(durabilityId, imageName), true)
                : (GetQueryImagePath(durabilityId, imageName), false);
        }

        public string GetSourcePath(string durabilityId, string imageName)
        {
            return Resolve(durabilityId, imageName).Path;
        }

        private string GetBoxImagePath(string durabilityId, string imageName)
        {
            string root = UseLargeImage
                ? PathFactory.GetBoxLargeImagePath(settings.Server!)
                : PathFactory.GetBoxCorrectedImagePath(settings.Server!);

            return Path.Combine(root, durabilityId, imageName + Extension);
        }

        // クエリの要求先：画像PC上のパス（serverSettings.json）
        private string GetQueryImagePath(string durabilityId, string imageName)
        {
            string root = UseLargeImage
                ? PathFactory.GetImagePcLargeImagePath(settings.Server!)
                : PathFactory.GetImagePcCorrectedImagePath(settings.Server!);

            return Path.Combine(root, durabilityId, imageName + Extension);
        }
    }
}
