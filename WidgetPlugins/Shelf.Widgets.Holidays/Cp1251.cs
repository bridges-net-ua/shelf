using System.Text;

namespace Shelf.Widgets.Holidays;

/// <summary>
/// Minimal Windows-1251 (Cyrillic Windows) decoder. Used as a fallback for
/// text files that aren't valid UTF-8 - the typical case for `.txt` files
/// saved by Ukrainian Windows apps with the default ANSI codepage.
///
/// Hardcoded to keep the plugin a single DLL: the .NET 8 desktop runtime
/// ships only UTF and ASCII encodings; using <c>Encoding.GetEncoding(1251)</c>
/// otherwise requires the <c>System.Text.Encoding.CodePages</c> NuGet.
/// </summary>
internal static class Cp1251
{
    // Maps byte 0x80..0xFF to its Unicode codepoint per the Windows-1251 standard.
    // 0x98 is officially undefined - mapped to U+FFFD (replacement character).
    private static readonly char[] HighMap =
    {
        'Ђ', 'Ѓ', '‚', 'ѓ', '„', '…', '†', '‡', // 80..87
        '€', '‰', 'Љ', '‹', 'Њ', 'Ќ', 'Ћ', 'Џ', // 88..8F
        'ђ', '‘', '’', '“', '”', '•', '–', '—', // 90..97
        '�', '™', 'љ', '›', 'њ', 'ќ', 'ћ', 'џ', // 98..9F
        ' ', 'Ў', 'ў', 'Ј', '¤', 'Ґ', '¦', '§', // A0..A7
        'Ё', '©', 'Є', '«', '¬', '­', '®', 'Ї', // A8..AF
        '°', '±', 'І', 'і', 'ґ', 'µ', '¶', '·', // B0..B7
        'ё', '№', 'є', '»', 'ј', 'Ѕ', 'ѕ', 'ї', // B8..BF
        'А', 'Б', 'В', 'Г', 'Д', 'Е', 'Ж', 'З', // C0..C7
        'И', 'Й', 'К', 'Л', 'М', 'Н', 'О', 'П', // C8..CF
        'Р', 'С', 'Т', 'У', 'Ф', 'Х', 'Ц', 'Ч', // D0..D7
        'Ш', 'Щ', 'Ъ', 'Ы', 'Ь', 'Э', 'Ю', 'Я', // D8..DF
        'а', 'б', 'в', 'г', 'д', 'е', 'ж', 'з', // E0..E7
        'и', 'й', 'к', 'л', 'м', 'н', 'о', 'п', // E8..EF
        'р', 'с', 'т', 'у', 'ф', 'х', 'ц', 'ч', // F0..F7
        'ш', 'щ', 'ъ', 'ы', 'ь', 'э', 'ю', 'я', // F8..FF
    };

    public static string Decode(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb.Append(b < 0x80 ? (char)b : HighMap[b - 0x80]);
        return sb.ToString();
    }
}
