namespace SampleViewer.Repositories;

// 検索条件に使う項目と、DBの列の対応
// 検索の部品（コンボボックス・耐久IDリスト）は画面に固定で置いているので、対応もコードで決める
public static class SearchFields
{
    public const string DurabilityId = "耐久ID";
    public const string Maker = "メーカー";
    public const string Model = "機種";
    public const string Env = "環境";
    public const string Pattern = "パターン";
    public const string Color = "色";

    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>
    {
        [DurabilityId] = "t.taikyu_code",
        [Maker] = "t.maker",
        [Model] = "t.model_name",
        [Env] = "t.env",
        [Pattern] = "p.pattern_name",
        [Color] = "p.color"
    };
}
