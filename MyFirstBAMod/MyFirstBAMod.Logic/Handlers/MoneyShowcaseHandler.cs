using BAModAPI.BA_Packages.Mods;
using Extensions;
using Localizor.LanguageChangeEvent;
using UnityEngine;

namespace MyFirstBAMod.Logic;

public class MoneyShowcaseHandler
{
    private const int MoneyAmount = 10000;

    public static void Start()
    {
        UnityLifecycleProvider.OnUpdate += OnUpdate;
    }

    public static void Stop()
    {
        UnityLifecycleProvider.OnUpdate -= OnUpdate;
    }

    private static void OnUpdate()
    {
        if (!Input.GetKeyDown(KeyCode.U))
            return;

        HudConfirm.Show(
            bodyData: new LanguageChangeEventDataHolder
            {
                Key = "receive_money_are_you_sure",
                Arguments = new { moneyAmount = MoneyAmount.ToShortCurrencyFormat(true) }
            },
            onConfirmAction: GiveMoney);
    }

    private static void GiveMoney()
    {
        GameManager.Command_ChangeMoney(MoneyAmount);
    }
}