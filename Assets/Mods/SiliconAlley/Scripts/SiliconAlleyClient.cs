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

        context.Logger.Info("SiliconAlley: client contact registered.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        GlobalEvents.onNewHour -= TrySendWelcome;
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
}

public class SiliconAlleyClientDialog : Dialog
{
    // Issue #27: the contract offer generated for this call (terms shown to the player, then applied on Accept).
    private string _offerKey, _offerStudioName;
    private float _offerScope, _offerPayout;
    private int _offerDeadlineDay;

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
                OnCancel = DialogController.current.CancelDialog,
            };

        // Issue #27: if a studio is free to take a contract, offer one. Three native buttons (the Dialog
        // ceiling): Accept contract · View studios · Hang up. Issue #59: the per-studio status now lives in
        // the card dashboard (SiliconAlleyDashboardScreen), so the offer message carries only the terms.
        if (TryGenerateOffer())
        {
            var days = Mathf.Max(0, _offerDeadlineDay - TimeHelper.CurrentDay);
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "siliconalley:client_contract_offer".Localize(new Dictionary<string, string>
                {
                    ["studio"] = _offerStudioName,
                    ["days"] = days.ToString(CultureInfo.InvariantCulture),
                    ["payout"] = "$" + Mathf.RoundToInt(_offerPayout).ToString("N0", CultureInfo.InvariantCulture),
                }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "siliconalley:client_contract_accept".Localize(),
                OnConfirm = AcceptOffer,
                SecondOptionTextOverride = "siliconalley:client_view_studios".GetLocalization(),
                OnSecondOption = OpenDashboard,
                OnCancel = DialogController.current.CancelDialog,
            };
        }

        // Issue #59: no contract on offer — a short greeting + "View studios" opens the card dashboard.
        return new DialogEntry
        {
            headerKey = npcNameKey,
            messageData = "siliconalley:client_greeting".Localize(),
            Template = DialogEntry.TemplateType.Text,
            ConfirmTextOverride = "siliconalley:client_view_studios".Localize(),
            OnConfirm = OpenDashboard,
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

    // Pick the first player-owned studio without an active contract and roll a fresh offer for it. Returns
    // false (no Accept button) when every studio already holds a contract or the player owns none.
    private bool TryGenerateOffer()
    {
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return false;
        foreach (var registration in current.BuildingRegistrations)
        {
            if (!SiliconAlleyClient.IsPlayerOwned(registration))
                continue;
            var key = SiliconAlleyState.KeyFor(registration);
            if (SiliconAlleyState.HasContract(key))
                continue;
            _offerKey = key;
            _offerStudioName = registration.GetDisplayName();
            _offerScope = UnityEngine.Random.Range(800f, 1600f);              // a few in-game days of staffed work
            _offerDeadlineDay = TimeHelper.CurrentDay + UnityEngine.Random.Range(14, 31); // 14–30 days
            _offerPayout = _offerScope * UnityEngine.Random.Range(2.5f, 4f);  // flat fee, scope-proportional
            return true;
        }
        return false;
    }

    // Accept the offered contract for the chosen studio (a no-op-safe state write), then confirm.
    private DialogEntry AcceptOffer()
    {
        SiliconAlleyState.AcceptContract(_offerKey, _offerScope, _offerDeadlineDay, _offerPayout);
        return new DialogEntry
        {
            headerKey = npcNameKey,
            messageData = "siliconalley:client_contract_accepted".Localize(new Dictionary<string, string>
            {
                ["studio"] = _offerStudioName,
                ["days"] = Mathf.Max(0, _offerDeadlineDay - TimeHelper.CurrentDay).ToString(CultureInfo.InvariantCulture),
                ["payout"] = "$" + Mathf.RoundToInt(_offerPayout).ToString("N0", CultureInfo.InvariantCulture),
            }),
            Template = DialogEntry.TemplateType.Text,
            OnCancel = DialogController.current.FinishDialog,
        };
    }

}
