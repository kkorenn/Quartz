using Quartz.Compat.Interface;
using Quartz.UI;

namespace Quartz.Core.Service;

public sealed class UIService : IRuntimeService, IRuntimeTick {
    public void Initialize() => UICore.Initialize();
    public void Dispose() => UICore.Dispose();
    public void Tick() => UICore.HandleUpdate();
}