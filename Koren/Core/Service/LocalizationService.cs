using Koren.Compat;
using Koren.Compat.Interface;
using Koren.IO;
using Koren.Localization;

namespace Koren.Core.Service;

public sealed class LocalizationService(
    string langPath,
    SettingsFile<CoreSettings> configFile,
    KorenLogger logger
) : IRuntimeService {
    public Translator Translator { get; } = new();

    public void Initialize() {
        Translator.Language =
            configFile.Data.Language;

        Translator.SetLog(
            logger.Msg
        );

        _ = Translator.Load(langPath);
    }

    public void Dispose() { }
}