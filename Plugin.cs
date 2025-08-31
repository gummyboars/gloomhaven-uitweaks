using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;


namespace UITweaks;

[BepInPlugin(pluginGUID, pluginName, pluginVersion)]
public class UITweaksPlugin : BaseUnityPlugin
{
    const string pluginGUID = "com.gummyboars.gloomhaven.uitweaks";
    const string pluginName = "UI Tweaks";
    const string pluginVersion = "1.0.0";

    private Harmony HarmonyInstance = null;

    public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(pluginName);

    private void Awake()
    {
        UITweaksPlugin.logger.LogInfo($"Loading plugin {pluginName}.");
        try
        {
            HarmonyInstance = new Harmony(pluginGUID);
            Assembly assembly = Assembly.GetExecutingAssembly();
            HarmonyInstance.PatchAll(assembly);
            UITweaksPlugin.logger.LogInfo($"Plugin {pluginName} loaded.");
        }
        catch (Exception e)
        {
            UITweaksPlugin.logger.LogError($"Could not load plugin {pluginName}: {e}");
        }
    }
}

// Upon exiting any of the three windows (merchant, temple, enchantress), save the selected
// character. Upon entering any of the three (EnableSelectionMode), restore the selected character.
[HarmonyPatch]
public static class Patcher
{
    public static int savedIndex = -1;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIShopItemWindow), "OnHidden")]
    private static void PrefixShop()
    {
        savedIndex = NewPartyDisplayUI.PartyDisplay.SelectedSlotIndex;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UITempleWindow), "Exit")]
    private static void PrefixTemple()
    {
        savedIndex = NewPartyDisplayUI.PartyDisplay.SelectedSlotIndex;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UINewEnhancementWindow), "ExitShop")]
    private static void PrefixEnchantress()
    {
        savedIndex = NewPartyDisplayUI.PartyDisplay.SelectedSlotIndex;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), "EnableSelectionMode")]
    private static void Postfix(NewPartyDisplayUI __instance)
    {
        if (0 <= savedIndex && savedIndex < __instance.CharacterSlots.Count)
        {
            var selectedCharacter = __instance.CharacterSlots[savedIndex];
            MethodInfo OnCharacterSelect = typeof(NewPartyDisplayUI).GetMethod("OnCharacterSelect", BindingFlags.NonPublic | BindingFlags.Instance);
            OnCharacterSelect.Invoke(__instance, new object[] {true, selectedCharacter});
        }
    }
}
