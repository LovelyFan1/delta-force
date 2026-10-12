using Microsoft.Win32;

namespace DAS.Platform;

/// <summary>三路探测：注册表 → 本地配置 → 进程命令行；命中即停并缓存。</summary>
public sealed class DirResolver
{
    private static readonly string[] RegRoots =
    {
        @"Software\Tencent\WeGame",
        @"SOFTWARE\WOW6432Node\Tencent\WeGame"
    };

    private readonly string _cachePath;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DirResolver(string cachePath)
    {
        _cachePath = cachePath;
        if (File.Exists(cachePath))
            _cache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(cachePath)) ?? new();
    }

    public string? Resolve(string game)
    {
        if (_cache.TryGetValue(game, out var hit) && Directory.Exists(hit)) return hit;

        var path = ProbeRegistry(game) ?? ProbeConfig() ?? ProbeProcess(game);
        if (path is null) return null;

        _cache[game] = path;
        File.WriteAllText(_cachePath, System.Text.Json.JsonSerializer.Serialize(_cache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string? ProbeRegistry(string game)
    {
        foreach (var root in RegRoots)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(root);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var g = key.OpenSubKey(sub);
                    var install = g?.GetValue("InstallPath") as string;
                    if (install is not null && Directory.Exists(install) && sub.Contains(game, StringComparison.OrdinalIgnoreCase))
                        return Normalize(install);
                }
            }
        }
        return null;
    }

    private static string? ProbeConfig()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "WeGame"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tencent", "WeGame")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var cfg in Directory.EnumerateFiles(root, "*.cfg", SearchOption.AllDirectories).Take(32))
            {
                foreach (var line in File.ReadLines(cfg))
                {
                    var idx = line.IndexOf("InstallPath", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    var value = line[(line.IndexOf('=', idx) is var e and > 0 ? e + 1 : idx)..].Trim().Trim('"');
                    if (Directory.Exists(value)) return Normalize(value);
                }
            }
        }
        return null;
    }

    private static string? ProbeProcess(string game)
    {
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (!p.ProcessName.Contains(game, StringComparison.OrdinalIgnoreCase)) continue;
                var dir = Path.GetDirectoryName(p.MainModule?.FileName);
                if (dir is not null && Directory.Exists(dir)) return Normalize(dir);
            }
            catch { /* 无权限访问的进程直接跳过 */ }
        }
        return null;
    }

    /// <summary>中文/空格路径统一走长路径前缀，避免 260 截断。</summary>
    private static string Normalize(string path) => path.StartsWith(@"\\?\") ? path : @"\\?\" + path;
}
