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

    private Contact _contact;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("siliconalley_clientdialog");
        _contact = Contact.GetContact(ContactName, ContactCategoryName.Business, ContactDescription);
        _contact.callDialogTypeOverride = dialogType;
        CallDialogFactory.RegisterDialog(dialogType, () => new SiliconAlleyClientDialog());

        if (_contact.messagesQueue == null || _contact.messagesQueue.Count == 0)
            _contact.SendMessage(new TextMessage("siliconalley:client_welcome"), sendNotificationInstantly: true);

        context.Logger.Info("SiliconAlley: client contact registered.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

public class SiliconAlleyClientDialog : Dialog
{
    public SiliconAlleyClientDialog()
    {
        npcNameKey = "siliconalley-clientname";
        DialogController.current.ShowEntry(Start());
    }

    private DialogEntry Start()
    {
        return new DialogEntry
        {
            messageData = "siliconalley:client_status".Localize(
                new Dictionary<string, string> { ["status"] = BuildStatus() }),
            Template = DialogEntry.TemplateType.Text,
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
            if (registration?.businessTypeName == null
                || !registration.businessTypeName.StartsWith("siliconalley:")
                || !string.IsNullOrEmpty(registration.businessOwnerRivalId))
                continue;

            any = true;
            var key = SiliconAlleyState.KeyFor(registration);
            var rawProgress = SiliconAlleyState.GetProgress(key);
            var phase = SiliconAlleyState.PhaseOf(rawProgress);
            var line = "siliconalley:client_status_line".Localize(new Dictionary<string, string>
            {
                ["business"] = registration.GetDisplayName(),
                ["phase"] = SiliconAlleyState.PhaseNameKey(phase).GetLocalization(),
                ["progress"] = Mathf.RoundToInt(SiliconAlleyState.PhaseProgressFraction(rawProgress) * 100f).ToString(CultureInfo.InvariantCulture),
                ["reputation"] = SiliconAlleyState.GetReputation(key).ToString("F2", CultureInfo.InvariantCulture),
                ["installedbase"] = SiliconAlleyState.GetInstalledBase(key).ToString(CultureInfo.InvariantCulture),
                ["support"] = SupportPerDay(registration, key),
            }).ToString();
            builder.Append('\n').Append(line);
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
                    perDay = installedBase * item.DefaultMarketPrice * SiliconAlleyState.SupportRatePerDay;
            }
        }
        return "$" + Mathf.RoundToInt(perDay).ToString("N0", CultureInfo.InvariantCulture) + "/day";
    }
}
