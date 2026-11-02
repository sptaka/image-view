namespace SampleViewer.Models
{
    public class IconItem
    {
        public string Pattern { get; set; } = "";

        public bool HasIcon { get; set; }

        public string ImagePath { get; set; } = "";

        public string ImageId { get; set; } = "";

        public string ImageName { get; set; } = "";

        public string DurabilityId { get; set; } = "";

        public string Item1 { get; set; } = "";

        public string Item2 { get; set; } = "";

        public string Item3 { get; set; } = "";

        public double? Pages { get; set; }

        // トレンドグラフの測定値の点（1測定に1点）と対応づける
        public long MeasurementId { get; set; }
    }
}
