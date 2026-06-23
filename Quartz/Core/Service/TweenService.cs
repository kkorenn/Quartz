using GTweens.Contexts;
using Quartz.Compat.Interface;
namespace Quartz.Core.Service;

public sealed class TweenService(GTweensContext context) : IRuntimeService, IRuntimeTick {
    private readonly GTweensContext _context = context;

    public void Initialize() { }

    public void Dispose() => _context.Clear();

    public void Tick() => _context.Tick(UnityEngine.Time.unscaledDeltaTime);
}