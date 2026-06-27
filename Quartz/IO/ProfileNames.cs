#nullable disable

namespace Quartz.IO;

public static class ProfileNames {
    public static string Sanitize(string name) {
        if(string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        const string portableInvalid = "<>:\"/\\|?*";
        string clean = new([.. name.Trim().Where(c => !invalid.Contains(c) && !portableInvalid.Contains(c))]);
        clean = clean.Trim().Trim('.');

        if(clean.Length > 32) {
            clean = clean[..32].Trim();
        }

        return clean.Length == 0 ? null : clean;
    }

    public static string Unique(string name, Func<string, bool> exists) {
        name = Sanitize(name) ?? "Imported";
        if(exists == null || !exists(name)) {
            return name;
        }

        for(int i = 2; ; i++) {
            string suffix = $" ({i})";
            string stem = name;
            if(stem.Length + suffix.Length > 32) {
                stem = stem[..(32 - suffix.Length)].Trim();
            }
            string candidate = stem + suffix;
            if(!exists(candidate)) {
                return candidate;
            }
        }
    }

    public static string ImportedModName(string label) {
        string clean = Sanitize(label) ?? "Imported";
        return Sanitize("Imported - " + clean) ?? "Imported";
    }
}
