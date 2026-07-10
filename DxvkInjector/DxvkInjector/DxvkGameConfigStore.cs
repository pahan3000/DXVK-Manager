using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// Persists per-game DxvkGameConfig as a single JSON dictionary file under the
    /// plugin's own user data folder (Playnite gives every plugin one via
    /// GetPluginUserDataPath(), so this doesn't touch Playnite's own database).
    /// </summary>
    public class DxvkGameConfigStore
    {
        private readonly string filePath;
        private Dictionary<string, DxvkGameConfig> cache;
        private readonly object sync = new object();

        public DxvkGameConfigStore(string pluginUserDataPath)
        {
            filePath = Path.Combine(pluginUserDataPath, "dxvk_game_configs.json");
        }

        private void EnsureLoaded()
        {
            if (cache != null) return;

            try
            {
                cache = File.Exists(filePath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, DxvkGameConfig>>(File.ReadAllText(filePath))
                      ?? new Dictionary<string, DxvkGameConfig>()
                    : new Dictionary<string, DxvkGameConfig>();
            }
            catch
            {
                // Corrupt/unreadable file — start fresh rather than crashing the plugin.
                cache = new Dictionary<string, DxvkGameConfig>();
            }
        }

        public DxvkGameConfig Load(Guid gameId)
        {
            lock (sync)
            {
                EnsureLoaded();
                return cache.TryGetValue(gameId.ToString(), out var cfg) ? cfg : null;
            }
        }

        public void Save(Guid gameId, DxvkGameConfig config)
        {
            lock (sync)
            {
                EnsureLoaded();
                cache[gameId.ToString()] = config;
                Persist();
            }
        }

        public void Remove(Guid gameId)
        {
            lock (sync)
            {
                EnsureLoaded();
                cache.Remove(gameId.ToString());
                Persist();
            }
        }

        private void Persist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(cache, Formatting.Indented));
        }
    }
}
