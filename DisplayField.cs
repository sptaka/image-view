namespace SampleViewer.Models;

// 画像の下のラベルに出せる項目（DBの display_field テーブルで増減できる）
public class DisplayField
{
    public string DisplayName { get; set; } = "";

    // 値を取り出すDBの列（m.pages など）
    public string DbColumn { get; set; } = "";
}
