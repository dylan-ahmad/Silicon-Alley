using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BAModAPI;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using UI.Dialog;
using UI.Notification;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

[assembly: RegisterModClass(typeof(SiliconAlleyClient))]

// Tier 3: a phone contact representing the studio's clients. Registers a contact, sends a welcome
// message, and opens a short dialog when called. Modeled on the BackAlleyDealer example (lighter —
// no commerce). Runs on city load.
[ModEntryOnCityLoad]
public class SiliconAlleyClient : IModBigAmbitions
{
    private const string ContactName = "siliconalley-clientname";
    private const string ContactDescription = "siliconalley:client_description";
    private const string WelcomeMessageKey = "siliconalley:client_welcome";

    // Identifier prefix shared by all three Silicon Alley business types; the ownership rule lives in
    // IsPlayerOwned so the client gating and the dashboard agree on what "the player's studio" means.
    public const string BusinessTypePrefix = "siliconalley:";

    // One-time "welcome delivered" flag, persisted in GameInstance.modData (which the game serializes
    // with the save) so the welcome is never re-sent on a later load.
    private const string WelcomeSentKey = "SiliconAlley.ClientWelcomeSent";

    // Issue #68: one-time "first-run help nudge shown" flag. A SEPARATE modData key from the welcome (and
    // with its own subscription below) so a save that already received the welcome still gets the nudge once.
    private const string HelpNudgeSentKey = "SiliconAlley.HelpNudgeSent";

    private Contact _contact;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("siliconalley_clientdialog");
        _contact = Contact.GetContact(ContactName, ContactCategoryName.Business, ContactDescription);
        _contact.callDialogTypeOverride = dialogType;
        CallDialogFactory.RegisterDialog(dialogType, () => new SiliconAlleyClientDialog());

        // The welcome is gated on the player actually owning a Silicon Alley business and is sent
        // once. Defer it to the hourly tick so it lands shortly after the studio is founded (reads
        // like a client reaching out) instead of firing instantly on every city load. Static handler
        // + remove-then-add de-duplicates the subscription across repeated city loads (mirrors
        // SiliconAlleyPersistence).
        if (!WelcomeAlreadySent())
        {
            GlobalEvents.onNewHour -= TrySendWelcome;
            GlobalEvents.onNewHour += TrySendWelcome;
        }

        // Issue #68: the first-run help nudge rides the same hourly tick + studio-owned gate as the welcome,
        // but on its OWN flag/subscription so a save that already saw the welcome still gets the nudge once.
        if (!HelpNudgeAlreadySent())
        {
            GlobalEvents.onNewHour -= TryShowHelpNudge;
            GlobalEvents.onNewHour += TryShowHelpNudge;
        }

        context.Logger.Info("SiliconAlley: client contact registered.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        GlobalEvents.onNewHour -= TrySendWelcome;
        GlobalEvents.onNewHour -= TryShowHelpNudge;
        return Task.CompletedTask;
    }

    // Each in-game hour, send the welcome the first time the player owns at least one Silicon Alley
    // business, then persist a flag and unsubscribe so it never re-sends (this session or a later one).
    private static void TrySendWelcome()
    {
        if (SaveGameManager.Current == null)
            return;
        if (WelcomeAlreadySent())
        {
            GlobalEvents.onNewHour -= TrySendWelcome;
            return;
        }
        if (!PlayerOwnsStudio())
            return;

        var contact = Contact.GetContact(ContactName, ContactCategoryName.Business, ContactDescription);
        contact.SendMessage(new TextMessage(WelcomeMessageKey), sendNotificationInstantly: true);
        MarkWelcomeSent();
        GlobalEvents.onNewHour -= TrySendWelcome;
    }

    // Issue #68: once the player owns a studio, show a single clickable toast pointing at the in-game guide,
    // then persist a flag and unsubscribe so it never re-fires. The click opens BA's native Help at the
    // Silicon Alley overview (works even if the #64 sidebar injection no-ops — the page renders from its
    // locale key regardless). Mirrors TrySendWelcome's gate/lifecycle exactly.
    private static void TryShowHelpNudge()
    {
        if (SaveGameManager.Current == null)
            return;
        if (HelpNudgeAlreadySent())
        {
            GlobalEvents.onNewHour -= TryShowHelpNudge;
            return;
        }
        if (!PlayerOwnsStudio())
            return;

        Notifications.Show(NotificationType.Info, "siliconalley:notify_helpnudge", null, 8f,
            "siliconalley:helpnudge", () => SiliconAlleyHelp.OpenOverview());
        MarkHelpNudgeSent();
        GlobalEvents.onNewHour -= TryShowHelpNudge;
    }

    // A business is the player's when the registration is rented by the player and its type is ours.
    // Rival-owner fields are not reliable for freshly started player companies.
    public static bool IsPlayerOwned(BuildingRegistration registration)
        => registration?.businessTypeName != null
           && registration.RentedByPlayer
           && registration.businessTypeName.StartsWith(BusinessTypePrefix, StringComparison.Ordinal);

    // True when the player owns at least one Silicon Alley studio. Public so the client dialog can gate its
    // "View studios" offer (issue #59) on the same rule the welcome + dashboard use.
    public static bool PlayerOwnsStudio()
    {
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return false;
        foreach (var registration in current.BuildingRegistrations)
            if (IsPlayerOwned(registration))
                return true;
        return false;
    }

    private static bool WelcomeAlreadySent()
    {
        var current = SaveGameManager.Current;
        return current?.modData != null
               && current.modData.TryGetValue(WelcomeSentKey, out var value)
               && value == "true";
    }

    private static void MarkWelcomeSent()
    {
        var current = SaveGameManager.Current;
        if (current?.modData != null)
            current.modData[WelcomeSentKey] = "true";
    }

    private static bool HelpNudgeAlreadySent()
    {
        var current = SaveGameManager.Current;
        return current?.modData != null
               && current.modData.TryGetValue(HelpNudgeSentKey, out var value)
               && value == "true";
    }

    private static void MarkHelpNudgeSent()
    {
        var current = SaveGameManager.Current;
        if (current?.modData != null)
            current.modData[HelpNudgeSentKey] = "true";
    }
}

public class SiliconAlleyClientDialog : Dialog
{
    // Issue #125: the studios eligible for a contract this call (player-owned, no active contract). The
    // offer entry cycles them with "Next studio"; each studio shows its own DETERMINISTIC offer
    // (SiliconAlleyContracts.OfferFor — same terms on every redial until the 3-day window rolls).
    private readonly List<(string key, string name, BuildingRegistration registration)> _eligible =
        new List<(string key, string name, BuildingRegistration registration)>();

    public SiliconAlleyClientDialog()
    {
        npcNameKey = "siliconalley-clientname";
        DialogController.current.ShowEntry(Start());
    }

    private DialogEntry Start()
    {
        // No studios yet: a text nudge (the card dashboard would be empty), Hang up only.
        if (!SiliconAlleyClient.PlayerOwnsStudio())
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = SiliconAlleyRegistry.NoStudioLocalizationKey(
                    "siliconalley:client_status_none",
                    "siliconalley:client_registration_failed").Localize(),
                Template = DialogEntry.TemplateType.Text,
                // Issue #68: nothing to do yet, so offer the guide as the primary action.
                ConfirmTextOverride = "siliconalley:client_open_guide".Localize(),
                OnConfirm = OpenHelp,
                OnCancel = DialogController.current.CancelDialog,
            };

        // Issue #27/#125: offer the first eligible studio its contract. Three native buttons (the Dialog
        // ceiling): Accept for {studio} · Next studio (or View studios when only one is free) · Hang up.
        CollectEligibleStudios();
        if (_eligible.Count > 0)
            return OfferEntry(0);

        // Issue #59: no contract on offer — a short greeting + "View studios" opens the card dashboard.
        return new DialogEntry
        {
            headerKey = npcNameKey,
            messageData = "siliconalley:client_greeting".Localize(),
            Template = DialogEntry.TemplateType.Text,
            ConfirmTextOverride = "siliconalley:client_view_studios".Localize(),
            OnConfirm = OpenDashboard,
            // Issue #68: the spare button opens the in-game guide.
            SecondOptionTextOverride = "siliconalley:client_open_guide", // #152: this field takes a KEY, not text
            OnSecondOption = OpenHelp,
            OnCancel = DialogController.current.CancelDialog,
        };
    }

    // Issue #59: open the card dashboard and end the call. ConfirmCurrentEntry/SecondOptionCurrentEntry only
    // show a follow-up when the handler returns non-null, so returning null after FinishDialog cleanly closes
    // the call (verified against the decompiled DialogController).
    private DialogEntry OpenDashboard()
    {
        SiliconAlleyDashboardScreen.Open();
        DialogController.current.FinishDialog();
        return null;
    }

    // Issue #68: open BA's native Help at the Silicon Alley overview, then end the call (same pattern as
    // OpenDashboard). The guaranteed entry point even if the #64 sidebar injection ever no-ops.
    private DialogEntry OpenHelp()
    {
        SiliconAlleyHelp.OpenOverview();
        DialogController.current.FinishDialog();
        return null;
    }

    // Issue #125: every player-owned studio without an active contract, in registration order. Re-collected
    // per call; the ELIGIBLE list is what "Next studio" cycles.
    private void CollectEligibleStudios()
    {
        _eligible.Clear();
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return;
        foreach (var registration in current.BuildingRegistrations)
        {
            if (!SiliconAlleyClient.IsPlayerOwned(registration))
                continue;
            var key = SiliconAlleyState.KeyFor(registration);
            if (SiliconAlleyState.HasContract(key))
                continue;
            // Issue #152: keep the registration — the offer now states its WORKLOAD as a duration at that
            // studio's current staffing, which needs the building to read throughput from.
            _eligible.Add((key, registration.GetDisplayName(), registration));
        }
    }

    // Issue #125: the offer entry for one eligible studio. "Next studio" chains to the next entry via the
    // non-null OnSecondOption return (ShowEntry — the base game's own negotiate-counter-offer pattern).
    //
    // Issue #152: the terms are scan lines now, not a paragraph with the two numbers that matter buried
    // mid-sentence — payout, deadline and (shown for the first time) the WORKLOAD, expressed as a duration
    // at this studio's current staffing because the raw scope figure means nothing to a player. Note the
    // deliberate wording: "workload", never "scope", which the design wizard already uses for the
    // Quick/Standard/Ambitious project size.
    //
    // The buttons no longer change meaning with the offer count. The middle slot is "Next studio" when
    // there is a next studio and is simply ABSENT otherwise (a button renders only when its callback is
    // non-null) — it used to turn into "View studios", which ended the call from the same orange slot.
    private DialogEntry OfferEntry(int index)
    {
        var (key, name, registration) = _eligible[index];
        var offer = SiliconAlleyContracts.OfferFor(key, TimeHelper.CurrentDay);
        var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(registration);
        var body = new Dictionary<string, string>
        {
            ["studio"] = name,
            ["days"] = offer.DeadlineDays.ToString(CultureInfo.InvariantCulture),
            ["payout"] = SiliconAlleyFormat.Money(offer.Payout),
            ["workload"] = SiliconAlleyFormat.Eta(offer.Scope, perHour),
        };
        var entry = new DialogEntry
        {
            headerKey = npcNameKey,
            // With more than one offer on the table the body also says which one you are looking at.
            messageData = (_eligible.Count > 1
                ? "siliconalley:client_contract_offer_multi"
                : "siliconalley:client_contract_offer").Localize(Position(body, index)),
            Template = DialogEntry.TemplateType.Text,
            ConfirmTextOverride = "siliconalley:client_contract_accept_for".Localize(new Dictionary<string, string>
            {
                ["studio"] = name,
            }),
            OnConfirm = () => AcceptOffer(index),
            OnCancel = DialogController.current.CancelDialog,
        };
        if (_eligible.Count > 1)
        {
            // SecondOptionTextOverride is consumed as a LOCALIZATION KEY (unlike ConfirmTextOverride, which
            // takes a data holder) — passing pre-localized text only worked because Localizor passes unknown
            // keys through, and would have broken in any other language.
            entry.SecondOptionTextOverride = "siliconalley:client_contract_next";
            entry.OnSecondOption = () => OfferEntry((index + 1) % _eligible.Count);
        }
        return entry;
    }

    // "2 of 4" — only meaningful while several studios are on offer; harmless to add either way.
    private Dictionary<string, string> Position(Dictionary<string, string> data, int index)
    {
        data["n"] = (index + 1).ToString(CultureInfo.InvariantCulture);
        data["m"] = _eligible.Count.ToString(CultureInfo.InvariantCulture);
        return data;
    }

    // Accept the shown studio's deterministic offer (a no-op-safe state write), then confirm. The absolute
    // deadline is fixed here, at accept time, so a shown-but-unaccepted offer never ages within its window.
    private DialogEntry AcceptOffer(int index)
    {
        var (key, name, registration) = _eligible[index];
        var offer = SiliconAlleyContracts.OfferFor(key, TimeHelper.CurrentDay);
        var deadlineDay = TimeHelper.CurrentDay + offer.DeadlineDays;
        SiliconAlleyState.AcceptContract(key, offer.Scope, deadlineDay, offer.Payout);
        return new DialogEntry
        {
            headerKey = npcNameKey,
            // #152: the confirmation repeats the same three scan lines the offer showed, so the terms you
            // just agreed to are still on screen in the same shape.
            messageData = "siliconalley:client_contract_accepted".Localize(new Dictionary<string, string>
            {
                ["studio"] = name,
                ["days"] = offer.DeadlineDays.ToString(CultureInfo.InvariantCulture),
                ["payout"] = SiliconAlleyFormat.Money(offer.Payout),
                ["workload"] = SiliconAlleyFormat.Eta(offer.Scope,
                    SiliconAlleyOfficeSimulator.CurrentHourlyProgress(registration)),
            }),
            Template = DialogEntry.TemplateType.Text,
            OnCancel = DialogController.current.FinishDialog,
        };
    }

}
