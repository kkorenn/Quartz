using System.Reflection;

namespace Quartz.Features.Interop;

// Detection + data bridge to the XPerfect UMM mod, ported from the original
// KorenResourcePack (src/Compatibility/XPerfectBridge.cs).
//
// XPerfect grades each Perfect hit as X (dead-center), + (slightly late) or -
// (slightly early) and exposes the running judgement + counts on a static
// XPerfect.AccuracyState. It loads into the same Mono domain as Quartz (UMM and
// MelonLoader share the domain), so we find its assembly by name and read those
// statics by reflection — no hard reference, so Quartz is unaffected when
// XPerfect isn't installed. [[umm-interop]] is the general UMM detector; this is
// the XPerfect-specific surface the combo / judgement features consume.
internal static class XPerfectBridge {
    public enum Judge {
        None = 0,
        X = 1,
        Plus = 2,
        Minus = 3,
    }

    private static bool resolved;
    private static bool installed;

    private static MemberInfo lastJudgeMember;
    private static MemberInfo lastJudgeForTextMember;
    private static MemberInfo xCountMember;
    private static MemberInfo plusCountMember;
    private static MemberInfo minusCountMember;

    private static PropertyInfo enabledProp;

    // True once XPerfect is loaded and its AccuracyState resolved.
    public static bool Installed {
        get {
            EnsureResolved();
            return installed;
        }
    }

    // True only when XPerfect is installed AND its own feature toggle is on, so
    // Quartz mirrors XPerfect's enabled state rather than overriding it. If
    // XPerfect exposes no toggle, "installed" implies active.
    public static bool Active {
        get {
            if(!Installed) {
                return false;
            }
            try {
                if(enabledProp == null) {
                    return true;
                }
                return enabledProp.GetValue(null, null) is bool b && b;
            } catch {
                return false;
            }
        }
    }

    // The judgement of the most recent Perfect (drives combo gating + restriction).
    public static Judge LastJudge() => ReadJudge(lastJudgeMember, Judge.None);

    // The judgement to DISPLAY for the most recent Perfect (XPerfect distinguishes
    // the value it counts from the value it shows); falls back to LastJudge.
    public static Judge LastJudgeForText() =>
        lastJudgeForTextMember == null ? LastJudge() : ReadJudge(lastJudgeForTextMember, LastJudge());

    public static int XCount() => ReadIntMember(xCountMember);
    public static int PlusCount() => ReadIntMember(plusCountMember);
    public static int MinusCount() => ReadIntMember(minusCountMember);

    private static Judge ReadJudge(MemberInfo member, Judge fallback) {
        if(!Installed || member == null) {
            return fallback;
        }
        try {
            object v = ReadStaticMember(member);
            if(v == null) {
                return Judge.None;
            }
            int i = System.Convert.ToInt32(v);
            return i is < 0 or > 3 ? Judge.None : (Judge)i;
        } catch {
            return fallback;
        }
    }

    private static int ReadIntMember(MemberInfo member) {
        if(!Installed || member == null) {
            return 0;
        }
        try {
            object v = ReadStaticMember(member);
            return v == null ? 0 : System.Convert.ToInt32(v);
        } catch {
            return 0;
        }
    }

    private static object ReadStaticMember(MemberInfo member) {
        if(member is PropertyInfo property) {
            return property.GetValue(null, null);
        }
        return member is FieldInfo field ? field.GetValue(null) : null;
    }

    // Resolves a static readable member, tolerating field, auto-property, or the
    // compiler-generated backing field (XPerfect's layout isn't guaranteed).
    private static MemberInfo GetStaticReadable(Type type, string name) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        PropertyInfo property = type.GetProperty(name, flags);
        if(property != null && property.GetGetMethod(true) != null) {
            return property;
        }

        FieldInfo field = type.GetField(name, flags);
        return field ?? type.GetField("<" + name + ">k__BackingField", flags);
    }

    private static void EnsureResolved() {
        if(resolved) {
            return;
        }
        resolved = true;
        try {
            Assembly xpAsm = null;
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
                if(a.GetName().Name == "XPerfect") {
                    xpAsm = a;
                    break;
                }
            }
            if(xpAsm == null) {
                return;
            }

            Type accuracyStateType = xpAsm.GetType("XPerfect.AccuracyState");
            if(accuracyStateType == null) {
                return;
            }

            lastJudgeMember = GetStaticReadable(accuracyStateType, "LastJudge");
            lastJudgeForTextMember = GetStaticReadable(accuracyStateType, "LastJudgeForText");
            xCountMember = GetStaticReadable(accuracyStateType, "XPerfectCount");
            plusCountMember = GetStaticReadable(accuracyStateType, "PlusPerfectCount");
            minusCountMember = GetStaticReadable(accuracyStateType, "MinusPerfectCount");

            Type mainType = xpAsm.GetType("XPerfect.Main");
            if(mainType != null) {
                enabledProp = mainType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static);
            }

            installed = lastJudgeMember != null;
        } catch {
            installed = false;
        }
    }
}
