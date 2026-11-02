using System.ComponentModel;

namespace SampleViewer.Models
{
    public class DurabilityImageGroup : INotifyPropertyChanged
    {
        public string DurabilityId { get; set; } = "";
        // 見出しのバッジ（A〜D、比較対象でない閲覧中の耐久は「閲覧」）
        public string SlotName { get; set; } = "";
        public string Purpose { get; set; } = "";

        // 見出しに表示する環境（HH/LL/NL/JJ で色分け）と機種（メーカー＋機種名）
        public string Env { get; set; } = "";
        public string Model { get; set; } = "";

        // 比較対象（A～D）に登録済みか。未登録なら左リストで閲覧中の耐久を一時表示している行
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
