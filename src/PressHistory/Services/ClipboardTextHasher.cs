using System.Security.Cryptography;
using System.Text;

namespace PressHistory.Services;

public static class ClipboardTextHasher
{
    public static string Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
