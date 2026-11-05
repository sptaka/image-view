using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Repositories;
using System.IO;

namespace SampleViewer.Services
{
    public static class SearchService
    {
       

        

        public static List<string> GetDistinctValues(
    string column,
    Dictionary<string, object> conditions)
        {
            var repo = new ImageRepository();

            return repo.GetDistinctValues(
                column,
                conditions);
        }
        public static List<DurabilityDisplayItem>
    GetDurabilityDisplayItems(
        Dictionary<string, object> conditions)
        {
            var repo = new ImageRepository();

            return repo.GetDurabilityDisplayItems(
                conditions);
        }

        public static Dictionary<int, HashSet<string>> GetDurabilitySerials()
        {
            return new ImageRepository().GetDurabilitySerials();
        }

        public static Dictionary<string, (string Env, string Model, string Purpose)> GetDurabilityInfo(IEnumerable<string> taikyuCodes)
        {
            return new ImageRepository().GetDurabilityInfo(taikyuCodes);
        }

        public static int GetHitCount(
    Dictionary<string, object> conditions)
        {
            var repo = new ImageRepository();

            return repo.GetHitCount(
                conditions);
        }
        public static List<IconItem> GetIconItems(
    Dictionary<string, object> conditions,
    string label1,
    string label2,
    string label3,
    int limit,
    string iconRootPath,
    HashSet<string> installedIconIds)
        {
            var repo =
                new ImageRepository();

            return repo.GetIconItems(
                conditions,
                label1,
                label2,
                label3,
                limit,
                iconRootPath,
                installedIconIds);
        }
        private static string GetPlaceholderPath(string iconRootPath, string pattern)
        {
            return Path.Combine(iconRootPath,"_templates",$"{pattern}.jpg");
        }

        public static SearchResult GetSearchResult(Dictionary<string, object> conditions,
            string label1,string label2,string label3,int limit,string iconRootPath,
            HashSet<string> installedIconIds)
        {
            return new SearchResult
            {
                HitCount =
                    GetHitCount(conditions),

                Items =
                    GetIconItems(
                        conditions,
                        label1,
                        label2,
                        label3,
                        limit,
                        iconRootPath,
                        installedIconIds)
            };
        }

        public static List<string> GetImageInfo(
    string imageId)
        {
            var repo = new ImageRepository();

            return repo.GetImageInfo(
                imageId);
        }



    }


}