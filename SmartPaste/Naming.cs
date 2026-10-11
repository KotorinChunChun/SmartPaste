using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SmartPaste;

internal sealed record NameToken(int Start, int Length, string Original, bool IsDate, bool IsYearMonth = false, bool IsTodayPlaceholder = false)
{
    public string Label => $"{(IsTodayPlaceholder ? "今日の日付" : IsYearMonth ? "年月" : IsDate ? "日付" : "数字")}：{Original}（{Start + 1} 文字目）";
    public override string ToString() => Label;

    public string InitialValue(DateTime? today = null) => IsTodayPlaceholder
        ? (today ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture) : Increment(Original);

    public string Increment(string value)
    {
        if (IsYearMonth)
        {
            if (!Naming.TryYearMonth(value, out var yearMonth)) throw new ArgumentException("正しい年月を入力してください。");
            return FormatCalendar(yearMonth.AddMonths(1), value, "yyyyMM");
        }
        if (IsDate)
        {
            if (!Naming.TryDate(value, out var date)) throw new ArgumentException("正しい年月日を入力してください。");
            return FormatCalendar(date.AddDays(1), value, "yyyyMMdd");
        }
        if (!Regex.IsMatch(value, "^[0-9０-９]+$")) throw new ArgumentException("数字を入力してください。");
        return Format((BigInteger.Parse(Naming.Ascii(value), CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture).PadLeft(value.Length, '0'), value);
    }

    public void Validate(string value)
    {
        if (IsYearMonth)
        {
            if (!Naming.TryYearMonth(value, out _)) throw new ArgumentException("正しい年月を入力してください。");
        }
        else if (IsDate)
        {
            if (!Naming.TryDate(value, out _)) throw new ArgumentException("正しい年月日を入力してください。");
        }
        else if (!Regex.IsMatch(value, "^[0-9０-９]+$")) throw new ArgumentException("数字を入力してください。");
    }

    private static string Format(string ascii, string reference) => reference.Any(c => c is >= '０' and <= '９')
        ? string.Concat(ascii.Select(c => c is >= '0' and <= '9' ? (char)(c + ('０' - '0')) : c)) : ascii;

    private static string FormatCalendar(DateTime date, string reference, string compactFormat)
    {
        if (Regex.Matches(reference, "[0-9０-９]+").Count == 1)
            return Format(date.ToString(compactFormat, CultureInfo.InvariantCulture), reference);
        int[] parts = [date.Year, date.Month, date.Day];
        int index = 0;
        return Regex.Replace(reference, "[0-9０-９]+", match =>
        {
            int part = index++;
            // Japanese M月/d日 have variable widths unless an explicit leading zero exists.
            int width = part > 0 && reference.Contains('年') && !Naming.Ascii(match.Value).StartsWith('0') ? 1 : match.Length;
            return Format(parts[part].ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'), match.Value);
        });
    }
}

internal static class Naming
{
    private static readonly Regex TodayPlaceholderPattern = new("yyyymmdd", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DatePattern = new(@"(?<![0-9０-９])(?:[0-9０-９]{4}(?<sep>[-_.])[0-9０-９]{1,2}\k<sep>[0-9０-９]{1,2}|[0-9０-９]{4}年[0-9０-９]{1,2}月[0-9０-９]{1,2}日|[0-9０-９]{8})(?![0-9０-９])", RegexOptions.Compiled);
    private static readonly Regex YearMonthPattern = new(@"(?<![0-9０-９])(?:[0-9０-９]{4}年[0-9０-９]{1,2}月|[0-9０-９]{4}[-_.][0-9０-９]{1,2}|[0-9０-９]{6})(?![0-9０-９])", RegexOptions.Compiled);
    public static string Ascii(string value) => string.Concat(value.Select(c => c is >= '０' and <= '９' ? (char)(c - ('０' - '0')) : c));

    public static bool TryYearMonth(string text, out DateTime yearMonth)
    {
        yearMonth = default;
        var ascii = Ascii(text);
        return YearMonthPattern.Match(text) is { Success: true } match && match.Value == text
            && DateTime.TryParseExact(ascii, ["yyyyMM", "yyyy年M月", "yyyy-M", "yyyy.M", "yyyy_M"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out yearMonth);
    }

    public static bool TryDate(string text, out DateTime date)
    {
        date = default;
        var ascii = Ascii(text);
        if (!DatePattern.IsMatch(text) || DatePattern.Match(text).Value != text) return false;
        var parts = Regex.Matches(ascii, "[0-9]+").Select(m => m.Value).ToArray();
        int year, month, day;
        if (parts.Length == 1 && parts[0].Length == 8)
        {
            year = int.Parse(ascii[..4]); month = int.Parse(ascii[4..6]); day = int.Parse(ascii[6..]);
        }
        else if (parts.Length == 3)
        {
            year = int.Parse(parts[0]); month = int.Parse(parts[1]); day = int.Parse(parts[2]);
        }
        else return false;
        try { date = new DateTime(year, month, day); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static IReadOnlyList<NameToken> Detect(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        // A numeric dot suffix can be the final part of a calendar name without a separate extension.
        int extensionStart = fileName.Length - Path.GetExtension(fileName).Length;
        if (DatePattern.Matches(fileName).Any(m => m.Index < extensionStart && m.Index + m.Length == fileName.Length)
            || YearMonthPattern.Matches(fileName).Any(m => m.Index < extensionStart && m.Index + m.Length == fileName.Length)) stem = fileName;
        var dateMatches = DatePattern.Matches(stem).ToArray();
        var dates = dateMatches.Where(m => TryDate(m.Value, out _))
            .Select(m => new NameToken(m.Index, m.Length, m.Value, true)).ToList();
        var yearMonths = YearMonthPattern.Matches(stem).Where(m => TryYearMonth(m.Value, out _)
                && !dateMatches.Any(d => m.Index < d.Index + d.Length && m.Index + m.Length > d.Index))
            .Select(m => new NameToken(m.Index, m.Length, m.Value, false, true));
        var placeholders = TodayPlaceholderPattern.Matches(stem)
            .Select(m => new NameToken(m.Index, m.Length, m.Value, true, IsTodayPlaceholder: true));
        var calendarTokens = dates.Concat(yearMonths).Concat(placeholders).ToArray();
        var numbers = Regex.Matches(stem, "[0-9０-９]+").Where(m => !calendarTokens.Any(d => m.Index < d.Start + d.Length && m.Index + m.Length > d.Start))
            .Select(m => new NameToken(m.Index, m.Length, m.Value, false));
        return calendarTokens.Concat(numbers).OrderBy(t => t.Start).ToArray();
    }

    public static string Replace(string name, NameToken token, string value)
    {
        token.Validate(value);
        var result = name[..token.Start] + value + name[(token.Start + token.Length)..];
        ValidateFileName(result);
        return result;
    }

    public static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("ファイル名に使えない文字、末尾の空白・ピリオド、または長すぎる名前です。");
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase))
            throw new ArgumentException("Windows の予約名は使えません。");
    }
}

internal sealed class CopyChoice
{
    public string Source { get; }
    public string OriginalName => Path.GetFileName(Source);
    public IReadOnlyList<NameToken> Tokens { get; }
    public int TokenIndex { get; set; }
    public string Value { get; set; }
    public bool Edited { get; set; }
    public NameToken? Token => Tokens.Count == 0 ? null : Tokens[TokenIndex];

    public CopyChoice(string source, DateTime? today = null)
    {
        Source = source;
        Tokens = Naming.Detect(OriginalName);
        Value = Tokens.Count == 0 ? "" : Tokens[0].InitialValue(today);
    }

    public string ProposedName => Token is { } t ? Naming.Replace(OriginalName, t, Value) : OriginalName;
    public CopyChoice Clone() => new(Source) { TokenIndex = TokenIndex, Value = Value, Edited = Edited };
}

internal static class Planner
{
    public static string Available(CopyChoice choice, string directory, ISet<string>? reserved = null)
    {
        string value = choice.Value;
        for (int attempt = 0; attempt < 100000; attempt++)
        {
            var name = choice.Token is { } t ? Naming.Replace(choice.OriginalName, t, value) : choice.OriginalName;
            if ((choice.Token == null || choice.Token.IsTodayPlaceholder) && attempt > 0)
                name = $"{Path.GetFileNameWithoutExtension(name)} (コピー{(attempt == 1 ? "" : $" {attempt}")}){Path.GetExtension(name)}";
            Naming.ValidateFileName(name);
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) && !Directory.Exists(path) && !(reserved?.Contains(name) ?? false)) return name;
            if (choice.Edited) throw new IOException($"「{name}」は既に存在します。");
            if (choice.Token is { IsTodayPlaceholder: false }) value = choice.Token.Increment(value);
        }
        throw new IOException("空いている名前が見つかりません。");
    }
}
