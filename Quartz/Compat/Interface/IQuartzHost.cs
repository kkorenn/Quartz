namespace Quartz.Compat.Interface;

public interface IQuartzHost {
    IQuartzLogger QuartzLogger { get; }

    string QuartzFilePath { get; }

    // Install locations, used by the updater to drop new builds in place.
    // ModsPath holds Quartz.dll (single-DLL layout); UserLibsPath is only
    // touched to clean up the old two-DLL install.
    string ModsPath { get; }
    string UserLibsPath { get; }
}
