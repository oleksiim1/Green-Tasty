using System.Text;
using System.Text.Json;

namespace Restaurant_Aqa_Framework.Support;

/// <summary>Reads the <c>sub</c> claim from a Cognito id token. The sign-in response does not return the user id
/// on its own, and <c>GET /waiters/{userId}/location</c> needs it.</summary>
public static class JwtSubject
{
    public static string Read(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("The token is not a JWT.");

        var json = Encoding.UTF8.GetString(Decode(parts[1]));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("sub", out var subject) && subject.GetString() is { Length: > 0 } value)
            return value;

        throw new InvalidOperationException("The token has no subject claim.");
    }

    private static byte[] Decode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - (padded.Length % 4)) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
