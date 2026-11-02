namespace SampleViewer.Models
{
    // 右ペインに縦に並べる、耐久1件分のトレンドグラフ
    public class TrendGraphItem
    {
        public string SlotName { get; set; } = "";
        public string DurabilityId { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string Env { get; set; } = "";
        public string Model { get; set; } = "";
        public List<TrendPoint> Points { get; set; } = new();
        public List<DeveloperChange> DeveloperChanges { get; set; } = new();
    }

    // 現像器のシリアルが前回の測定から変わった位置（同じ枚数での交換は1件にまとめる）
    public class DeveloperChange
    {
        public double Pages { get; set; }

        // 交換した現像器（例："TN3"）
        public List<string> Developers { get; set; } = new();

        // 例："TN3: TCMA...4033 → TCMA...3959"
        public List<string> Details { get; set; } = new();
    }
}
