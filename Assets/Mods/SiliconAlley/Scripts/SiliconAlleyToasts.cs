using System.Collections.Generic;
using System.Globalization;
using UI.Notification;

// Issue #152 (epic #142): every Silicon Alley toast goes through here.
//
// Two problems this solves. First, URGENCY: the toasts were mostly Info regardless of what they meant, so
// a decision window that expires looked exactly like a patch note. The event CLASS now decides the
// notification type, using the same taxonomy the hub's attention model (#148) already grades studios with,
// so the two can't drift apart.
//
// Second, BURSTS: a time-machine catch-up runs many hours synchronously, and the game's duplicate filter is
// not time-based — it drops a repeat only while a toast of that exact name is still alive, and it does so
// BEFORE the save's notification enqueue, so the suppressed event is lost from the history too. Four
// studios finishing a build in one tick therefore meant four stacked toasts and no summary. Events are now
// buffered per class and flushed once: one event keeps its precise text and its studio deep-link, several
// collapse into a digest that deep-links to the hub — where #148's triage strip already answers "which
// studios?". This is the base game's own pattern (Contact.ShowAddedContactNotifications).
public static class SiliconAlleyToasts
{
    // The event classes, in the order a flush emits them: the ones that cost you something first.
    public enum Klass
    {
        Decision,  // a window that expires if ignored (design prompt, milestone)
        Deadline,  // a contract/publisher deadline closing in
        Failed,    // something already lapsed
        Parked,    // work finished and waiting on you (ready to release, build done)
        Idle,      // a staffed studio doing nothing
        Money,     // cash arrived (ship, patch, deal, contract)
        Progress,  // pure FYI (phase transitions)
    }

    private sealed class Pending
    {
        public string LocaleKey;
        public Dictionary<string, string> Data;
        public float Seconds;
        public string DedupeId;
        public string StudioKey;
    }

    private static readonly Dictionary<Klass, List<Pending>> Buffer = new Dictionary<Klass, List<Pending>>();

    // Queue one event. Nothing shows until Flush runs, which happens on the frame the hour's simulation
    // finishes — so every studio that fired this class in the same tick is counted together.
    public static void Queue(Klass klass, string localeKey, Dictionary<string, string> data,
        float seconds, string dedupeId, string studioKey)
    {
        if (!Buffer.TryGetValue(klass, out var list))
        {
            list = new List<Pending>();
            Buffer[klass] = list;
        }
        list.Add(new Pending
        {
            LocaleKey = localeKey,
            Data = data,
            Seconds = seconds,
            DedupeId = dedupeId,
            StudioKey = studioKey,
        });
    }

    // Emit whatever has accumulated. One event per class ⇒ the precise toast, unchanged, deep-linking to
    // that studio. More than one ⇒ a single digest naming the count, deep-linking to the hub.
    public static void Flush()
    {
        if (Buffer.Count == 0)
            return;
        foreach (var klass in ClassOrder)
        {
            if (!Buffer.TryGetValue(klass, out var list) || list.Count == 0)
                continue;
            if (list.Count == 1)
            {
                var one = list[0];
                var key = one.StudioKey;
                Notifications.Show(TypeOf(klass), one.LocaleKey, one.Data, one.Seconds, one.DedupeId,
                    () => SiliconAlleyProjectScreen.Open(key));
            }
            else
            {
                var data = new Dictionary<string, string>
                {
                    ["count"] = list.Count.ToString(CultureInfo.InvariantCulture),
                };
                Notifications.Show(TypeOf(klass), DigestKeyOf(klass), data, 6f, "siliconalley:digest:" + klass,
                    SiliconAlleyProjectScreen.OpenHub);
            }
            list.Clear();
        }
    }

    // Drop everything without showing it — the mod is unloading or the city is going away.
    public static void Clear() => Buffer.Clear();

    private static readonly Klass[] ClassOrder =
    {
        Klass.Decision, Klass.Deadline, Klass.Failed, Klass.Parked, Klass.Idle, Klass.Money, Klass.Progress,
    };

    // The class IS the urgency tier. Note the deliberate divergence #148 documented and this preserves: the
    // hub badge warns at 3 days out, the deal TOAST at 2 — a toast interrupts, a badge is ambient.
    private static NotificationType TypeOf(Klass klass)
    {
        switch (klass)
        {
            case Klass.Money: return NotificationType.Success;
            case Klass.Progress: return NotificationType.Info;
            default: return NotificationType.Warning; // decision / deadline / failed / parked / idle
        }
    }

    private static string DigestKeyOf(Klass klass)
    {
        switch (klass)
        {
            case Klass.Decision: return "siliconalley:digest_decision";
            case Klass.Deadline: return "siliconalley:digest_deadline";
            case Klass.Failed: return "siliconalley:digest_failed";
            case Klass.Parked: return "siliconalley:digest_parked";
            case Klass.Idle: return "siliconalley:digest_idle";
            case Klass.Money: return "siliconalley:digest_money";
            default: return "siliconalley:digest_progress";
        }
    }
}
