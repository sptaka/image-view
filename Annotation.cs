namespace SampleViewer.Models
{
    // 画像・測定・耐久に付けるメモ
    public class Annotation
    {
        public const string TargetImage = "image";
        public const string TargetMeasurement = "measurement";
        public const string TargetDurability = "taikyu";

        // 複数のPCのメモを後で統合しても重ならないよう、連番ではなくGUIDにする
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // 何に付けたメモか（image / measurement / taikyu）
        public string TargetType { get; set; } = TargetImage;

        public string DurabilityId { get; set; } = "";
        public long? MeasurementId { get; set; }
        public long? ImageId { get; set; }

        // メインのDBが無くても読めるよう、画像名と枚数も一緒に持つ
        public string ImageName { get; set; } = "";
        public double? Pages { get; set; }

        public string Category { get; set; } = "";
        public string Comment { get; set; } = "";

        public string Author { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string TargetLabel => TargetType switch
        {
            TargetMeasurement => Pages.HasValue ? $"測定（{Pages:#,0}枚）" : "測定",
            TargetDurability => "耐久",
            _ => "画像"
        };

        // 編集・削除できるのは書いた本人だけ
        public bool IsMine => string.Equals(Author, Environment.UserName, StringComparison.OrdinalIgnoreCase);

        public string Footer => $"{Author}  {UpdatedAt:yyyy/MM/dd HH:mm}";

        public string Summary => $"［{Category}］{Comment}（{Author}）";
    }

    // メモを対象ごとに引けるようにまとめたもの（検索のたびに作り直さず、変更時だけ読み直す）
    public class AnnotationIndex
    {
        private readonly Dictionary<long, List<Annotation>> _byImage = new();
        private readonly Dictionary<long, List<Annotation>> _byMeasurement = new();
        private readonly Dictionary<string, List<Annotation>> _byDurability = new();

        public IReadOnlyList<Annotation> All { get; }

        public AnnotationIndex(IReadOnlyList<Annotation> annotations)
        {
            All = annotations;

            foreach (var annotation in annotations)
            {
                if (annotation.TargetType == Annotation.TargetImage && annotation.ImageId.HasValue)
                    Add(_byImage, annotation.ImageId.Value, annotation);
                else if (annotation.TargetType == Annotation.TargetMeasurement && annotation.MeasurementId.HasValue)
                    Add(_byMeasurement, annotation.MeasurementId.Value, annotation);

                Add(_byDurability, annotation.DurabilityId, annotation);
            }
        }

        private static void Add<TKey>(Dictionary<TKey, List<Annotation>> map, TKey key, Annotation annotation)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<Annotation>();

            list.Add(annotation);
        }

        // その画像に付いたメモと、その画像の測定に付いたメモ
        public List<Annotation> ForImage(long imageId, long measurementId)
        {
            var result = new List<Annotation>();

            if (_byImage.TryGetValue(imageId, out var image))
                result.AddRange(image);

            if (_byMeasurement.TryGetValue(measurementId, out var measurement))
                result.AddRange(measurement);

            return result;
        }

        // その耐久に関係するすべてのメモ（画像・測定・耐久）
        public IReadOnlyList<Annotation> ForDurability(string durabilityId)
        {
            return _byDurability.TryGetValue(durabilityId, out var list) ? list : Array.Empty<Annotation>();
        }

        // 耐久そのものに付いたメモ
        public List<Annotation> DurabilityLevel(string durabilityId)
        {
            return ForDurability(durabilityId).Where(x => x.TargetType == Annotation.TargetDurability).ToList();
        }
    }
}
