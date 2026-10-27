using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AIGameFramework.Architecture
{
    public static class ArchitectureStore
    {
        public const string AssetPath = "Assets/Architecture/architecture.json";

        public static DateTime LastWriteTimeUtc()
        {
            var path = AbsolutePath();
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        public static ArchitectureDocument Load(out string error)
        {
            error = null;
            var absolutePath = AbsolutePath();
            if (!File.Exists(absolutePath))
            {
                var empty = ArchitectureModel.CreateEmpty();
                Save(empty);
                return empty;
            }

            try
            {
                var document = JsonUtility.FromJson<ArchitectureDocument>(File.ReadAllText(absolutePath, Encoding.UTF8));
                ArchitectureModel.UpgradeLegacyText(document);
                if (document != null && document.calls == null)
                    document.calls = new System.Collections.Generic.List<InterfaceCall>();
                var errors = ArchitectureModel.Validate(document);
                if (errors.Count > 0)
                {
                    error = string.Join("\n", errors);
                    return null;
                }
                return document;
            }
            catch (Exception exception)
            {
                error = "架构文件读取失败：" + exception.Message;
                return null;
            }
        }

        public static void Save(ArchitectureDocument document)
        {
            var errors = ArchitectureModel.Validate(document);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

            var absolutePath = AbsolutePath();
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(document, true) + "\n", new UTF8Encoding(false));
        }

        private static string AbsolutePath()
        {
            return Path.Combine(Application.dataPath, "Architecture", "architecture.json");
        }
    }
}
