using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Services;
using System.IO;

namespace SampleViewer.Repositories;

public class ImageRepository
{
    public int GetHitCount(Dictionary<string, object> conditions)
    {
        var (whereClause, parameters)
            = SearchConditionBuilder.Build(
                conditions);

        string sql = $@"
SELECT COUNT(*)
{BaseFromClause}
WHERE 1=1
{whereClause}
";

        using var conn =
            DbHelper.OpenConnection();

        using var cmd =
            new SqliteCommand(sql, conn);

        cmd.Parameters.AddRange(
            parameters.ToArray());

        return Convert.ToInt32(
            cmd.ExecuteScalar());
    }

    private static string BaseFromClause => @"
FROM image i
INNER JOIN measurement m
    ON i.measurement_id = m.measurement_id
INNER JOIN taikyu t
    ON m.taikyu_id = t.taikyu_id
INNER JOIN pattern p
    ON i.pattern_id = p.pattern_id
";

    public List<string> GetDistinctValues(string column, Dictionary<string, object> conditions)
    {
        if (!DisplayFieldMap.ColumnMap.TryGetValue(
            column,
            out string dbColumn))
        {
            return new List<string>();
        }

        var filteredConditions = new Dictionary<string, object>(conditions);

        filteredConditions.Remove(column);

        var (whereClause, parameters) = SearchConditionBuilder.Build(filteredConditions);

        string sql = $@"
SELECT DISTINCT
    {dbColumn}
{BaseFromClause}
WHERE 1 = 1
{whereClause}
AND {dbColumn} IS NOT NULL
AND {dbColumn} <> ''
ORDER BY {dbColumn}
";

        using var conn = DbHelper.OpenConnection();

        using var cmd = new SqliteCommand(sql, conn);

        cmd.Parameters.AddRange(parameters.ToArray());

        using var reader = cmd.ExecuteReader();

        var result = new List<string>();

        while (reader.Read())
        {
            result.Add(
                reader[0]?.ToString() ?? "");
        }

        return result;
    }

    public List<DurabilityDisplayItem>
    GetDurabilityDisplayItems( Dictionary<string, object> conditions)
    {
        var filteredConditions = new Dictionary<string, object>(conditions);

        filteredConditions.Remove(Columns.DurabilityId);

        var (whereClause, parameters) = SearchConditionBuilder.Build(filteredConditions);

        string sql = $@"
SELECT DISTINCT
    t.taikyu_id,
    t.taikyu_code,
    t.purpose_name
{BaseFromClause}
WHERE 1 = 1
{whereClause}
ORDER BY t.taikyu_code
";

        using var conn = DbHelper.OpenConnection();

        using var cmd = new SqliteCommand(sql, conn);

        cmd.Parameters.AddRange(parameters.ToArray());

        using var reader = cmd.ExecuteReader();

        var result = new List<DurabilityDisplayItem>();

        while (reader.Read())
        {
            result.Add(new DurabilityDisplayItem
            {
                    TaikyuId = Convert.ToInt32(reader["taikyu_id"]),
                    Id = reader["taikyu_code"]
                            ?.ToString()
                            ?? "",

                    EvaluationPurpose = reader["purpose_name"]
                            ?.ToString()
                            ?? ""
                });
        }

        return result;
    }

    // 耐久IDごとの環境と機種（メーカー＋機種名）
    public Dictionary<string, (string Env, string Model)> GetDurabilityInfo(IEnumerable<string> taikyuCodes)
    {
        const string sql = @"
SELECT env, maker, model_name
FROM taikyu
WHERE taikyu_code = @taikyuCode
";

        var result = new Dictionary<string, (string Env, string Model)>();

        using var conn = DbHelper.OpenConnection();

        foreach (var taikyuCode in taikyuCodes)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taikyuCode", taikyuCode);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                continue;

            string env = reader["env"]?.ToString()?.Trim() ?? "";
            string model = string.Join(" ",
                new[] { reader["maker"]?.ToString(), reader["model_name"]?.ToString() }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim()));

            result[taikyuCode] = (env, model);
        }

        return result;
    }

    // 耐久（taikyu_id）ごとに使われたシリアル番号：現像器 TN1～4・ドラム DRM1～4・本体 Machine
    public Dictionary<int, HashSet<string>> GetDurabilitySerials()
    {
        const string sql = @"
SELECT DISTINCT
    taikyu_id, Machine, TN1, TN2, TN3, TN4, DRM1, DRM2, DRM3, DRM4
FROM measurement
";

        using var conn = DbHelper.OpenConnection();

        using var cmd = new SqliteCommand(sql, conn);

        using var reader = cmd.ExecuteReader();

        var result = new Dictionary<int, HashSet<string>>();

        while (reader.Read())
        {
            int taikyuId = Convert.ToInt32(reader[0]);

            if (!result.TryGetValue(taikyuId, out var serials))
            {
                serials = new HashSet<string>();
                result[taikyuId] = serials;
            }

            for (int i = 1; i < reader.FieldCount; i++)
            {
                string value = reader.IsDBNull(i) ? "" : reader[i].ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(value))
                    serials.Add(value);
            }
        }

        return result;
    }

    public List<IconItem> GetIconItems(Dictionary<string, object> conditions,string label1,string label2,string label3,
        int limit,string iconRootPath, HashSet<string> installedIconIds)
    {
        var (whereClause, parameters) = SearchConditionBuilder.Build(conditions);

        string col1 =
            DisplayFieldMap.ColumnMap.TryGetValue(
                label1,
                out var c1)
            ? c1
            : "''";

        string col2 =
            DisplayFieldMap.ColumnMap.TryGetValue(
                label2,
                out var c2)
            ? c2
            : "''";

        string col3 =
            DisplayFieldMap.ColumnMap.TryGetValue(
                label3,
                out var c3)
            ? c3
            : "''";

        string sql = $@"
SELECT
    i.image_id,
    i.image_name,
    t.taikyu_code,
    p.pattern_name,
    {col1},
    {col2},
    {col3},
    m.pages,
    m.measurement_id
{BaseFromClause}
WHERE 1=1
{whereClause}
ORDER BY t.taikyu_code, m.pages, m.measurement_id, p.""order"", i.image_id
LIMIT @limit
";

        using var conn = DbHelper.OpenConnection();

        using var cmd = new SqliteCommand(sql, conn);

        cmd.Parameters.AddRange(parameters.ToArray());

        cmd.Parameters.AddWithValue(
            "@limit",
            limit);

        using var reader = cmd.ExecuteReader();

        var result = new List<IconItem>();

        while (reader.Read())
        {
            string durabilityId =
                reader[2]?.ToString() ?? "";

            string imageName =
                reader[1]?.ToString() ?? "";

            bool hasIcon =
                installedIconIds.Contains(
                    durabilityId);

            string imagePath;

            if (hasIcon)
            {
                imagePath =
                    Path.Combine(
                        iconRootPath,
                        durabilityId,
                        imageName + ".jpg");
            }
            else
            {
                string pattern =
                    reader[3]?.ToString() ?? "";

                imagePath =
                    Path.Combine(
                        iconRootPath,
                        "_templates",
                        pattern + ".jpg");
            }

            result.Add(
                new IconItem
                {
                    ImageId =
                        reader[0]?.ToString() ?? "",

                    ImageName =
                        imageName,

                    DurabilityId =
                        durabilityId,

                    Pattern =
                        reader[3]?.ToString() ?? "",

                    ImagePath =
                        imagePath,

                    HasIcon =
                        hasIcon,

                    Item1 =
                        reader[4]?.ToString() ?? "",

                    Item2 =
                        reader[5]?.ToString() ?? "",

                    Item3 =
                        reader[6]?.ToString() ?? "",

                    Pages =
                        reader.IsDBNull(7)
                            ? null
                            : Convert.ToDouble(reader[7]),

                    MeasurementId =
                        reader.IsDBNull(8)
                            ? 0
                            : Convert.ToInt64(reader[8])
                });
        }

        return result;
    }
    public List<string> GetImageInfo(string imageId)
    {
        string sql = $@"
SELECT
    i.image_id,
    i.image_name,
    t.taikyu_code,
    t.purpose_name,
    t.maker,
    t.model_name,
    t.env,
    p.pattern_name,
    p.color,
    m.pages,
    m.cold_hot,
    m.measurement_datetime,
    m.Vb1,
    m.Vb2,
    m.Vb3,
    m.Vb4,

    m.TN1,
    m.TN2,
    m.TN3,
    m.TN4,

    m.DRM1,
    m.DRM2,
    m.DRM3,
    m.DRM4,

    m.QM1,
    m.QM2,
    m.QM3,
    m.QM4,

    m.MA1,
    m.MA2,
    m.MA3,
    m.MA4
{BaseFromClause}
WHERE i.image_id = @imageId
";

        using var conn =
            DbHelper.OpenConnection();

        using var cmd =
            new SqliteCommand(sql, conn);

        cmd.Parameters.AddWithValue(
            "@imageId",
            imageId);

        using var reader =
            cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new List<string>();
        }

        var result = new List<string>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            result.Add(
                $"{reader.GetName(i)}: {reader.GetValue(i)}");
        }
        var features = GetFeatureValues(imageId);

        if (features.Count > 0)
        {
            result.Add("");

            result.Add("===== Feature =====");

            foreach (var feature in features)
            {
                result.Add($"{feature.Name}: {feature.Value}");
            }
        }


        return result;
    }

    private List<(string Name, string Value)>
    GetFeatureValues(string imageId)
    {
        string sql = @"
SELECT
    f.feature_name,
    ifv.value
FROM image_feature_value ifv

INNER JOIN feature f
    ON ifv.feature_id = f.feature_id

WHERE ifv.image_id = @imageId

ORDER BY
    f.feature_name
";

        using var conn = DbHelper.OpenConnection();

        using var cmd = new SqliteCommand(sql, conn);

        cmd.Parameters.AddWithValue(
            "@imageId",
            imageId);

        using var reader = cmd.ExecuteReader();

        var result = new List<(string, string)>();

        while (reader.Read())
        {
            result.Add(
                (
                    reader["feature_name"]
                        ?.ToString()
                        ?? "",
                    reader["value"]
                        ?.ToString()
                        ?? ""
                ));
        }

        return result;
    }
}