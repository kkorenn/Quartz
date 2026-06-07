using Koren.Compat.Interface;
using Koren.UI;

namespace Koren.Core.Service;

public sealed class UIService : IRuntimeService, IRuntimeTick {
    public void Initialize() => UICore.Initialize();
    public void Dispose() => UICore.Dispose();
    public void Tick() => UICore.HandleUpdate();
}