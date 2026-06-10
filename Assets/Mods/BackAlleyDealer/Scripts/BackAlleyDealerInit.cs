using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackAlleyDealer;
using BAModAPI;
using BigAmbitions.Items;
using Services;
using Vehicles.VehicleTypes;

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

            UpdateModdedItems();
            UpdateModdedVehicles();
            
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

        private void UpdateModdedItems()
        {
            _registeredItemNames.Clear();
            foreach (var item in ItemsGetter.AllItems)
            {
                if (item == null || string.IsNullOrEmpty(item.itemName))
                    continue;
                
                if (ItemsGetter.IsModItem(item.itemName))
                    _registeredItemNames.Add(item.itemName);
            }
            
            SetContractItemsForContact(BackAlleyDealerCity.DealerName, _registeredItemNames);
        }

        private void UpdateModdedVehicles()
        {
            _registeredVehicleNames.Clear();
            foreach (var vehicleName in VehicleTypeHelper.GetVehicleTypeNames())
            {
                if (string.IsNullOrEmpty(vehicleName))
                    continue;
                
                if (VehicleTypeHelper.IsModVehicleType(vehicleName))
                    _registeredVehicleNames.Add(vehicleName);
            }
            
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
