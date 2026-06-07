namespace Koren.Compat.Interface;

public interface IKorenHost {
    IKorenLogger KorenLogger { get; }

    string KorenFilePath { get; }
}