using System.ComponentModel;

namespace SampleViewer.Models
{
    public class DurabilityImageGroup : INotifyPropertyChanged
    {
        public string DurabilityId { get; set; } = "";
        public string SlotName { get; set; } = "";
        public string Purpose { get; set; } = "";

        public string Env { get; set; } = "";
        public string Model { get; set; } = "";

        public bool IsPinned { get; set; }
        public bool IsBrowsingOnly => !IsPinned;

        // 検索結果そのもの
        public List<IconItem> Icons { get; set; } = new();

        // 表示用のマス（モード・フィルタ適用後）。切替時は再検索せずにここだけ作り直す
        private List<CompareCell> _cells = new();
        public List<CompareCell> Cells
        {
            get => _cells;
            set
            {
                _cells = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cells)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
