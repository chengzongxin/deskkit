using System.Text;
using Microsoft.International.Converters.PinYinConverter;

namespace DeskKit.Platform;

public static class Pinyin
{
    static Pinyin()
    {
        // 拼音库按 GB2312 读汉字。.NET 默认不带这个编码，不注册的话中文标题会变成空拼音。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string[] Variants(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var full = new StringBuilder();
        var initials = new StringBuilder();
        foreach (var character in text)
        {
            if (ChineseChar.IsValidChar(character))
            {
                var syllable = Syllable(character);
                if (syllable.Length == 0)
                {
                    continue;
                }

                full.Append(syllable);
                initials.Append(syllable[0]);
            }
            else if (char.IsLetterOrDigit(character))
            {
                var lower = char.ToLowerInvariant(character);
                full.Append(lower);
                initials.Append(lower);
            }
        }

        return new[] { full.ToString(), initials.ToString(), text.ToLowerInvariant() }
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    static string Syllable(char character)
    {
        try
        {
            var raw = new ChineseChar(character).Pinyins?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }

            var end = raw.Length;
            while (end > 0 && char.IsDigit(raw[end - 1]))
            {
                end--;
            }

            return raw[..end].ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
