namespace Quartz.Utility;

public static class StringUtils {
    public static List<string> Search(string query, IEnumerable<string> source) {
        if(string.IsNullOrWhiteSpace(query)) {
            return [.. source];
        }

        string q = Normalize(query);

        if(string.IsNullOrEmpty(q)) {
            return [];
        }

        return [..
            source
                .Select(original => new {
                    Original = original,
                    Normalized = Normalize(original)
                })
                .Select(x => new {
                    x.Original,
                    Score = ScoreMatch(x.Normalized, q)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Original)
        ];
    }

    private static int ScoreMatch(string normalizedValue, string normalizedQuery) {
        if(normalizedValue == normalizedQuery) {
            return 100;
        }

        if(normalizedValue.StartsWith(normalizedQuery)) {
            return 80;
        }

        return normalizedValue.Contains(normalizedQuery) ? 50 : 0;
    }

    public static string Normalize(string input) {
        if(string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        char[] chars = [.. input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)];
        return new string(chars);
    }

    private static readonly string[] ChosungTable = [
        "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ",
        "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"
    ];

    // Collapses Hangul syllables to their leading consonant (초성) so a query
    // like "ㅈㄷ" matches "진동". Non-Hangul characters pass through untouched.
    public static string NormalizeToHangulChosung(string input) {
        if(string.IsNullOrEmpty(input)) {
            return input;
        }

        var result = new System.Text.StringBuilder();

        foreach(char c in input) {
            if(c >= 0xAC00 && c <= 0xD7A3) {
                int index = (c - 0xAC00) / 588;
                result.Append(ChosungTable[index]);
            } else {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
