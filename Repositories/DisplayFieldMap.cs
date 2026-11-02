using SampleViewer.Repositories;

namespace SampleViewer.Repositories;

public static class DisplayFieldMap
{
    private static Dictionary<string, string>? _cache;

    public static Dictionary<string, string> ColumnMap
    {
        get
        {
            if (_cache == null)
            {
                var repo =
                    new DisplayFieldRepository();

                _cache =
                    repo.GetColumnMap();
            }

            return _cache;
        }
    }

    public static void Reload()
    {
        _cache = null;
    }
}