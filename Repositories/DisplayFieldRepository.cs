using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Services;
using System.Text.RegularExpressions;

namespace SampleViewer.Repositories;

// 画像の下のラベルに出せる項目。DBの display_field テーブルで決める
// （is_info_label と is_visible が 1 の行を、display_order の順に使う。ほかの列は使わない）
public static class DisplayFieldRepository
{
    // 列名はそのままSQLに入るので、検索で使うテーブル（m・t・i・p）の列の形だけを通す
    private static readonly Regex ColumnPattern = new(@"^[mtip]\.[A-Za-z_][A-Za-z0-9_]*$");

    private static List<DisplayField>? _cache;

    public static IReadOnlyList<DisplayField> InfoLabelFields => _cache ??= Load();

    // ラベルの項目名から列を引く。ラベルに出せない項目は false
    public static bool TryGetColumn(string displayName, out string column)
    {
        column = InfoLabelFields.FirstOrDefault(x => x.DisplayName == displayName)?.DbColumn ?? "";
        return column != "";
    }

    private static List<DisplayField> Load()
    {
        // 値が文字で入っていても、数値として判定・整列する
        const string sql = @"
SELECT display_name, db_column
FROM display_field
WHERE CAST(is_info_label AS INTEGER) = 1
  AND CAST(is_visible AS INTEGER) = 1
ORDER BY CAST(display_order AS INTEGER), display_field_id";

        using var conn = DbHelper.OpenConnection();
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = cmd.ExecuteReader();

        var result = new List<DisplayField>();

        while (reader.Read())
        {
            string name = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            string column = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

            if (name == "" || !ColumnPattern.IsMatch(column) || result.Any(x => x.DisplayName == name))
                continue;

            result.Add(new DisplayField { DisplayName = name, DbColumn = column });
        }

        return result;
    }
}
