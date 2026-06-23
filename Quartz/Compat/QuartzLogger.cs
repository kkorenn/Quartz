using Quartz.Compat.Interface;

namespace Quartz.Compat;

public sealed class QuartzLogger(IQuartzLogger logger, string prefix = null) {

    private string F(string msg) => string.IsNullOrEmpty(prefix) ? msg : $"[{prefix}] {msg}";

    public void Msg(string msg) => logger.QuartzMsg(F(msg));

    public void Wrn(string msg) => logger.QuartzWrn(F(msg));

    public void Err(string msg) => logger.QuartzErr(F(msg));
}