using SampleViewer.Models;

namespace SampleViewer.Services
{

    public static class CompareAligner
    {
        private readonly record struct SlotKey(double Pages, string Pattern, int Occurrence);

        public static List<List<CompareCell>> Build(
            IReadOnlyList<IReadOnlyList<IconItem>> rows,
            bool syncMode,
            bool commonOnly,
            double? tolerance)
        {
            var sorted = rows
                .Select(r => r
                    .OrderBy(x => x.Pages ?? double.MaxValue)
                    .ThenBy(x => x.Pattern, StringComparer.Ordinal)
                    .ThenBy(x => x.ImageName, StringComparer.Ordinal)
                    .ToList())
                .ToList();

            if (commonOnly && sorted.Count > 1)
            {
                var common = sorted
                    .Select(r => r
                        .Where(x => x.Pages.HasValue)
                        .Select(x => (x.Pages!.Value, x.Pattern))
                        .ToHashSet())
                    .Aggregate((a, b) =>
                    {
                        a.IntersectWith(b);
                        return a;
                    });

                sorted = sorted
                    .Select(r => r
                        .Where(x => x.Pages.HasValue && common.Contains((x.Pages.Value, x.Pattern)))
                        .ToList())
                    .ToList();
            }

            if (!syncMode)
            {
                return sorted
                    .Select(r => r.Select(x => CompareCell.Exact(x, x.Pages)).ToList())
                    .ToList();
            }

            var keyedRows = sorted.Select(BuildKeyMap).ToList();

            var axis = keyedRows
                .SelectMany(d => d.Keys)
                .Distinct()
                .OrderBy(k => k.Pages)
                .ThenBy(k => k.Pattern, StringComparer.Ordinal)
                .ThenBy(k => k.Occurrence)
                .ToList();

            var result = new List<List<CompareCell>>();

            for (int r = 0; r < sorted.Count; r++)
            {
                var keyed = keyedRows[r];
                var nearestIndex = new NearestIndex(sorted[r]);

                var cells = new List<CompareCell>(axis.Count);

                foreach (var key in axis)
                {
                    if (keyed.TryGetValue(key, out var icon))
                    {
                        cells.Add(CompareCell.Exact(icon, key.Pages));
                        continue;
                    }

                    var nearest = nearestIndex.Find(key.Pattern, key.Pages);

                    if (nearest != null &&
                        (tolerance == null || Math.Abs(nearest.Pages!.Value - key.Pages) <= tolerance))
                    {
                        cells.Add(new CompareCell
                        {
                            Icon = nearest,
                            AxisPages = key.Pages,
                            IsApproximate = true
                        });
                    }
                    else
                    {
                        cells.Add(new CompareCell { AxisPages = key.Pages });
                    }
                }

                result.Add(cells);
            }

            return result;
        }

        private static Dictionary<SlotKey, IconItem> BuildKeyMap(List<IconItem> row)
        {
            var map = new Dictionary<SlotKey, IconItem>();
            var counter = new Dictionary<(double, string), int>();

            foreach (var icon in row)
            {
                if (!icon.Pages.HasValue)
                    continue;

                var baseKey = (icon.Pages.Value, icon.Pattern);
                counter.TryGetValue(baseKey, out int occurrence);
                counter[baseKey] = occurrence + 1;

                map[new SlotKey(icon.Pages.Value, icon.Pattern, occurrence)] = icon;
            }

            return map;
        }

        /// <summary>
        /// 同じパターンの中で、指定枚数に最も近い画像を二分探索で引く
        /// </summary>
        private class NearestIndex
        {
            private readonly Dictionary<string, (double[] Pages, IconItem[] Icons)> _byPattern;

            public NearestIndex(List<IconItem> sortedRow)
            {
                _byPattern = sortedRow
                    .Where(x => x.Pages.HasValue)
                    .GroupBy(x => x.Pattern)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var icons = g.OrderBy(x => x.Pages!.Value).ToArray();
                            return (icons.Select(x => x.Pages!.Value).ToArray(), icons);
                        });
            }

            public IconItem? Find(string pattern, double pages)
            {
                if (!_byPattern.TryGetValue(pattern, out var entry) || entry.Pages.Length == 0)
                    return null;

                int index = Array.BinarySearch(entry.Pages, pages);

                if (index >= 0)
                    return entry.Icons[index];

                int upper = ~index;
                int lower = upper - 1;

                if (upper >= entry.Pages.Length)
                    return entry.Icons[lower];

                if (lower < 0)
                    return entry.Icons[upper];

                // 等距離なら少ない枚数側を採用
                return pages - entry.Pages[lower] <= entry.Pages[upper] - pages
                    ? entry.Icons[lower]
                    : entry.Icons[upper];
            }
        }
    }
}
