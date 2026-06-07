namespace Koren.Compat.Interface;

public interface IKorenLogger {
    void KorenMsg(string msg);
    void KorenWrn(string msg);
    void KorenErr(string msg);
}