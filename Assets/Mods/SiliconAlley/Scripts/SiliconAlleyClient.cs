using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
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

    // A business is the player's when its type is ours and it has no rival owner (businessOwnerRivalId
    // empty) — the same rule the client dashboard uses (SiliconAlleyClientDialog.BuildStatus).
    public static bool IsPlayerOwned(BuildingRegistration registration)
        => registration?.businessTypeName != null
           && registration.businessTypeName.StartsWith(BusinessTypePrefix)
           && string.IsNullOrEmpty(registration.businessOwnerRivalId);

    private static bool PlayerOwnsStudio()
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
        var status = BuildStatus();
        // Issue #27: if a studio is free to take a contract, offer one (Accept/Hang up); else just show status.
        if (TryGenerateOffer())
        {
            var days = Mathf.Max(0, _offerDeadlineDay - TimeHelper.CurrentDay);
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "siliconalley:client_contract_offer".Localize(new Dictionary<string, string>
                {
                    ["status"] = status,
                    ["studio"] = _offerStudioName,
                    ["days"] = days.ToString(CultureInfo.InvariantCulture),
                    ["payout"] = "$" + Mathf.RoundToInt(_offerPayout).ToString("N0", CultureInfo.InvariantCulture),
                }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "siliconalley:client_contract_accept".Localize(),
                OnConfirm = AcceptOffer,
                OnCancel = DialogController.current.CancelDialog,
            };
        }

        return new DialogEntry
        {
            headerKey = npcNameKey,
            messageData = "siliconalley:client_status".Localize(
                new Dictionary<string, string> { ["status"] = status }),
            Template = DialogEntry.TemplateType.Text,
            OnCancel = DialogController.current.CancelDialog,
        };
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

    // Step 1 (visibility): an on-demand status readout of the player's Silicon Alley studios, built
    // from the same live SiliconAlleyState the simulator maintains and persists. This lets the player
    // verify progress / reputation / installed base — including across a save/reload. A business is
    // the player's when its type is ours and it has no rival owner (businessOwnerRivalId empty).
    private static string BuildStatus()
    {
        var current = SaveGameManager.Current;
        if (current == null || current.BuildingRegistrations == null)
            return "siliconalley:client_status_none".GetLocalization();

        var builder = new StringBuilder();
        var any = false;
        foreach (var registration in current.BuildingRegistrations)
        {
            if (!SiliconAlleyClient.IsPlayerOwned(registration))
                continue;

            any = true;
            var key = SiliconAlleyState.KeyFor(registration);
            var rawProgress = SiliconAlleyState.GetProgress(key);
            var kind = SiliconAlleyState.GetProjectType(key);
            var size = SiliconAlleyState.EffectiveProjectSize(key);
            var phase = SiliconAlleyState.PhaseOf(rawProgress, size);
            var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(registration);
            var line = "siliconalley:client_status_line".Localize(new Dictionary<string, string>
            {
                ["business"] = registration.GetDisplayName(),
                ["type"] = SiliconAlleyState.ProjectTypeNameKey(kind).GetLocalization(),
                ["phase"] = SiliconAlleyState.PhaseNameKey(phase).GetLocalization(),
                ["progress"] = Mathf.RoundToInt(SiliconAlleyState.PhaseProgressFraction(rawProgress, size) * 100f).ToString(CultureInfo.InvariantCulture),
                ["phaseeta"] = FormatEta(SiliconAlleyState.PhaseEndProgress(phase, size) - rawProgress, perHour),
                ["shipeta"] = FormatEta(size - rawProgress, perHour),
                ["quality"] = FormatQuality(SiliconAlleyState.GetAverageQuality(key)),
                ["reputation"] = SiliconAlleyState.GetReputation(key).ToString("F2", CultureInfo.InvariantCulture),
                ["installedbase"] = SiliconAlleyState.GetInstalledBase(key).ToString(CultureInfo.InvariantCulture),
                ["support"] = SupportPerDay(registration, key),
                ["patcheta"] = PatchEta(registration, key),
                ["rivals"] = SiliconAlleyOfficeSimulator.CompetitorCount(registration).ToString(CultureInfo.InvariantCulture),
                ["market"] = SiliconAlleyOfficeSimulator.MarketFactor(registration, kind).ToString("F2", CultureInfo.InvariantCulture),
                // Issue #28: the category's current market demand (a per-type cycle) + a rising/falling hint so
                // the player can time releases. Derived from the day — the same value the simulator pays out at.
                ["demand"] = SiliconAlleyMarket.DemandFactor(registration.businessTypeName, TimeHelper.CurrentDay).ToString("F2", CultureInfo.InvariantCulture),
                ["trend"] = SiliconAlleyMarket.IsRising(registration.businessTypeName, TimeHelper.CurrentDay) ? "▲" : "▼",
            }).ToString();
            builder.Append("\n\n").Append(line);
        }

        if (!any)
            return "siliconalley:client_status_none".GetLocalization();
        return "siliconalley:client_status_intro".GetLocalization() + builder;
    }

    // Estimated recurring support income per day = installed base x product market price x support rate
    // — the same factors the simulator accrues hourly (SupportRatePerDay), shown so the player can see
    // the support-income channel exists and grows with the installed base.
    private static string SupportPerDay(BuildingRegistration registration, string key)
    {
        var installedBase = SiliconAlleyState.GetInstalledBase(key);
        var perDay = 0f;
        if (installedBase > 0)
        {
            var businessType = BusinessTypeHelper.GetData(registration);
            if (businessType?.businessProducts != null && businessType.businessProducts.Length > 0)
            {
                var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
                if (item != null)
                    // Issue #28: include the current market demand so this estimate matches the demand-scaled
                    // support the simulator actually credits.
                    perDay = installedBase * item.DefaultMarketPrice * SiliconAlleyState.SupportRatePerDay
                        * SiliconAlleyMarket.DemandFactor(registration.businessTypeName, TimeHelper.CurrentDay);
            }
        }
        return "$" + Mathf.RoundToInt(perDay).ToString("N0", CultureInfo.InvariantCulture) + "/day";
    }

    // Per-phase ETA: remaining progress / current hourly throughput, rendered as a short "~Nd Nh".
    // perHour is this hour's live staffing (SiliconAlleyOfficeSimulator.CurrentHourlyProgress), so an
    // unstaffed studio reports "needs staff" rather than an infinite ETA.
    private static string FormatEta(float remainingProgress, float perHour)
    {
        if (perHour <= 0f)
            return "siliconalley:client_eta_idle".GetLocalization();
        var hours = Mathf.CeilToInt(Mathf.Max(0f, remainingProgress) / perHour);
        if (hours <= 0)
            return "siliconalley:client_eta_due".GetLocalization();
        var days = hours / 24;
        var rest = hours % 24;
        if (days > 0)
            return "~" + days.ToString(CultureInfo.InvariantCulture) + "d " + rest.ToString(CultureInfo.InvariantCulture) + "h";
        return "~" + rest.ToString(CultureInfo.InvariantCulture) + "h";
    }

    // Estimated shipped quality of the in-flight project (the phase-weighted average the simulator
    // accrues), or "—" before any quality has accrued. Steers the player toward staffing Testing well.
    private static string FormatQuality(float quality)
    {
        if (quality < 0f)
            return "—";
        return Mathf.RoundToInt(Mathf.Clamp01(quality) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    // Days until this studio next patches its live catalog (only meaningful once it has shipped, i.e.
    // installed base > 0); "due now" when the interval has elapsed, "—" with nothing released yet.
    private static string PatchEta(BuildingRegistration registration, string key)
    {
        if (SiliconAlleyState.GetInstalledBase(key) <= 0)
            return "—";
        var daysUntil = SiliconAlleyOfficeSimulator.PatchIntervalDays - (TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key));
        if (daysUntil <= 0)
            return "siliconalley:client_eta_due".GetLocalization();
        return "~" + daysUntil.ToString(CultureInfo.InvariantCulture) + "d";
    }
}
