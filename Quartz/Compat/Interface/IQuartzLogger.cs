namespace Quartz.Compat.Interface;

public interface IQuartzLogger {
    void QuartzMsg(string msg);
    void QuartzWrn(string msg);
    void QuartzErr(string msg);
}