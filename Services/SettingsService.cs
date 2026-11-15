using System.Text.Json.Serialization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace SampleViewer.Services
{
    public class SettingsService
    {

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            Converters ={new JsonStringEnumConverter()}
        };

        // ✅ 読み込み
        public static T Load<T>(string path) where T : new()
        {
            try
            {
                if (!File.Exists(path))
                {
                    // ファイルが無い場合はデフォルト返す
                    return new T();
                }

                var json = File.ReadAllText(path);

                var data = JsonSerializer.Deserialize<T>(json,_options);

                return data ?? new T();
            }
            catch (Exception ex)
            {
                throw new Exception($"JSON読み込み失敗: {path}", ex);
            }
        }

        // ✅ 保存
        // 既定値のファイルに、利用者のファイルにある項目を上書きして読み込む
        // アプリ更新で項目が増えても、利用者の設定は残り、新しい項目だけ既定値になる
        // 利用者のファイルが無い・壊れている場合は既定値のまま
        public static T LoadWithDefaults<T>(string path, string defaultPath) where T : new()
        {
            var merged = JsonNode.Parse(File.ReadAllText(defaultPath)) as JsonObject ?? new JsonObject();

            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject user)
                {
                    foreach (var item in user.ToList())
                    {
                        user.Remove(item.Key);
                        merged[item.Key] = item.Value;
                    }
                }

                return merged.Deserialize<T>(_options) ?? new T();
            }
            catch (JsonException)
            {
                return Load<T>(defaultPath);
            }
        }

        public static void Save<T>(string path, T data)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(data, _options);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"JSON保存失敗: {path}", ex);
            }
        }



        
        

    }
}
