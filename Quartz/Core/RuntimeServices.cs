using System.Diagnostics;
using Quartz.Compat;
using Quartz.Compat.Interface;

namespace Quartz.Core;

public sealed class RuntimeServices {
    private readonly List<IRuntimeService> services = [];

    public void Add(IRuntimeService service) => services.Add(service);

    // Logs how long each service takes so intermittent slow startups can be
    // attributed to a specific phase from the player's log.
    public void Initialize(QuartzLogger log = null) {
        Stopwatch sw = new();

        foreach(var service in services) {
            sw.Restart();
            service.Initialize();
            log?.Msg($"[Startup] {service.GetType().Name} took {sw.ElapsedMilliseconds} ms");
        }
    }

    public void Dispose() {
        for(int i = services.Count - 1; i >= 0; i--) {
            services[i].Dispose();
        }
    }
}
