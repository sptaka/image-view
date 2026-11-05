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

        public string DisplayText =>
            $"{DurabilityId} - {ImageName}";
    }
}

