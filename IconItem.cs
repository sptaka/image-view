using System.ComponentModel;

namespace SampleViewer.Models
{
    public class IconItem : INotifyPropertyChanged
    {
        public string Pattern { get; set; } = "";

        public bool HasIcon { get; set; }

        public string ImagePath { get; set; } = "";

        public string ImageId { get; set; } = "";

        // 新DB用
        public string ImageName { get; set; } = "";

        public string DurabilityId { get; set; } = "";

        public string Item1 { get; set; } = "";

        public string Item2 { get; set; } = "";

        public string Item3 { get; set; } = "";

        // 印字枚数（耐久比較で行を揃える基準）
        public double? Pages { get; set; }

        // トレンドグラフの測定値の点（1測定に1点）と対応づける
        public long MeasurementId { get; set; }

        // パターンの色（Y/M/C/K/Color）。グラフに出す測定値の色の初期値に使う
        public string Color { get; set; } = "";

        // この画像と、この画像の測定に付いたメモ（件数と、マウスを乗せたときに出す内容）
        private int _memoCount;
        public int MemoCount
        {
            get => _memoCount;
            set
            {
                _memoCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MemoCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMemo)));
            }
        }

        public bool HasMemo => MemoCount > 0;

        private string _memoToolTip = "";
        public string MemoToolTip
        {
            get => _memoToolTip;
            set
            {
                _memoToolTip = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MemoToolTip)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
