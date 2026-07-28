using Localizor;
using UnityEngine;

// Issue #123 (epic #121): mid-project milestone decisions — the four MilestoneSlot windows that fill the
// long Development/Testing stretch with real choices. Each slot is anchored to a progress window inside its
// stage; when the project crosses the window's start, one of two event variants (picked deterministically per
// save, see EventFor) surfaces a two-option decision card (#128) plus a clickable toast. Ignoring the card is
// always safe: once progress passes the window's end, the simulator resolves the slot silently with ZERO
// effect — that neutral auto-resolve is what keeps old saves byte-identical unless the player clicks.
//
// SAVE-COMPAT: only the resolved bits persist (SiliconAlleyState.MilestoneMask, field 47, #122). Whether a
// slot is pending is DERIVED here (stage + progress window + bit unset); variants, thresholds and effects are
// tunable catalog data. The slot ordinals are the APPEND-ONLY MilestoneSlot family (see CLAUDE.md): once
// shipped, a slot keeps its bit forever — add new milestones only by appending slots.
//
// DETERMINISM: the variant pick hashes key + product version + slot with FNV-1a — no UnityEngine.Random, no
// wall clock, no string.GetHashCode (randomized per process on some runtimes) — so reloading a save re-derives
// the SAME pending event, and save-scumming cannot reroll a decision.
public static class SiliconAlleyMilestones
{
    public const int SlotCount = 4;

    // Where each slot lives, as fractions of EffectiveProjectSize (frozen once the concept locks, so the
    // window cannot move mid-flight). Windows sit strictly inside their stage band and close BEFORE the
    // stage's park ceiling (Development parks at 0.70·size − 1, Testing at size), so every slot either fires
    // or auto-resolves before the stage can end.
    private static readonly float[] FireFractions = { 0.30f, 0.55f, 0.80f, 0.92f };
    private static readonly float[] CloseFractions = { 0.45f, 0.68f, 0.90f, 0.99f };

    // One decision option: label/summary locale keys + the effect bundle it applies. All effects reuse
    // existing levers only (progress, bugs, awareness/hype, a quality sample, a cash spend) — nothing new is
    // persisted. Numbers are tunable; a default(MilestoneOption) with both keys null is "no action".
    public readonly struct MilestoneOption
    {
        public readonly string LabelKey, SummaryKey;
        public readonly float Cost;             // cash charged via SiliconAlleyMoney.TrySpend (0 = free)
        public readonly float ProgressFraction; // ± fraction of EffectiveProjectSize, clamped to the stage band
        public readonly float BugsScaleDelta;   // ± flat bugs, in fractions of BugScale (e.g. -0.15 = -4.5 bugs)
        public readonly float BugsPercentDelta; // ± fraction of the CURRENT BugCount (e.g. 0.25 = +25%)
        public readonly float Awareness, Hype;  // flat marketing boosts (never below 0 — the setters clamp)
        public readonly float QualitySample;    // <0 = none; else one extra staff-quality sample …
        public readonly float QualityWeight;    // … accrued at this weight in the slot's phase

        public MilestoneOption(string labelKey, string summaryKey, float cost = 0f, float progressFraction = 0f,
            float bugsScaleDelta = 0f, float bugsPercentDelta = 0f, float awareness = 0f, float hype = 0f,
            float qualitySample = -1f, float qualityWeight = 0f)
        {
            LabelKey = labelKey;
            SummaryKey = summaryKey;
            Cost = cost;
            ProgressFraction = progressFraction;
            BugsScaleDelta = bugsScaleDelta;
            BugsPercentDelta = bugsPercentDelta;
            Awareness = awareness;
            Hype = hype;
            QualitySample = qualitySample;
            QualityWeight = qualityWeight;
        }
    }

    // One event variant: identity (for icons/logging), title/description locale keys and its two options.
    public readonly struct MilestoneEvent
    {
        public readonly string Id;             // e.g. "scope_creep" — locale keys are siliconalley:ms_<Id>_*
        public readonly string TitleKey, DescKey;
        public readonly MilestoneOption OptionA, OptionB;

        public MilestoneEvent(string id, MilestoneOption optionA, MilestoneOption optionB)
        {
            Id = id;
            TitleKey = "siliconalley:ms_" + id + "_title";
            DescKey = "siliconalley:ms_" + id + "_desc";
            OptionA = optionA;
            OptionB = optionB;
        }
    }

    // The catalog: [slot][variant]. Effects are intentionally modest (a few % of size / of the bug count) so
    // a milestone nudges a build rather than deciding it; every pair trades between at least two of progress,
    // quality, bugs, marketing and cash.
    private static readonly MilestoneEvent[][] Events =
    {
        new[] // slot 0 — Development, 30%..45%: the build meets reality
        {
            new MilestoneEvent("scope_creep",
                new MilestoneOption("siliconalley:ms_scope_creep_opt0", "siliconalley:ms_scope_creep_opt0_sum",
                    progressFraction: 0.04f, qualitySample: 0.2f, qualityWeight: 2f),
                new MilestoneOption("siliconalley:ms_scope_creep_opt1", "siliconalley:ms_scope_creep_opt1_sum",
                    progressFraction: -0.03f, qualitySample: 1f, qualityWeight: 2f)),
            new MilestoneEvent("middleware",
                new MilestoneOption("siliconalley:ms_middleware_opt0", "siliconalley:ms_middleware_opt0_sum",
                    cost: 2000f, progressFraction: 0.02f, bugsScaleDelta: -0.15f),
                new MilestoneOption("siliconalley:ms_middleware_opt1", "siliconalley:ms_middleware_opt1_sum")),
        },
        new[] // slot 1 — Development, 55%..68%: the midpoint reckoning
        {
            new MilestoneEvent("tech_debt",
                new MilestoneOption("siliconalley:ms_tech_debt_opt0", "siliconalley:ms_tech_debt_opt0_sum",
                    progressFraction: -0.05f, bugsPercentDelta: -0.30f),
                new MilestoneOption("siliconalley:ms_tech_debt_opt1", "siliconalley:ms_tech_debt_opt1_sum",
                    progressFraction: 0.03f, bugsPercentDelta: 0.25f)),
            new MilestoneEvent("conference",
                new MilestoneOption("siliconalley:ms_conference_opt0", "siliconalley:ms_conference_opt0_sum",
                    cost: 2500f, awareness: 12f),
                new MilestoneOption("siliconalley:ms_conference_opt1", "siliconalley:ms_conference_opt1_sum")),
        },
        new[] // slot 2 — Testing, 80%..90%: how to run QA
        {
            new MilestoneEvent("beta_call",
                new MilestoneOption("siliconalley:ms_beta_call_opt0", "siliconalley:ms_beta_call_opt0_sum",
                    awareness: 8f, bugsPercentDelta: 0.15f),
                new MilestoneOption("siliconalley:ms_beta_call_opt1", "siliconalley:ms_beta_call_opt1_sum",
                    bugsPercentDelta: -0.20f)),
            new MilestoneEvent("crunch_qa",
                new MilestoneOption("siliconalley:ms_crunch_qa_opt0", "siliconalley:ms_crunch_qa_opt0_sum",
                    bugsPercentDelta: -0.25f, qualitySample: 0.3f, qualityWeight: 2f),
                new MilestoneOption("siliconalley:ms_crunch_qa_opt1", "siliconalley:ms_crunch_qa_opt1_sum")),
        },
        new[] // slot 3 — Testing, 92%..99%: the endgame call
        {
            new MilestoneEvent("go_gold",
                new MilestoneOption("siliconalley:ms_go_gold_opt0", "siliconalley:ms_go_gold_opt0_sum",
                    hype: 8f),
                new MilestoneOption("siliconalley:ms_go_gold_opt1", "siliconalley:ms_go_gold_opt1_sum",
                    bugsPercentDelta: -0.15f)),
            new MilestoneEvent("dayone_patch",
                new MilestoneOption("siliconalley:ms_dayone_patch_opt0", "siliconalley:ms_dayone_patch_opt0_sum",
                    awareness: 5f, bugsPercentDelta: 0.10f),
                new MilestoneOption("siliconalley:ms_dayone_patch_opt1", "siliconalley:ms_dayone_patch_opt1_sum")),
        },
    };

    public static SiliconAlleyState.ProjectStage SlotStage(int slot)
        => slot <= 1 ? SiliconAlleyState.ProjectStage.Development : SiliconAlleyState.ProjectStage.Testing;

    public static float FireProgress(int slot, float size) => FireFractions[slot] * size;
    public static float CloseProgress(int slot, float size) => CloseFractions[slot] * size;

    // The deterministic event variant this save sees for a slot: FNV-1a over key + product version + slot.
    // Version is in the mix so the same studio rolls different events project after project.
    public static MilestoneEvent EventFor(string key, int slot)
    {
        var variants = Events[slot];
        var hash = Fnv1a(key + "|" + SiliconAlleyState.GetVersion(key) + "|" + slot);
        return variants[(int)(hash % (uint)variants.Length)];
    }

    // A slot is pending exactly while: the studio sits in the slot's stage, progress is inside the window,
    // and the slot has not been resolved (by a choice or by expiry). Pure derivation — nothing persisted.
    public static bool IsPending(string key, int slot, SiliconAlleyState.ProjectStage stage, float progress, float size)
    {
        return slot >= 0 && slot < SlotCount
            && stage == SlotStage(slot)
            && !SiliconAlleyState.IsMilestoneResolved(key, slot)
            && progress >= FireProgress(slot, size)
            && progress < CloseProgress(slot, size);
    }

    // The first pending slot for the screen's decision card (windows never overlap, so 0 or 1 slot matches).
    public static bool TryGetPending(string key, SiliconAlleyState.ProjectStage stage, float progress, float size,
        out int slot, out MilestoneEvent evt)
    {
        for (var s = 0; s < SlotCount; s++)
        {
            if (!IsPending(key, s, stage, progress, size))
                continue;
            slot = s;
            evt = EventFor(key, s);
            return true;
        }
        slot = -1;
        evt = default;
        return false;
    }

    // The simulator's per-tick bookkeeping for one staffed hour of product work: silently expire any slot
    // whose window the project has passed (the neutral auto-resolve), and report the slot — if any — whose
    // window START was crossed this tick and is still open, so the caller can toast it. Returns -1 otherwise.
    public static int ProcessTick(string key, SiliconAlleyState.ProjectStage stage, float progressBefore,
        float progressAfter, float size)
    {
        var fired = -1;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (stage != SlotStage(slot) || SiliconAlleyState.IsMilestoneResolved(key, slot))
                continue;
            if (progressAfter >= CloseProgress(slot, size))
                SiliconAlleyState.MarkMilestoneResolved(key, slot); // window passed unanswered: neutral, silent
            else if (progressBefore < FireProgress(slot, size) && progressAfter >= FireProgress(slot, size))
                fired = slot;
        }
        return fired;
    }

    // Apply the player's choice: charge the cost (refusing cleanly if the player can't pay), apply the effect
    // bundle through existing levers, and mark the slot resolved. Returns false — and changes nothing — when
    // the slot is no longer pending (raced the expiry) or the cost can't be met.
    public static bool TryResolve(string key, int slot, int optionIndex, BuildingRegistration registration)
    {
        var stage = SiliconAlleyState.GetStage(key);
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        var progress = SiliconAlleyState.GetProgress(key);
        if (!IsPending(key, slot, stage, progress, size))
            return false;

        var evt = EventFor(key, slot);
        var option = optionIndex == 0 ? evt.OptionA : evt.OptionB;
        if (option.Cost > 0f && !SiliconAlleyMoney.TrySpend(registration, option.Cost, evt.TitleKey.GetLocalization()))
            return false;

        // Progress swings stay inside the stage band: never regress into the previous phase, never leap the
        // stage's park ceiling (so a milestone can neither un-develop a build nor release one).
        if (option.ProgressFraction != 0f)
        {
            var bandStart = SiliconAlleyState.PhaseEndProgress(
                stage == SiliconAlleyState.ProjectStage.Testing
                    ? SiliconAlleyState.ProjectPhase.Development
                    : SiliconAlleyState.ProjectPhase.Design, size);
            SiliconAlleyState.AdjustProgressClamped(key, option.ProgressFraction * size,
                bandStart, SiliconAlleyState.StageCeiling(stage, size));
        }
        if (option.BugsScaleDelta > 0f)
            SiliconAlleyState.AddBugs(key, option.BugsScaleDelta * SiliconAlleyState.BugScale);
        else if (option.BugsScaleDelta < 0f)
            SiliconAlleyState.BurnBugs(key, -option.BugsScaleDelta * SiliconAlleyState.BugScale);
        if (option.BugsPercentDelta != 0f)
        {
            var delta = SiliconAlleyState.GetBugCount(key) * option.BugsPercentDelta;
            if (delta > 0f) SiliconAlleyState.AddBugs(key, delta);
            else if (delta < 0f) SiliconAlleyState.BurnBugs(key, -delta);
        }
        if (option.Awareness != 0f)
            SiliconAlleyState.AddAwareness(key, option.Awareness);
        if (option.Hype != 0f)
            SiliconAlleyState.AddHype(key, option.Hype);
        if (option.QualitySample >= 0f && option.QualityWeight > 0f)
        {
            var phase = stage == SiliconAlleyState.ProjectStage.Testing
                ? SiliconAlleyState.ProjectPhase.Testing
                : SiliconAlleyState.ProjectPhase.Development;
            SiliconAlleyState.AccumulateQuality(key, phase, option.QualitySample, option.QualityWeight);
        }

        SiliconAlleyState.MarkMilestoneResolved(key, slot);
        Debug.Log($"[SiliconAlley] {key} resolved milestone {slot} ({evt.Id}) with option {optionIndex}.");
        return true;
    }

    // FNV-1a 32-bit — a tiny string hash that is stable across processes and runtimes (string.GetHashCode
    // is not), so the variant pick above survives save/reload and cannot be rerolled.
    private static uint Fnv1a(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
