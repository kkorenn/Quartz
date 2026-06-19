using System.Text.Json;
using Koren.Core;
using Koren.IO;

List<(string Name, Action Run)> tests = [
    ("SemVer parses and orders channels", TestSemVer),
    ("AtomicFile replaces without temp debris", TestAtomicFile),
    ("Localization keys stay in parity", TestLocalizationParity),
];

int failed = 0;
foreach((string name, Action run) in tests) {
    try {
        run();
        Console.WriteLine("PASS " + name);
    } catch(Exception e) {
        failed++;
        Console.Error.WriteLine("FAIL " + name + ": " + e.Message);
    }
}

return failed == 0 ? 0 : 1;

static void TestSemVer() {
    Assert(SemVer.TryParse("v2.0.0-alpha.17", out SemVer alpha), "alpha parse");
    Assert(SemVer.TryParse("2.0.0-beta.1", out SemVer beta), "beta parse");
    Assert(SemVer.TryParse("2.0.0", out SemVer stable), "stable parse");
    Assert(beta.CompareTo(alpha) > 0, "beta must outrank alpha");
    Assert(stable.CompareTo(beta) > 0, "stable must outrank prerelease");
    Assert(SemVer.Compare("2.0.0-alpha.10", "2.0.0-alpha.2") > 0, "numeric build ordering");
    Assert(!SemVer.TryParse("2.0", out _), "short version rejection");
}

static void TestAtomicFile() {
    string root = Path.Combine(Path.GetTempPath(), "koren-tests-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(root, "settings.json");
    try {
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");
        Assert(File.ReadAllText(path) == "two", "replacement content");
        Assert(Directory.GetFiles(root, "*.tmp").Length == 0, "temporary files cleaned");
    } finally {
        if(Directory.Exists(root)) {
            Directory.Delete(root, true);
        }
    }
}

static void TestLocalizationParity() {
    string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    string lang = Path.Combine(repo, "Koren", "Resource", "Export", "Lang");
    HashSet<string> english = ReadLanguageKeys(Path.Combine(lang, "en-US.json"), "en-US");
    HashSet<string> korean = ReadLanguageKeys(Path.Combine(lang, "ko-KR.json"), "ko-KR");

    string[] missingKorean = english.Except(korean).OrderBy(x => x).ToArray();
    string[] missingEnglish = korean.Except(english).OrderBy(x => x).ToArray();
    Assert(missingKorean.Length == 0, "missing ko-KR: " + string.Join(", ", missingKorean));
    Assert(missingEnglish.Length == 0, "missing en-US: " + string.Join(", ", missingEnglish));
}

static HashSet<string> ReadLanguageKeys(string path, string language) {
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement block = doc.RootElement.GetProperty(language);
    return block.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
}

static void Assert(bool condition, string message) {
    if(!condition) {
        throw new InvalidOperationException(message);
    }
}
