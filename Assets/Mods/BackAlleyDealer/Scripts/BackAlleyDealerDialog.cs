using System.Collections.Generic;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Streets;
using UI.Dialog;
using UI.Notification;

namespace BackAlleyDealer
{
    public class BackAlleyDealerDialog : Dialog
    {
        public BackAlleyDealerDialog()
        {
            npcNameKey = "backalleydealer:dealer_name";

            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            DialogController.current.contact.SendMessage(
                new TextMessage("backalleydealer:dialog_start", null, true, true));

            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_start".Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "backalleydealer:dialog_option_merchandise".Localize(),
                SecondOptionTextOverride = "backalleydealer:dialog_option_vehicles",
                OnConfirm = StartFurniture,
                OnSecondOption = StartVehicles,
            };
        }

        #region Furniture

        private DialogEntry StartFurniture()
        {
            var hasAtLeastOneAddress = SaveGameManager.Current.BuildingRegistrations.Any(x => x.RentedByPlayer);
            var hasAlreadyADelivery =
                SaveGameManager.Current.FurnitureDeliveryContracts.Any(x =>
                    x.fromAddress == DialogController.current.contact.Address);
            return !hasAtLeastOneAddress
                ? NoAddresses()
                : hasAlreadyADelivery
                    ? AlreadyHasAPendingDelivery(false)
                    : new DialogEntry
                    {
                        messageData = "backalleydealer:dialog_furniture_start".Localize(),
                        Template = DialogEntry.TemplateType.Text,
                        headerKey = npcNameKey,
                        OnVisible = () => DialogController.current.ShowEntry(FurnitureDeliveryContract())
                    };
        }

        private DialogEntry FurnitureDeliveryContract()
        {
            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_furniture_player_start".Localize(),
                headerKey = "dialog_furniture_delivery_header",
                Template = DialogEntry.TemplateType.Input,
                ConfirmTextOverride = "furniture_delivery_dialog_order".Localize(),
                InputTemplate = DialogEntry.InputTemplateName.FurnitureDeliverySettings,
                OnConfirm = OnDeliveryContractSettingsSet,
                OnCancel = DialogController.current.CancelDialog,
                onCancelMessage = new TextMessage(LegacyRef.MessageType.ContactsMessagePlayerCancelCall)
            };
        }

        private DialogEntry NoAddresses()
        {
            DialogController.current.contact.SendMessage(
                new TextMessage(LegacyRef.MessageType.DialogFurnitureStoreNoAddresses, null, true, true));
            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_furniture_no_addresses".Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnVisible =
                    DialogController.current.dialogType == DialogType.PhoneCall
                        ? DialogController.current.FinishDialog
                        : null,
                OnCancel = DialogController.current.dialogType == DialogType.Physical
                    ? DialogController.current.FinishDialog
                    : null
            };
        }

        private DialogEntry AlreadyHasAPendingDelivery(bool continueConversation)
        {
            return new DialogEntry
            {
                messageData = (continueConversation
                        ? "backalleydealer:dialog_anything_else"
                        : "backalleydealer:dialog_type_of_goods"
                    ).Localize(),
                headerKey = npcNameKey,
                OnCancel = DialogController.current.FinishDialog,
                OnConfirm = FurnitureDeliveryContract,
                ConfirmTextOverride = "furniture_delivery_dialog_order".Localize(),
                OnSecondOption = OnShowFurnitureDeliveriesList,
                SecondOptionTextOverride = "dialog_furniture_deliveries_manage_deliveries"
            };
        }

        private DialogEntry OnShowFurnitureDeliveriesList()
        {
            return new DialogEntry
            {
                OnCancel = DialogController.current.CancelDialog,
                Template = DialogEntry.TemplateType.Input,
                InputTemplate = DialogEntry.InputTemplateName.FurnitureDeliveriesList
            };
        }

        private DialogEntry OnDeliveryContractSettingsSet()
        {
            var deliveryContractSettings =
                DialogController.current.GetInputComponent<FurnitureDeliveryContractSettings>();

            if (deliveryContractSettings.selectedAddress == null)
            {
                Notifications.ShowError(
                    "common_notification_select_address",
                    "common_notification_select_address");
                return null;
            }

            if (deliveryContractSettings.selectedDeliverySlot == (-1, -1))
            {
                Notifications.ShowError(
                    "common_notification_select_delivery_time",
                    "common_notification_select_delivery_time");
                return null;
            }

            if (deliveryContractSettings.TotalItemsToDeliverAmount <= 0)
            {
                Notifications.ShowError(
                    "dialog_furniture_delivery_select_items_notification",
                    "dialog_furniture_delivery_select_items_notification");
                return null;
            }

            var itemsToDeliver = deliveryContractSettings.itemsToDeliver
                .Select(item => new FurnitureDeliveryItem
                {
                    itemName = item.itemName,
                    amount = item.amount,
                    pricePerUnit =
                        item.price
                })
                .ToList();

            var deliveryContract = new FurnitureDeliveryContract
            {
                fromAddress = DialogController.current.contact.Address,
                toAddress = deliveryContractSettings.selectedAddress,
                dayOfDelivery = deliveryContractSettings.selectedDeliverySlot.Item1,
                itemsToDeliver = itemsToDeliver,
                hourOfDelivery = deliveryContractSettings.selectedDeliverySlot.Item2,
                deliveryFee = FurnitureDeliveryContractSettings.deliveryFee
            };

            SaveGameManager.Current.FurnitureDeliveryContracts.Add(deliveryContract);

            var businessName = BuildingHelper
                .GetBuildingRegistration(deliveryContractSettings.selectedAddress).BusinessName;
            var deliveryItemsToText = itemsToDeliver.Aggregate("",
                (current, item) => current + $"{item.amount}x {item.itemName.GetLocalization()}<br>")[..^4];
            var messageData = new Dictionary<string, string>
            {
                { "amount", deliveryContractSettings.TotalItemsToDeliverAmount.ToString() },
                { "address", deliveryContractSettings.selectedAddress.ToFormattedString() },
                { "text", deliveryItemsToText },
                { "day", deliveryContract.dayOfDelivery.ToString() },
                { "hour", deliveryContract.hourOfDelivery.GetFormattedTime() },
                { "businessName", businessName }
            };
            DialogController.current.contact.ReceivePlayerMessage(new TextMessage(
                string.IsNullOrEmpty(businessName)
                    ? LegacyRef.MessageType.DialogFurnitureStoreOnContractSettingsSetPlayer
                    : LegacyRef.MessageType.DialogFurnitureStoreOnContractSettingsSetPlayerBusinessName, messageData,
                true));
            DialogController.current.contact.SendMessage(
                new TextMessage(LegacyRef.MessageType.DialogFurnitureStoreOnContractSettingsSetManager, null, true));
            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_furniture_on_contract_settings_set_manager".Localize(),
                InputTemplate = DialogEntry.InputTemplateName.None,
                OnVisible = () => AlreadyHasAPendingDelivery(true).ShowEntry()
            };
        }

        public DialogEntry OnCancelFurnitureDelivery(FurnitureDeliveryContract deliveryContract)
        {
            SaveGameManager.Current.FurnitureDeliveryContracts.Remove(deliveryContract);

            DialogController.current.contact.ReceivePlayerMessage(
                new TextMessage(LegacyRef.MessageType.DialogFurnitureStoreCancelDelivery, null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage(LegacyRef.MessageType.DialogFurnitureStoreDeliveryCancelled, null, true));
            return new DialogEntry
            {
                messageData = "dialog_furniture_store_delivery_cancelled".Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnVisible =
                    DialogController.current.dialogType == DialogType.PhoneCall
                        ? DialogController.current.FinishDialog
                        : null,
                OnCancel = DialogController.current.dialogType == DialogType.Physical
                    ? DialogController.current.FinishDialog
                    : null
            };
        }

        #endregion

        #region Vehicles

        private DialogEntry StartVehicles()
        {
            
            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_vehicles_start".Localize(),
                Template = DialogEntry.TemplateType.Text,
                headerKey = npcNameKey,
                OnVisible = () => DialogController.current.ShowEntry(FurnitureDeliveryContract())
            };
        }

        #endregion
    }
}