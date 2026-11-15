namespace SampleViewer.Models
{
    // 右ペインに縦に並べる、耐久1件分のトレンドグラフ
    public class TrendGraphItem
    {
        public string SlotName { get; set; } = "";

        // 種類ごとにグラフを分けたときの見出し（Vb、Q/M、M/A、特徴量名）。分けないときは空
        public string Title { get; set; } = "";
        public bool HasTitle => Title != "";

        public string DurabilityId { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string Env { get; set; } = "";
        public string Model { get; set; } = "";
        public List<TrendPoint> Points { get; set; } = new();
        public List<DeveloperChange> DeveloperChanges { get; set; } = new();

        // グラフの上端に印を出すメモ（画像・測定に付いたもの。枚数ごとにまとめる）
        public List<(double Pages, string Text)> Memos { get; set; } = new();
    }

    // トレンド欄の上部のタブ（全体／耐久ごと）。Id が空のものが「全体」
    public class TrendTab
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    // 現像器のシリアルが前回の測定から変わった位置（同じ枚数での交換は1件にまとめる）
    public class DeveloperChange
    {
        public double Pages { get; set; }

        // 交換した現像器の色（TN1～TN4 = Y/M/C/K）。Details と同じ並び
        public List<string> Developers { get; set; } = new();

        // 例："C: TCMA...4033 → TCMA...3959"
        public List<string> Details { get; set; } = new();
    }
}
