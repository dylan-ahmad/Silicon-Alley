using System.Collections.Generic;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
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
        private VehicleContractSettings _vehicleContractSettings;
        private string _lastPurchasedVehicleId;
        private bool _shouldMovePurchasedVehicleToConfiguredSpot;
        private readonly BackAlleyDealerVehicleService _vehicleService = new();
        
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
            if (BackAlleyDealerInit.Instance == null || !BackAlleyDealerInit.Instance.HasRegisteredItems)
                return NoRegisteredItemsInStock();

            var contactAddress = DialogController.current.contact.Address ?? BackAlleyDealerCity.DealerAddress;
            var hasAtLeastOneAddress = false;
            foreach (var x in SaveGameManager.Current.BuildingRegistrations)
            {
                if (!x.RentedByPlayer)
                    continue;
                
                hasAtLeastOneAddress = true;
                break;
            }

            var hasAlreadyADelivery = false;
            foreach (var x in SaveGameManager.Current.FurnitureDeliveryContracts)
            {
                if (x.fromAddress != contactAddress)
                    continue;
                
                hasAlreadyADelivery = true;
                break;
            }

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

        private static DialogEntry NoRegisteredItemsInStock()
        {
            const string noStockMessage = "backalleydealer:dialog_furniture_no_stock";
            DialogController.current.contact.SendMessage(
                new TextMessage(noStockMessage, null, true, true));
            return new DialogEntry
            {
                messageData = noStockMessage.Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = DialogController.current.dialogType == DialogType.PhoneCall
                    ? DialogController.current.FinishDialog
                    : null,
                OnCancel = DialogController.current.dialogType == DialogType.Physical
                    ? DialogController.current.FinishDialog
                    : null
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
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        private static DialogEntry NoAddresses()
        {
            DialogController.current.contact.SendMessage(
                new TextMessage("backalleydealer:dialog_furniture_no_addresses", null, true, true));
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
                fromAddress = DialogController.current.contact.Address ?? BackAlleyDealerCity.DealerAddress,
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
                new TextMessage("backalleydealer:dialog_furniture_on_contract_settings_set_manager", null, true));
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
                new TextMessage("backalleydealer:dialog_furniture_cancel_delivery", null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage("backalleydealer:dialog_furniture_store_delivery_cancelled", null, true));
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
            if (BackAlleyDealerInit.Instance == null || !BackAlleyDealerInit.Instance.HasRegisteredVehicles)
                return NoRegisteredVehiclesInStock();

            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_vehicles_start".Localize(),
                Template = DialogEntry.TemplateType.Text,
                headerKey = npcNameKey,
                OnVisible = () => DialogController.current.ShowEntry(VehicleContractSettings())
            };
        }

        private static DialogEntry NoRegisteredVehiclesInStock()
        {
            const string noStockMessage = "backalleydealer:dialog_vehicles_no_stock";
            DialogController.current.contact.SendMessage(
                new TextMessage(noStockMessage, null, true, true));
            return new DialogEntry
            {
                messageData = noStockMessage.Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = DialogController.current.dialogType == DialogType.PhoneCall
                    ? DialogController.current.FinishDialog
                    : null,
                OnCancel = DialogController.current.dialogType == DialogType.Physical
                    ? DialogController.current.FinishDialog
                    : null
            };
        }
        
        private DialogEntry VehicleContractSettings() =>
            new()
            {
                headerKey = "dialog_vehicle_store_contract_header",
                Template = DialogEntry.TemplateType.Input,
                InputTemplate = DialogEntry.InputTemplateName.VehicleContractSettings,
                OnVisible = DisableDeliveryForBackAlleyVehicleSettings,
                OnConfirm = OnVehicleSettingsSet,
                OnCancel = DialogController.current.CancelDialog,
                onCancelMessage = new TextMessage(LegacyRef.MessageType.ContactsMessagePlayerCancelCall),
            };

        private static void DisableDeliveryForBackAlleyVehicleSettings()
        {
            var settings = DialogController.current.GetInputTransform<VehicleContractSettings>(null);
            if (settings == null)
                return;

            settings.DisableDeliveryOptions();
        }

        private DialogEntry OnVehicleSettingsSet()
        {
            _vehicleContractSettings = DialogController.current.GetInputTransform<VehicleContractSettings>(null);
            if (_vehicleContractSettings != null && _vehicleContractSettings.selectedVehicleForSale != null)
                return !BackAlleyDealerVehicleService.TryPurchaseAtConfiguredSpot(
                    _vehicleContractSettings,
                    out _lastPurchasedVehicleId,
                    out _shouldMovePurchasedVehicleToConfiguredSpot)
                    ? null
                    : OnVehiclePurchased();
            Notifications.ShowError("common_notification_select_vehicle");
            return null;
        }

        private DialogEntry OnVehiclePurchased()
        {
            BackAlleyDealerVehicleService.FinalizePurchase(
                _vehicleContractSettings,
                _lastPurchasedVehicleId,
                _shouldMovePurchasedVehicleToConfiguredSpot);

            _lastPurchasedVehicleId = null;
            _shouldMovePurchasedVehicleToConfiguredSpot = false;
            var vehicleTypeName = _vehicleContractSettings.selectedVehicleForSale.vehicleName;
            var messageData =
                new Dictionary<string, string> { { "vehicleTypeName", vehicleTypeName.GetLocalization() } };
            DialogController.current.contact.ReceivePlayerMessage(new TextMessage(
                LegacyRef.MessageType.DialogVehicleStoreVehiclePurchasedPlayer, messageData, true));
            DialogController.current.contact.SendMessage(new TextMessage(
                LegacyRef.MessageType.DialogVehicleStoreVehiclePurchasedManager, read: true));
            return new DialogEntry
            {
                messageData = "backalleydealer:dialog_vehicle_purchased_manager".Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = DialogController.current.FinishDialog,
            };
        }

        #endregion
    }
}
