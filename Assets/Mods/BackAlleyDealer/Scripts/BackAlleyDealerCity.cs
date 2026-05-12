using System;
using System.Linq;
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
        private const string DealerName = "Back Alley Dealer";
        private const ContactCategoryName DealerCategory = ContactCategoryName.FurnitureAndEquipment;
        private const string DealerDescription =
            "A shady dealer offering high-quality furniture, vehicles and equipment at a premium price.";

        private static readonly Address DealerAddress = new("backalleydealer:street_anonAve", 420);

        private ModContext _context;
        private Contact _contact;

        public string[] RelativeAssetBundlePaths { get; }

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
            _context.Logger.Info("Registering contact");
            var bundle = AssetService.GetBundle(_context.ModId, BundleKey);
            _context.Logger.Info("Loading sprite");
            var contactSprite = bundle.LoadAsset<Sprite>("Assets/Mods/BackAlleyDealer/BackAlleyDealerContactIcon.png");
            _context.Logger.Info("Received sprite");
            GlobalReferences.Instance.contactIcons =
                GlobalReferences.Instance.contactIcons.Append(contactSprite).ToArray();
            _contact = Contact.GetContact(DealerName, DealerCategory, DealerDescription, DealerAddress, hasWelcomeMessages: true);
            
            var dealerDialogType = (CallDialogType)ModEnumHash.GetSafeHash("backalleydealer_calldialogtype");
            CallDialogFactory.RegisterDialog(dealerDialogType, () => new BackAlleyDealerDialog());
            _context.Logger.Info("Contact registered with welcome message check");
            if (_contact.messagesQueue == null || _contact.messagesQueue.Count == 0)
                SendWelcomeMessage();
        }

        private void SendWelcomeMessage()
        {
            var textMessage = new TextMessage("backalleydealer:textmessage_welcome");
            _contact.SendMessage(textMessage, sendNotificationInstantly: true);
        }
    }
}