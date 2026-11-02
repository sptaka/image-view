namespace SampleViewer.Models
{
    /// <summary>
    /// 耐久比較の1行に並ぶ1マス。シンクロモードでは、基準の枚数に対して
    /// 完全一致・近似（許容差以内の最も近い枚数）・該当なし のいずれかになる。
    /// </summary>
    public class CompareCell
    {
        public IconItem? Icon { get; init; }

        // このマスが表す枚数（シンクロモードでは全行共通の軸の値）
        public double? AxisPages { get; init; }

        public bool IsApproximate { get; init; }

        public bool IsEmpty => Icon == null;

        // スクロール位置の復元に使う枚数
        public double? PositionPages => AxisPages ?? Icon?.Pages;

        public string ApproxText
        {
            get
            {
                if (!IsApproximate || Icon?.Pages == null || AxisPages == null)
                    return "";

                double diff = Icon.Pages.Value - AxisPages.Value;

                return diff == 0 ? "≈" : $"≈ {diff:+#,0;-#,0}";
            }
        }

        public string ToolTipText
        {
            get
            {
                if (IsEmpty)
                    return $"{AxisPages:#,0}枚 付近に画像なし";

                if (IsApproximate)
                    return $"近似表示：実際 {Icon!.Pages:#,0}枚（基準 {AxisPages:#,0}枚）";

                return $"{Icon!.Pages:#,0}枚";
            }
        }

        public static CompareCell Exact(IconItem icon, double? axisPages)
        {
            return new CompareCell { Icon = icon, AxisPages = axisPages };
        }
    }
}
