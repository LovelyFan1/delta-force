using System.Text.Json;

namespace DAS.Core;

/// <summary>流式队列读取：一次一行，不预读全表。</summary>
public sealed class QueueEngine : IDisposable
{
    private readonly StreamReader _reader;
    private readonly string _format;

    public QueueEngine(string path)
    {
        _format = Path.GetExtension(path).ToLowerInvariant();
        _reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
    }

    public IEnumerable<Account> Read()
    {
        if (_format == ".json") { foreach (var a in ReadJson()) yield return a; yield break; }

        string? line;
        while ((line = _reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (_format == ".csv")
            {
                var c = line.Split(',');
                if (c.Length < 2 || c[0].Equals("user", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new Account(c[0].Trim(), c[1].Trim(), c.Length > 2 ? c[2].Trim() : null);
            }
            else
            {
                var i = line.IndexOf("----", StringComparison.Ordinal);
                if (i <= 0) continue;
                yield return new Account(line[..i].Trim(), line[(i + 4)..].Trim());
            }
        }
    }

    private IEnumerable<Account> ReadJson()
    {
        var doc = JsonDocument.Parse(_reader.ReadToEnd());
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var user = e.GetProperty("user").GetString() ?? "";
            var pass = e.GetProperty("pass").GetString() ?? "";
            var tag = e.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (user.Length == 0) continue;
            yield return new Account(user, pass, tag);
        }
    }

    public void Dispose() => _reader.Dispose();
}
