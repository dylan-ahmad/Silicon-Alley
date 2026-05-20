using System.Collections.Generic;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Extensions;
using Helpers;
using UI;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace BackAlleyDealer
{
    public sealed class BackAlleyDealerVehicleService
    {
        private const string ParkingGarageRootPath = "BuildingBlocks/BuildingBlock(5,1)/Parking01Exterior";

        public static bool TryPurchaseAtConfiguredSpot(VehicleContractSettings vehicleContractSettings,
            out string purchasedVehicleId,
            out bool shouldMovePurchasedVehicleToConfiguredSpot)
        {
            purchasedVehicleId = null;
            shouldMovePurchasedVehicleToConfiguredSpot = false;
            if (vehicleContractSettings == null || vehicleContractSettings.selectedVehicleForSale == null)
                return false;

            return vehicleContractSettings.selectedVehicle != null
                ? TryPurchaseShowcaseVehicle(vehicleContractSettings, out purchasedVehicleId,
                    out shouldMovePurchasedVehicleToConfiguredSpot)
                : TryPurchaseVehicleTypeBasedVehicle(vehicleContractSettings, out purchasedVehicleId);
        }

        public static void FinalizePurchase(VehicleContractSettings vehicleContractSettings, string purchasedVehicleId,
            bool shouldMovePurchasedVehicleToConfiguredSpot)
        {
            if (shouldMovePurchasedVehicleToConfiguredSpot && !string.IsNullOrEmpty(purchasedVehicleId))
                TryMovePurchasedVehicleToConfiguredSpot(purchasedVehicleId);

            UIs.Instance.playerHUD.purchaseVehicleUI.SetAsset(vehicleContractSettings.selectedVehicleForSale, false);
            UIs.Instance.playerHUD.purchaseVehicleUI.RunShowcaseAnimation();
        }

        private static bool TryPurchaseShowcaseVehicle(VehicleContractSettings vehicleContractSettings,
            out string purchasedVehicleId,
            out bool shouldMovePurchasedVehicleToConfiguredSpot)
        {
            purchasedVehicleId = null;
            shouldMovePurchasedVehicleToConfiguredSpot = false;
            var saveGame = SaveGameManager.Current;
            if (saveGame?.VehicleInstances == null)
                return false;

            var oldCount = saveGame.VehicleInstances.Count;
            if (!vehicleContractSettings.selectedVehicle.Purchase())
                return false;

            if (oldCount >= saveGame.VehicleInstances.Count)
                return false;

            var purchasedVehicleInstance = saveGame.VehicleInstances[oldCount];
            if (purchasedVehicleInstance == null)
                return false;

            shouldMovePurchasedVehicleToConfiguredSpot = true;
            purchasedVehicleId = purchasedVehicleInstance.id;
            return !string.IsNullOrEmpty(purchasedVehicleId);
        }

        private static bool TryPurchaseVehicleTypeBasedVehicle(VehicleContractSettings vehicleContractSettings,
            out string purchasedVehicleId)
        {
            purchasedVehicleId = null;
            var saveGame = SaveGameManager.Current;
            if (saveGame?.VehicleInstances == null)
                return false;

            var vehicleName = vehicleContractSettings.selectedVehicleForSale.vehicleName;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleName);
            if (vehicleType == null)
                return false;

            if (!TryGetPreferredVehicleSpawnPoint(out var spawnPosition, out var spawnRotation))
                return false;

            var transactionData = new Dictionary<string, string> { { "vehicleName", vehicleName } };
            if (vehicleType.taxDeductible)
                transactionData["taxDeductibleName"] = vehicleName;

            var transactionInfo =
                new TransactionInfo(LegacyRef.Transaction.VehicleBought, transactionData, vehicleType.taxDeductible);
            if (!GameManager.ChangeMoneySafe(
                    -vehicleContractSettings.selectedVehicleForSale.GetPurchasePrice(),
                    transactionInfo,
                    showNotification: true))
                return false;

            var initialFuel = vehicleType.maxFuel * Random.Range(0.97f, 0.98f);
            var vehicleInstance = new VehicleInstance(vehicleName)
            {
                id = UuidHelper.GenerateBase64Uuid(),
                vehicleColorName = vehicleContractSettings.selectedVehicleForSale.GetInitialColor(),
                fuel = initialFuel
            };
            VehicleHelper.CreateAndSpawnVehicle(vehicleInstance, spawnPosition, spawnRotation);
            purchasedVehicleId = vehicleInstance.id;
            return true;
        }

        private static bool TryGetPreferredVehicleSpawnPoint(out Vector3 spawnPosition,
            out Quaternion spawnRotation)
        {
            spawnPosition = default;
            spawnRotation = default;

            if (VehicleParkingHelper.TryGetRandomParkingGarageSpot(
                    ParkingGarageRootPath,
                    out var garageSpotPosition,
                    out var garageSpotRotation))
            {
                spawnPosition = garageSpotPosition;
                spawnRotation = garageSpotRotation;
                return true;
            }

            var cityBuildingController = BuildingManager.Instance?.cityBuildingController;
            var customPositions = cityBuildingController?.customPositions;
            if (customPositions is { Count: > 0 })
            {
                var spawnPoint = customPositions[0];
                spawnPosition = spawnPoint.position;
                spawnRotation = spawnPoint.rotation;
                return true;
            }

            var playerController = GameManager.Instance?.playerController;
            if (playerController == null)
                return false;

            spawnPosition = playerController.transform.position + playerController.transform.forward * 2;
            spawnRotation = playerController.transform.rotation;
            return true;
        }

        private static void TryMovePurchasedVehicleToConfiguredSpot(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null) return;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController == null || vehicleController.vehicleInstance == null)
                    continue;

                if (vehicleController.vehicleInstance.id != vehicleId)
                    continue;

                if (!VehicleParkingHelper.TryGetRandomParkingGarageSpot(
                        ParkingGarageRootPath,
                        out var lanePosition,
                        out var laneRotation))
                    return;

                VehicleHelper.TeleportVehicleToGround(vehicleController, lanePosition, laneRotation);
                return;
            }
        }
    }
}