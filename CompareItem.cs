namespace SampleViewer.Models
{
    public class CompareItem
    {

        public string Key { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string? SourcePath { get; set; }

        // SourcePath を直接読めるか（ローカル・Box）。false はクエリで要求して届くのを待つ
        public bool IsDirect { get; set; } = true;

        // タイルのラベルに表示する情報（ドラッグ＆ドロップで追加した画像は DisplayName のみ）
        public string DurabilityId { get; set; } = "";
        public double? Pages { get; set; }
        public string Pattern { get; set; } = "";
        public string Env { get; set; } = "";
        public string Model { get; set; } = "";
        public string Purpose { get; set; } = "";

        // ラベルの先頭に出す札（限度見本との比較で「対象」「限度 Lv.2」など）。空なら出さない
        public string Badge { get; set; } = "";
        public bool IsLimitSample { get; set; }

    }
}
