using System.IO;
using Newtonsoft.Json;

namespace ETABSModelDefinitionValidator.Core.Configuration
{
    public static class ConfigurationLoader
    {
        // ObjectCreationHandling.Replace is mandatory here: several config properties have
        // non-empty default values from their property initializers (e.g. WallRulesConfig's
        // CheckedModifiers). Newtonsoft's default behavior is to re-use and APPEND to an
        // existing list/dictionary instance rather than replace it, which silently duplicates
        // every default entry with whatever the JSON file also specifies for it.
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static ValidationProfileConfig LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ValidationProfileConfig>(json, Settings)
                   ?? throw new InvalidDataException($"Configuration file '{path}' deserialized to null.");
        }

        public static ValidationProfileConfig LoadDefault()
        {
            var config = new ValidationProfileConfig
            {
                Slabs = SlabRulesConfig.Default(),
                RebarMaterials = RebarMaterialRulesConfig.Default()
            };
            return config;
        }

        public static void SaveToFile(ValidationProfileConfig config, string path)
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
