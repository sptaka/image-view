namespace SampleViewer.Models
{
    public class CompareTargetItem
    {
        public string ImageId { get; set; } = "";

        public string ImageName { get; set; } = "";

        public string DurabilityId { get; set; } = "";

        public double? Pages { get; set; }

        public string Pattern { get; set; } = "";

        public string Item1 { get; set; } = "";

        public string Item2 { get; set; } = "";

        public string Item3 { get; set; } = "";

        // 一覧に出す小さな画像（アイコン）
        public string ImagePath { get; set; } = "";

        public string DisplayText =>
            $"{DurabilityId} - {ImageName}";

        public string PagesText => Pages.HasValue ? $"{Pages:#,0}枚" : "";

        // マウスを乗せたときに出す詳しい情報
        public string ToolTipText =>
            string.Join("\n", new[] { ImageName, Item1, Item2, Item3 }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}

