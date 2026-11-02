using Microsoft.Data.Sqlite;
using SampleViewer.Models;
using SampleViewer.Services;

namespace SampleViewer.Repositories;

public class DisplayFieldRepository
{
    public List<DisplayField> GetAll()
    {
        const string sql = @"
SELECT
    display_field_id,
    control_name,
    display_name,
    db_column,
    category,
    display_order,
    is_searchable,
    is_info_label,
    is_visible,
    comment
FROM display_field
ORDER BY display_order";

        using var conn =
            DbHelper.OpenConnection();

        using var cmd =
            new SqliteCommand(sql, conn);

        using var reader =
            cmd.ExecuteReader();

        var result =
            new List<DisplayField>();

        while (reader.Read())
        {
            result.Add(
                new DisplayField
                {
                    DisplayFieldId =
                        Convert.ToInt32(
                            reader["display_field_id"]),
                    ControlName = reader["control_name"] ?.ToString() ?? "",

                    DisplayName =
                        reader["display_name"]
                            ?.ToString()
                            ?? "",

                    DbColumn =
                        reader["db_column"]
                            ?.ToString()
                            ?? "",

                    Category =
                        reader["category"]
                            ?.ToString()
                            ?? "",

                    DisplayOrder =
                        Convert.ToInt32(
                            reader["display_order"]),

                    IsSearchable =
                        Convert.ToInt32(
                            reader["is_searchable"]) == 1,

                    IsInfoLabel =
                        Convert.ToInt32(
                            reader["is_info_label"]) == 1,

                    IsVisible =
                        Convert.ToInt32(
                            reader["is_visible"]) == 1,

                    Comment =
                        reader["comment"]
                            ?.ToString()
                            ?? ""
                });
        }

        return result;
    }

    public List<DisplayField> GetInfoLabelFields()
    {
        return GetAll()
            .Where(x =>
                x.IsInfoLabel &&
                x.IsVisible)
            .OrderBy(x =>
                x.DisplayOrder)
            .ToList();
    }
    public List<DisplayField> GetSearchControls()
    {
        return GetAll()
            .Where(x =>
                x.IsSearchable &&
                x.IsVisible)
            .OrderBy(x =>
                x.DisplayOrder)
            .ToList();
    }

    public List<DisplayField> GetSearchableFields()
    {
        return GetAll()
            .Where(x =>
                x.IsSearchable &&
                x.IsVisible)
            .OrderBy(x =>
                x.DisplayOrder)
            .ToList();
    }

    public Dictionary<string, string> GetColumnMap()
    {
        return GetAll()
            .Where(x =>
                x.IsVisible)
            .ToDictionary(
                x => x.DisplayName,
                x => x.DbColumn);
    }
}
