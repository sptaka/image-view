namespace SampleViewer.Models
{
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

    public class DeveloperChange
    {
        public double Pages { get; set; }

        public List<string> Developers { get; set; } = new();

        public List<string> Details { get; set; } = new();
    }
}
