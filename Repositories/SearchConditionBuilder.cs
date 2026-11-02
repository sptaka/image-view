using Microsoft.Data.Sqlite;
using SampleViewer.Models;

namespace SampleViewer.Repositories;

public static class SearchConditionBuilder
{
    public const string PagesMinKey = "PagesMin";
    public const string PagesMaxKey = "PagesMax";

    public static (
        string whereClause,
        List<SqliteParameter> parameters)
        Build(
            Dictionary<string, object> conditions,
            string? excludeField = null)
    {
        var parts = new List<string>();
        var parameters = new List<SqliteParameter>();

        int index = 0;

        foreach (var kv in conditions)
        {
            // 印字枚数の範囲（下限・上限）
            if (kv.Key == PagesMinKey || kv.Key == PagesMaxKey)
            {
                if (kv.Value is double pages)
                {
                    string p1 = $"@p{index++}";
                    parts.Add(kv.Key == PagesMinKey ? $"m.pages >= {p1}" : $"m.pages <= {p1}");
                    parameters.Add(new SqliteParameter(p1, pages));
                }
                continue;
            }

            if (kv.Key == excludeField)
                continue;

            if (!DisplayFieldMap.ColumnMap.TryGetValue(
                    kv.Key,
                    out var column))
            {
                continue;
            }

            if (kv.Value is string s &&
                !string.IsNullOrWhiteSpace(s))
            {
                string name = $"@p{index++}";

                parts.Add(
                    $"{column} = {name}");

                parameters.Add(
                    new SqliteParameter(
                        name,
                        s));
            }

            else if (kv.Value is List<string> list &&
                     list.Count > 0)
            {
                var names = new List<string>();

                foreach (var item in list)
                {
                    string name = $"@p{index++}";

                    names.Add(name);

                    parameters.Add(
                        new SqliteParameter(
                            name,
                            item));
                }

                parts.Add(
                    $"{column} IN ({string.Join(",", names)})");
            }
        }

        return (
            parts.Count == 0
                ? ""
                : " AND " + string.Join(" AND ", parts),
            parameters);
    }
}