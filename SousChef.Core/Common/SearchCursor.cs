using System.Text;
using System.Text.Json;

namespace SousChef.Core.Common;

public record SearchCursor(string? Title, float? Distance, Guid Id)
{
    public string Encode()
    {
        var json = JsonSerializer.Serialize(this);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static SearchCursor? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return JsonSerializer.Deserialize<SearchCursor>(json);
        }
        catch { return null; }
    }
}
