namespace SampleViewer.Models
{
    // 限度見本1件：どの特徴量の、どの段階の見本か
    public class LimitSample
    {
        public string FeatureName { get; set; } = "";

        // 特徴量が属するパターン（DBから補う）
        public string PatternName { get; set; } = "";

        // 段階：Lv.1～Lv.4、特別救済 など
        public string Grade { get; set; } = "";

        // 機種特有の見本なら機種名。空は全機種共通
        public string ModelName { get; set; } = "";

        public string Comment { get; set; } = "";

        // 見本の画像（通常の画像と同じく、耐久IDと画像名で指定する）
        public string DurabilityId { get; set; } = "";
        public string ImageName { get; set; } = "";
    }
}
