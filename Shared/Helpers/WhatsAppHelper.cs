using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionCommerciale.Shared.Helpers;

public static class WhatsAppHelper
{
    /// <summary>
    /// Builds a wa.me URL. Returns null if the phone cannot be normalized.
    /// Default country code 212 (Morocco) when the number starts with 0.
    /// </summary>
    public static string? BuildChatUrl(string? phone, string? prefillMessage = null, string defaultCountryCode = "212")
    {
        var digits = NormalizePhoneDigits(phone, defaultCountryCode);
        if (digits is null)
            return null;

        var url = new StringBuilder("https://wa.me/");
        url.Append(digits);
        if (!string.IsNullOrWhiteSpace(prefillMessage))
        {
            url.Append("?text=");
            url.Append(Uri.EscapeDataString(prefillMessage.Trim()));
        }

        return url.ToString();
    }

    public static string? NormalizePhoneDigits(string? phone, string defaultCountryCode = "212")
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var digits = Regex.Replace(phone, @"\D", string.Empty);
        if (digits.Length == 0)
            return null;

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.StartsWith('0') && !string.IsNullOrWhiteSpace(defaultCountryCode))
            digits = defaultCountryCode + digits[1..];

        // WhatsApp expects international format without + ; typically 8–15 digits.
        if (digits.Length is < 8 or > 15)
            return null;

        return digits;
    }

    public static bool TryOpenChat(string? phone, out string? errorKey, string? prefillMessage = null)
    {
        errorKey = null;
        var url = BuildChatUrl(phone, prefillMessage);
        if (url is null)
        {
            errorKey = "WhatsApp_ErrPhone";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            errorKey = "WhatsApp_ErrOpen";
            return false;
        }
    }
}
