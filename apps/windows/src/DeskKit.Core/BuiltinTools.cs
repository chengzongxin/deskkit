using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskKit.Core;

public static class ExpressionEvaluator
{
    public static DkResult Evaluate(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return DkResult.Fail("INVALID_ARG", "算式为空");
        }

        var parser = new Parser(trimmed.ToCharArray());
        try
        {
            var value = parser.ParseExpression();
            parser.Skip();
            if (!parser.End)
            {
                return DkResult.Fail("INVALID_ARG", "算式无法解析");
            }

            return DkResult.Success(Format(value));
        }
        catch (DivideByZeroException)
        {
            return DkResult.Fail("INVALID_ARG", "除数不能为 0");
        }
        catch (FormatException)
        {
            return DkResult.Fail("INVALID_ARG", "算式无法解析");
        }
    }

    public static string Format(decimal value)
    {
        if (value == decimal.Truncate(value) && value >= int.MinValue && value <= int.MaxValue)
        {
            return decimal.ToInt32(value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    sealed class Parser
    {
        readonly char[] chars;
        int index;

        public Parser(char[] chars) => this.chars = chars;

        public bool End => index >= chars.Length;

        public void Skip()
        {
            while (!End && char.IsWhiteSpace(chars[index]))
            {
                index++;
            }
        }

        public decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                Skip();
                if (End || (chars[index] != '+' && chars[index] != '-'))
                {
                    return value;
                }

                var op = chars[index++];
                var right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }
        }

        decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                Skip();
                if (End || (chars[index] != '*' && chars[index] != '/'))
                {
                    return value;
                }

                var op = chars[index++];
                var right = ParseFactor();
                if (op == '/' && right == 0)
                {
                    throw new DivideByZeroException();
                }

                value = op == '*' ? value * right : value / right;
            }
        }

        decimal ParseFactor()
        {
            Skip();
            if (End)
            {
                throw new FormatException();
            }

            if (chars[index] == '(')
            {
                index++;
                var value = ParseExpression();
                Skip();
                if (End || chars[index] != ')')
                {
                    throw new FormatException();
                }

                index++;
                return value;
            }

            return ParseNumber();
        }

        decimal ParseNumber()
        {
            Skip();
            var start = index;
            if (!End && (chars[index] == '+' || chars[index] == '-'))
            {
                index++;
            }

            var digits = false;
            while (!End && IsDigit(chars[index]))
            {
                digits = true;
                index++;
            }

            if (!End && chars[index] == '.')
            {
                index++;
                while (!End && IsDigit(chars[index]))
                {
                    digits = true;
                    index++;
                }
            }

            var text = new string(chars, start, index - start);
            if (!digits || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException();
            }

            return value;
        }

        static bool IsDigit(char c) => c is >= '0' and <= '9';
    }
}

public static class UnitConverter
{
    static readonly Dictionary<string, (string Group, decimal Base)> Table = new(StringComparer.Ordinal)
    {
        ["m"] = ("len", 1m),
        ["km"] = ("len", 1000m),
        ["mi"] = ("len", 1609.344m),
        ["cm"] = ("len", 0.01m),
        ["kg"] = ("wt", 1m),
        ["g"] = ("wt", 0.001m),
        ["lb"] = ("wt", 0.45359237m),
        ["b"] = ("data", 1m),
        ["kb"] = ("data", 1024m),
        ["mb"] = ("data", 1048576m),
        ["cny"] = ("money", 1m),
        ["usd"] = ("money", 7.12m)
    };

    static readonly Regex Pattern = new(@"^\s*([0-9]*\.?[0-9]+)\s*([A-Za-z]+)\s+to\s+([A-Za-z]+)\s*$", RegexOptions.CultureInvariant);

    public static string? Convert(string input)
    {
        var match = Pattern.Match(input);
        if (!match.Success)
        {
            return null;
        }

        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var from = match.Groups[2].Value.ToLowerInvariant();
        var to = match.Groups[3].Value.ToLowerInvariant();
        if (from is "c" or "f" or "k" && to is "c" or "f" or "k")
        {
            return FormatTemp(amount, from, to);
        }

        if (!Table.TryGetValue(from, out var left) || !Table.TryGetValue(to, out var right) || left.Group != right.Group || right.Base == 0)
        {
            return null;
        }

        var value = amount * left.Base / right.Base;
        return ExpressionEvaluator.Evaluate(ExpressionEvaluator.Format(value)).Data + " " + to;
    }

    static string FormatTemp(decimal amount, string from, string to)
    {
        var celsius = from switch
        {
            "f" => (amount - 32m) * 5m / 9m,
            "k" => amount - 273.15m,
            _ => amount
        };
        var output = to switch
        {
            "f" => celsius * 9m / 5m + 32m,
            "k" => celsius + 273.15m,
            _ => celsius
        };
        return ExpressionEvaluator.Format(output) + " " + to;
    }
}

public static class ColorConvert
{
    public static string? Describe(string input)
    {
        var rgb = Parse(input);
        if (rgb == null)
        {
            return null;
        }

        var (r, g, b) = rgb.Value;
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        var hsl = ToHsl(r, g, b);
        return $"{hex}  rgb({r},{g},{b})  hsl({hsl.H}, {hsl.S}%, {hsl.L}%)";
    }

    public static (int R, int G, int B)? Parse(string input)
    {
        var text = input.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 3)
        {
            text = string.Concat(text.Select(ch => $"{ch}{ch}"));
        }

        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return ((value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    static (int H, int S, int L) ToHsl(int r, int g, int b)
    {
        var rf = r / 255d;
        var gf = g / 255d;
        var bf = b / 255d;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var l = (max + min) / 2;
        var h = 0d;
        var s = 0d;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rf)
            {
                h = (gf - bf) / d + (gf < bf ? 6 : 0);
            }
            else if (max == gf)
            {
                h = (bf - rf) / d + 2;
            }
            else
            {
                h = (rf - gf) / d + 4;
            }

            h /= 6;
        }

        return ((int)Math.Round(h * 360), (int)Math.Round(s * 100), (int)Math.Round(l * 100));
    }
}

public static class TimestampConvert
{
    public static string? Describe(string input)
    {
        var text = input.Trim();
        if (text.Length is 10 or 13 && text.All(char.IsDigit) && double.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            var seconds = text.Length == 13 ? raw / 1000 : raw;
            return DateTimeOffset.FromUnixTimeSeconds((long)Math.Round(seconds)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local)).ToUnixTimeSeconds();
            return utc.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }
}

public static class Translator
{
    static readonly Dictionary<string, string> Dictionary = new(StringComparer.Ordinal)
    {
        ["hello world"] = "你好，世界",
        ["hello"] = "你好",
        ["world"] = "世界",
        ["thank you"] = "谢谢",
        ["thanks"] = "谢谢",
        ["good morning"] = "早上好",
        ["deskkit"] = "DeskKit",
        ["search"] = "搜索",
        ["file"] = "文件",
        ["copy"] = "复制",
        ["color"] = "颜色",
        ["time"] = "时间"
    };

    public static (string Text, bool Offline) Translate(string input)
    {
        var key = input.Trim().ToLowerInvariant();
        if (Dictionary.TryGetValue(key, out var hit))
        {
            return (hit, true);
        }

        return ($"未收录：{input}", true);
    }
}
