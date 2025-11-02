using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;

using UnityEngine;
using UnityEngine.UI;

using ScenarioRuleLibrary;


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
// Also save the selected character on the map screen when the user selects items/perks/cards/etc.
[HarmonyPatch]
public static class WindowPatcher
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
    [HarmonyPatch(typeof(NewPartyDisplayUI), "OnCardsSelected", new Type[] {typeof(NewPartyCharacterUI), typeof(bool), typeof(bool), typeof(bool)})]
    private static void PostfixSelectCards(NewPartyDisplayUI __instance)
    {
        if (__instance.SelectedSlotIndex >= 0)
        {
            savedIndex = __instance.SelectedSlotIndex;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), "OnItemsSelected")]
    private static void PostfixSelectItems(NewPartyDisplayUI __instance)
    {
        if (__instance.SelectedSlotIndex >= 0)
        {
            savedIndex = __instance.SelectedSlotIndex;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), "OnPerksSelected")]
    private static void PostfixSelectPerks(NewPartyDisplayUI __instance)
    {
        if (__instance.SelectedSlotIndex >= 0)
        {
            savedIndex = __instance.SelectedSlotIndex;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), "OnCharacterPickerSelected")]
    private static void PostfixSelectPicker(NewPartyDisplayUI __instance)
    {
        if (__instance.SelectedSlotIndex >= 0)
        {
            savedIndex = __instance.SelectedSlotIndex;
        }
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

// When showing the circular card counters in the UI, have max 5 counters per line.
[HarmonyPatch(typeof(UIScenarioAttackModifier), "UpdateCounters", new Type[] {typeof(int)})]
public static class UpdateCountersPatch
{
    private static void Postfix(Transform ___countersHolder)
    {
        ___countersHolder.GetComponent<GridLayoutGroup>().constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        ___countersHolder.GetComponent<GridLayoutGroup>().constraintCount = 5;
        GridLayoutGroup g = ___countersHolder.GetComponent<GridLayoutGroup>();
        var spc = g.spacing;
        spc.x = g.cellSize.x / 2;
        ___countersHolder.GetComponent<GridLayoutGroup>().spacing = spc;
    }
}

[HarmonyPatch(typeof(UIAttackModifierCalculator), "SetupCounters")]
public static class SetupCountersPatch
{
    private static void Postfix(Transform ___container)
    {
        ___container.GetComponent<GridLayoutGroup>().constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        ___container.GetComponent<GridLayoutGroup>().constraintCount = 5;
        GridLayoutGroup g = ___container.GetComponent<GridLayoutGroup>();
        var spc = g.spacing;
        spc.x = g.cellSize.x / 2;
        ___container.GetComponent<GridLayoutGroup>().spacing = spc;
    }
}
