using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SampleViewer.Models;

namespace SampleViewer.Services
{
    public class PathFactory
    {
        public static string GetBoxRootPath(ServerSettings settings)
        {
            return Path.Combine(
            @"C:\Users",
            Environment.UserName,
            "Box",
            settings.BoxRootFolder!);
        }

        public static string GetBoxImageDbPath(ServerSettings settings)
        {
            return Path.Combine(GetBoxRootPath(settings),"ImageDB");
        }

        public static string GetBoxOriginalImagePath(
    ServerSettings settings)
        {
            return Path.Combine(
                GetBoxImageDbPath(settings),
                settings.OriginalImageFolderName!);
        }

        public static string GetBoxCorrectedImagePath(
            ServerSettings settings)
        {
            return Path.Combine(
                GetBoxImageDbPath(settings),
                settings.CorrectedImageFolderName!);
        }

        public static string GetBoxLargeImagePath(
            ServerSettings settings)
        {
            return Path.Combine(
                GetBoxImageDbPath(settings),
                settings.LargeImageFolderName!);
        }


        public static string GetImagePcOriginalImagePath(
    ServerSettings settings)
        {
            return Path.Combine(
                settings.ImagePcRootPath!,
                settings.OriginalImageFolderName!);
        }

        public static string GetImagePcCorrectedImagePath(
            ServerSettings settings)
        {
            return Path.Combine(
                settings.ImagePcRootPath!,
                settings.CorrectedImageFolderName!);
        }

        public static string GetImagePcLargeImagePath(ServerSettings settings)
        {
            string root = string.IsNullOrWhiteSpace(settings.ImagePcLargeRootPath)
                ? settings.ImagePcRootPath!
                : settings.ImagePcLargeRootPath;

            return Path.Combine(root, settings.LargeImageFolderName!);
        }
        public static LocalPaths Create()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SampleViewer");

            var sharedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SampleViewer");

            return new LocalPaths
            {
                BaseDir = baseDir,
                SharedDir = SharedDataService.CanUse(sharedDir) ? sharedDir : ""
            };
        }

    }
}
