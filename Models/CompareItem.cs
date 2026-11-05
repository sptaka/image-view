namespace SampleViewer.Models
{
    public class CompareItem
    {

        public string Key { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string? SourcePath { get; set; }

        // タイルのラベルに表示する情報（ドラッグ＆ドロップで追加した画像は DisplayName のみ）
        public string DurabilityId { get; set; } = "";
        public double? Pages { get; set; }
        public string Pattern { get; set; } = "";
        public string Env { get; set; } = "";
        public string Model { get; set; } = "";
        public string Purpose { get; set; } = "";

    }
}
