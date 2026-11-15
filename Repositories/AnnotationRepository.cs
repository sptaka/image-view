using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using System.Globalization;
using System.IO;

namespace SampleViewer.Repositories
{
    // メモの保存先。メインのDB（配布で丸ごと入れ替わる）とは別のDBに持つ
    // 削除は行を消さずに印を付ける（後で他のPCのメモと統合するとき、削除したことを伝えられるように）
    public class AnnotationRepository
    {
        private readonly string _connectionString;

        public AnnotationRepository(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath}";

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS annotation (
    annotation_id  TEXT PRIMARY KEY,
    target_type    TEXT NOT NULL,
    taikyu_code    TEXT NOT NULL,
    measurement_id INTEGER,
    image_id       INTEGER,
    image_name     TEXT NOT NULL DEFAULT '',
    pages          REAL,
    category       TEXT NOT NULL,
    comment        TEXT NOT NULL,
    author         TEXT NOT NULL,
    machine        TEXT NOT NULL,
    created_at     TEXT NOT NULL,
    updated_at     TEXT NOT NULL,
    is_deleted     INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_annotation_taikyu_code ON annotation (taikyu_code);
";
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public List<Annotation> GetAll()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT annotation_id, target_type, taikyu_code, measurement_id, image_id, image_name, pages,
       category, comment, author, machine, created_at, updated_at
FROM annotation
WHERE is_deleted = 0
ORDER BY created_at
";
            using var reader = cmd.ExecuteReader();

            var result = new List<Annotation>();

            while (reader.Read())
            {
                result.Add(new Annotation
                {
                    Id = reader.GetString(0),
                    TargetType = reader.GetString(1),
                    DurabilityId = reader.GetString(2),
                    MeasurementId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    ImageId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    ImageName = reader.GetString(5),
                    Pages = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    Category = reader.GetString(7),
                    Comment = reader.GetString(8),
                    Author = reader.GetString(9),
                    Machine = reader.GetString(10),
                    CreatedAt = ParseTime(reader.GetString(11)),
                    UpdatedAt = ParseTime(reader.GetString(12))
                });
            }

            return result;
        }

        // 新規は追加、既存（同じID）は内容と更新日時を書き換える
        public void Save(Annotation annotation)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO annotation
    (annotation_id, target_type, taikyu_code, measurement_id, image_id, image_name, pages,
     category, comment, author, machine, created_at, updated_at, is_deleted)
VALUES
    (@id, @targetType, @taikyuCode, @measurementId, @imageId, @imageName, @pages,
     @category, @comment, @author, @machine, @createdAt, @updatedAt, 0)
ON CONFLICT (annotation_id) DO UPDATE SET
    category = excluded.category,
    comment = excluded.comment,
    updated_at = excluded.updated_at,
    is_deleted = 0
";
            cmd.Parameters.AddWithValue("@id", annotation.Id);
            cmd.Parameters.AddWithValue("@targetType", annotation.TargetType);
            cmd.Parameters.AddWithValue("@taikyuCode", annotation.DurabilityId);
            cmd.Parameters.AddWithValue("@measurementId", (object?)annotation.MeasurementId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@imageId", (object?)annotation.ImageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@imageName", annotation.ImageName);
            cmd.Parameters.AddWithValue("@pages", (object?)annotation.Pages ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@category", annotation.Category);
            cmd.Parameters.AddWithValue("@comment", annotation.Comment);
            cmd.Parameters.AddWithValue("@author", annotation.Author);
            cmd.Parameters.AddWithValue("@machine", annotation.Machine);
            cmd.Parameters.AddWithValue("@createdAt", FormatTime(annotation.CreatedAt));
            cmd.Parameters.AddWithValue("@updatedAt", FormatTime(annotation.UpdatedAt));
            cmd.ExecuteNonQuery();
        }

        public void Delete(string id)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE annotation SET is_deleted = 1, updated_at = @updatedAt WHERE annotation_id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@updatedAt", FormatTime(DateTime.Now));
            cmd.ExecuteNonQuery();
        }

        private static string FormatTime(DateTime time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static DateTime ParseTime(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : DateTime.MinValue;
    }
}
