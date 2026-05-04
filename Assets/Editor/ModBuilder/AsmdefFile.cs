#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Round-trippable view of an <c>*.asmdef</c> file. Uses <see cref="JObject"/> under the hood
    /// so unknown fields (e.g. <c>versionDefines</c>) are preserved on write.
    /// </summary>
    public sealed class AsmdefFile
    {
        private readonly JObject _root;

        private AsmdefFile(JObject root)
        {
            _root = root;
        }

        public string AbsolutePath { get; private set; } = string.Empty;

        public string Name
        {
            get => _root.Value<string>("name") ?? string.Empty;
        }

        public bool OverrideReferences
        {
            get => _root.Value<bool?>("overrideReferences") ?? false;
            set => _root["overrideReferences"] = value;
        }

        public bool AutoReferenced
        {
            get => _root.Value<bool?>("autoReferenced") ?? true;
            set => _root["autoReferenced"] = value;
        }

        public List<string> PrecompiledReferences
        {
            get
            {
                var arr = _root["precompiledReferences"] as JArray;
                return arr?.Select(t => t.ToString()).ToList() ?? new List<string>();
            }
            set
            {
                _root["precompiledReferences"] = new JArray(value);
            }
        }

        public static AsmdefFile Load(string absolutePath)
        {
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"asmdef not found: {absolutePath}", absolutePath);
            var text = File.ReadAllText(absolutePath);
            var root = JObject.Parse(text);
            return new AsmdefFile(root) { AbsolutePath = absolutePath };
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(AbsolutePath))
                throw new InvalidOperationException("AsmdefFile has no AbsolutePath; cannot Save().");
            var text = _root.ToString(Formatting.Indented);
            File.WriteAllText(AbsolutePath, text);
        }
    }
}
