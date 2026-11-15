using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Services;

namespace SampleViewer.Repositories
{
    // 限度見本：特徴量ごとに「ここまでは許容」を示す基準の画像
    // DBに limit_sample テーブルがあればそれを使い、無ければテスト用の定義ファイルを使う
    // （本番の見本ができたら、DBにテーブルを追加するだけで切り替わる）
    public class LimitSampleRepository
    {
        public bool HasTable()
        {
            using var conn = DbHelper.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'limit_sample'", conn);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        // DBの limit_sample テーブルから読む。見本の画像は、通常の画像と同じく image に登録されている前提
        public List<LimitSample> GetFromDatabase()
        {
            const string sql = @"
SELECT f.feature_name, p.pattern_name, ls.grade, ls.model_name, ls.comment, t.taikyu_code, i.image_name
FROM limit_sample ls
INNER JOIN feature f ON ls.feature_id = f.feature_id
INNER JOIN pattern p ON f.pattern_id = p.pattern_id
INNER JOIN image i ON ls.image_id = i.image_id
INNER JOIN measurement m ON i.measurement_id = m.measurement_id
INNER JOIN taikyu t ON m.taikyu_id = t.taikyu_id
ORDER BY ls.limit_sample_id
";
            using var conn = DbHelper.OpenConnection();
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            var result = new List<LimitSample>();

            while (reader.Read())
            {
                result.Add(new LimitSample
                {
                    FeatureName = reader.GetString(0),
                    PatternName = reader.GetString(1),
                    Grade = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ModelName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    DurabilityId = reader.GetString(5),
                    ImageName = reader.GetString(6)
                });
            }

            return result;
        }

        // 定義ファイルの見本に、特徴量が属するパターンを補う（特徴量はパターン固有）
        public void FillPatternNames(List<LimitSample> samples)
        {
            using var conn = DbHelper.OpenConnection();

            foreach (var sample in samples)
            {
                using var cmd = new SqliteCommand(@"
SELECT p.pattern_name
FROM feature f
INNER JOIN pattern p ON f.pattern_id = p.pattern_id
WHERE f.feature_name = @featureName
", conn);
                cmd.Parameters.AddWithValue("@featureName", sample.FeatureName);

                sample.PatternName = cmd.ExecuteScalar()?.ToString() ?? "";
            }
        }
    }
}
