using Koren.Compat.Interface;

namespace Koren.Compat;

public sealed class KorenLogger(IKorenLogger logger, string prefix = null) {

    private string F(string msg) => string.IsNullOrEmpty(prefix) ? msg : $"[{prefix}] {msg}";

    public void Msg(string msg) => logger.KorenMsg(F(msg));

    public void Wrn(string msg) => logger.KorenWrn(F(msg));

    public void Err(string msg) => logger.KorenErr(F(msg));
}