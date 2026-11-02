using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Services;
using System.Diagnostics;

namespace SampleViewer.Repositories;

public class TrendGraphRepository
{
    public List<TrendPoint> GetMeasurementTrend(IEnumerable<string> taikyuCodes, string measurementColumn)
    {
        var validColumns = new[]
        {
        "Vb1","Vb2","Vb3","Vb4",
        "TN1","TN2","TN3","TN4",
        "DRM1","DRM2","DRM3","DRM4",
        "QM1","QM2","QM3","QM4",
        "MA1","MA2","MA3","MA4"
    };

        if (!validColumns.Contains(measurementColumn))
        {
            throw new ArgumentException(
                $"Invalid measurement column : {measurementColumn}");
        }

        var result = new List<TrendPoint>();

        using var conn = DbHelper.OpenConnection();

        foreach (var taikyuCode in taikyuCodes)
        {
            string sql = $@"
SELECT
    m.pages,
    m.{measurementColumn} AS value,
    m.measurement_id
FROM measurement m
INNER JOIN taikyu t
    ON m.taikyu_id = t.taikyu_id
WHERE
    t.taikyu_code = @taikyuCode
ORDER BY m.pages
";

            using var cmd =
                new SqliteCommand(sql, conn);

            cmd.Parameters.AddWithValue(
                "@taikyuCode",
                taikyuCode);

            using var reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                if (reader["pages"] == DBNull.Value)
                    continue;

                if (reader["value"] == DBNull.Value)
                    continue;

                // 測定値は測定ごとの値なので画像とは結び付けない（1測定に1点）
                result.Add(
                    new TrendPoint
                    {
                        Pages =
                            Convert.ToDouble(reader["pages"]),

                        Value =
                            Convert.ToDouble(reader["value"]),

                        MeasurementId =
                            Convert.ToInt64(
                                reader["measurement_id"]),

                        SeriesName = $"{taikyuCode}:{measurementColumn}"
                    });
            }
        }

        return result;
    }

    public List<TrendPoint> GetFeatureTrend(IEnumerable<string> taikyuCodes, string featureName)
    {
        var result = new List<TrendPoint>();

        using var conn = DbHelper.OpenConnection();

        foreach (var taikyuCode in taikyuCodes)
        {
            const string sql = @"
SELECT
    m.pages,
    ifv.value,
    m.measurement_id,
    i.image_id,
    i.image_name
FROM taikyu t
INNER JOIN measurement m
    ON t.taikyu_id = m.taikyu_id
INNER JOIN image i
    ON m.measurement_id = i.measurement_id
INNER JOIN image_feature_value ifv
    ON i.image_id = ifv.image_id
INNER JOIN feature f
    ON ifv.feature_id = f.feature_id
WHERE
    t.taikyu_code = @taikyuCode
    AND f.feature_name = @featureName
ORDER BY
    m.pages
";

            using var cmd =
                new SqliteCommand(sql, conn);

            cmd.Parameters.AddWithValue(
                "@taikyuCode",
                taikyuCode);

            cmd.Parameters.AddWithValue(
                "@featureName",
                featureName);

            using var reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                if (reader["pages"] == DBNull.Value)
                    continue;

                if (reader["value"] == DBNull.Value)
                    continue;

                result.Add(
                    new TrendPoint
                    {
                        Pages =
                            Convert.ToDouble(
                                reader["pages"]),

                        Value =
                            Convert.ToDouble(
                                reader["value"]),

                        MeasurementId =
                            Convert.ToInt64(
                                reader["measurement_id"]),

                        ImageId =
                            Convert.ToInt64(
                                reader["image_id"]),

                        ImageName =
                            reader["image_name"]
                            ?.ToString() ?? "",

                        SeriesName =
                            $"{taikyuCode}:{featureName}",

                        IsFeature = true
                    });
            }
        }

        return result;
    }

    public Dictionary<string, List<DeveloperChange>> GetDeveloperChanges(IEnumerable<string> taikyuCodes)
    {
        const string sql = @"
SELECT m.pages, m.TN1, m.TN2, m.TN3, m.TN4
FROM measurement m
INNER JOIN taikyu t
    ON m.taikyu_id = t.taikyu_id
WHERE
    t.taikyu_code = @taikyuCode
    AND m.pages IS NOT NULL
ORDER BY m.pages, m.measurement_id
";

        var result = new Dictionary<string, List<DeveloperChange>>();

        using var conn = DbHelper.OpenConnection();

        foreach (var taikyuCode in taikyuCodes)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taikyuCode", taikyuCode);

            using var reader = cmd.ExecuteReader();

            var last = new string?[4];
            var changes = new List<DeveloperChange>();

            while (reader.Read())
            {
                double pages = Convert.ToDouble(reader["pages"]);

                for (int i = 0; i < 4; i++)
                {
                    string serial = reader[i + 1]?.ToString()?.Trim() ?? "";

                    if (serial.Length == 0)
                        continue;

                    if (last[i] != null && last[i] != serial)
                    {
                        var change = changes.LastOrDefault();
                        if (change == null || change.Pages != pages)
                        {
                            change = new DeveloperChange { Pages = pages };
                            changes.Add(change);
                        }

                        change.Developers.Add($"TN{i + 1}");
                        change.Details.Add($"TN{i + 1}: {last[i]} → {serial}");
                    }

                    last[i] = serial;
                }
            }

            result[taikyuCode] = changes;
        }

        return result;
    }


    public List<string> GetFeatureNames(string? patternName = null)
    {
        string sql = string.IsNullOrWhiteSpace(patternName)
            ? @"
SELECT feature_name
FROM feature
ORDER BY feature_name
"
            : @"
SELECT f.feature_name
FROM feature f
INNER JOIN pattern p
    ON f.pattern_id = p.pattern_id
WHERE p.pattern_name = @patternName
ORDER BY f.feature_id
";

        using var conn = DbHelper.OpenConnection();
        using var cmd = new SqliteCommand(sql, conn);

        if (!string.IsNullOrWhiteSpace(patternName))
            cmd.Parameters.AddWithValue("@patternName", patternName);

        using var reader = cmd.ExecuteReader();

        var result = new List<string>();

        while (reader.Read())
        {
            result.Add(
                reader["feature_name"]?.ToString()
                ?? "");
        }

        return result;
    }
}
