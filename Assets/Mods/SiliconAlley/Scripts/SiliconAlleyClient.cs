using System;
using System.Threading.Tasks;
using BAModAPI;
using Dialogs;
using Entities;
using Localizor;
using UI.Dialog;
using UI.Smartphone.Apps.Contacts;

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
            messageData = "siliconalley:client_greeting".Localize(),
            Template = DialogEntry.TemplateType.Text,
        };
    }
}
