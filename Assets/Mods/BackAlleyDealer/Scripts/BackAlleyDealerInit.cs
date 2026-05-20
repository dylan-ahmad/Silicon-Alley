using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackAlleyDealer;
using BAModAPI;
using BigAmbitions.Items;
using Services;

[assembly: RegisterModClass(typeof(BackAlleyDealerInit))]

namespace BackAlleyDealer
{
    [ModEntryOnInitializationLoad]
    public class BackAlleyDealerInit : IModBigAmbitions
    {
        private readonly HashSet<string> _registeredItemNames = new();
        private readonly HashSet<string> _registeredVehicleNames = new();
        private ModContext _context;
        public static BackAlleyDealerInit Instance { get; private set; }

        public bool HasRegisteredItems => _registeredItemNames.Count > 0;
        public bool HasRegisteredVehicles => _registeredVehicleNames.Count > 0;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();


        public Task OnLoadAsync(ModContext context)
        {
            _context = context;
            Instance = this;
            SetContactForAddress(BackAlleyDealerCity.DealerAddress, BackAlleyDealerCity.DealerName);
            if (HasRegisteredItems)
                SetContractItemsForContact(BackAlleyDealerCity.DealerName, _registeredItemNames);
            if (HasRegisteredVehicles)
                SetContractVehiclesForContact(BackAlleyDealerCity.DealerName, _registeredVehicleNames);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            RemoveContractItemsForContact(BackAlleyDealerCity.DealerName);
            RemoveContactForAddress(BackAlleyDealerCity.DealerAddress);
            Instance = null;
            _registeredItemNames.Clear();
            _registeredVehicleNames.Clear();
            return Task.CompletedTask;
        }

        public void RegisterItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return;

            if (ItemsGetter.GetByName(itemName) == null)
            {
                _context.Logger.Warn($"Tried to register invalid item '{itemName}' for BackAlleyDealer");
                return;
            }

            if (!_registeredItemNames.Add(itemName))
                _context.Logger.Warn($"Item '{itemName}' is already registered");

            SetContractItemsForContact(BackAlleyDealerCity.DealerName, _registeredItemNames);
        }

        public void UnregisterItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return;

            if (!_registeredItemNames.Remove(itemName))
                _context.Logger.Warn($"Item '{itemName}' is not registered");

            SetContractItemsForContact(BackAlleyDealerCity.DealerName, _registeredItemNames);
        }

        public void RegisterVehicle(string vehicleName)
        {
            if (string.IsNullOrEmpty(vehicleName))
                return;

            if (!_registeredVehicleNames.Add(vehicleName))
                _context.Logger.Warn($"Vehicle '{vehicleName}' is already registered");

            SetContractVehiclesForContact(BackAlleyDealerCity.DealerName, _registeredVehicleNames);
        }

        public void UnregisterVehicle(string vehicleName)
        {
            if (string.IsNullOrEmpty(vehicleName))
                return;

            if (!_registeredVehicleNames.Remove(vehicleName))
                _context.Logger.Warn($"Vehicle '{vehicleName}' is not registered");

            SetContractVehiclesForContact(BackAlleyDealerCity.DealerName, _registeredVehicleNames);
        }

        private static void SetContractItemsForContact(string contactId, HashSet<string> itemNames)
        {
            if (string.IsNullOrEmpty(contactId))
                return;
            
            ContractItemsForSaleService.SetItemsForContact(contactId, itemNames);
        }

        private static void SetContractVehiclesForContact(string contactId, HashSet<string> vehicleNames)
        {
            if (string.IsNullOrEmpty(contactId))
                return;

            ContractItemsForSaleService.SetVehiclesForContact(contactId, vehicleNames);
        }

        private static void RemoveContractItemsForContact(string contactId)
        {
            if (string.IsNullOrEmpty(contactId))
                return;
            
            ContractItemsForSaleService.RemoveContact(contactId);
        }
        
        private static void SetContactForAddress(Address address, string contactId)
        {
            if (address == null || string.IsNullOrEmpty(contactId))
                return;
            
            ContractItemsForSaleService.SetContactForAddress(address, contactId);
        }

        private static void RemoveContactForAddress(Address address)
        {
            if (address == null)
                return;

            ContractItemsForSaleService.RemoveContactForAddress(address);
        }
    }
}
