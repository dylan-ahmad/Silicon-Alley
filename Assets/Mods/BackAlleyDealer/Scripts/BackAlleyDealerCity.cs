using System.Threading.Tasks;
using BackAlleyDealer;
using BAModAPI;
using BAModAPI.Services;
using Dialogs;
using Entities;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

[assembly: RegisterModClass(typeof(BackAlleyDealerCity))]

namespace BackAlleyDealer
{
    [ModEntryOnCityLoad]
    public class BackAlleyDealerCity : IModBigAmbitions
    {
        private const string BundleKey = "AssetBundles/backalleydealer.unity3d";
        private const ContactCategoryName DealerCategory = ContactCategoryName.FurnitureAndEquipment;
        private const string DealerDescription = "backalleydealer:description";
        public const string DealerName = "backalleydealer-dealername";
        public static readonly Address DealerAddress = new("backalleydealer:street_anonAve", 420);

        private Contact _contact;
        private ModContext _context;
        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            _context = context;
            RegisterContact();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            return Task.CompletedTask;
        }

        private void RegisterContact()
        {
            var bundle = AssetService.GetBundle(_context.ModId, BundleKey);
            var contactSprite = bundle.LoadAsset<Sprite>($"Assets/Mods/BackAlleyDealer/{DealerName}.png");
            var oldIcons = GlobalReferences.Instance.contactIcons;
            var newIcons = new Sprite[oldIcons.Length + 1];
            for (var i = 0; i < oldIcons.Length; i++)
                newIcons[i] = oldIcons[i];

            newIcons[oldIcons.Length] = contactSprite;
            GlobalReferences.Instance.contactIcons = newIcons;

            _contact = Contact.GetContact(DealerName, DealerCategory, DealerDescription);
            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("backalleydealer_calldialogtype");
            _contact.callDialogTypeOverride = dialogType;
            _context.Logger.Info("Contact created");

            CallDialogFactory.RegisterDialog(dialogType, () => new BackAlleyDealerDialog());
            _context.Logger.Info("Registered dialog");
            if (_contact.messagesQueue == null || _contact.messagesQueue.Count == 0)
                SendWelcomeMessage();
        }

        private void SendWelcomeMessage()
        {
            var textMessage = new TextMessage("backalleydealer:textmessage_welcome");
            _contact.SendMessage(textMessage, sendNotificationInstantly: true);
            _context.Logger.Info("Sent welcome message");
        }
    }
}