namespace Koren.Features.Status;

// Current floor's margin (timing) scale — drives how forgiving Perfect/Early/Late
// windows are. Read straight from scrController.instance.currFloor.marginScale.
// Returns 1f when unavailable (default scale).
internal static class TimingScale {
    internal static float CurrentMarginScale {
        get {
            try {
                scrController c = scrController.instance;
                if(c != null && c.currFloor != null) {
                    return (float)c.currFloor.marginScale;
                }
            } catch {
            }

            return 1f;
        }
    }
}
