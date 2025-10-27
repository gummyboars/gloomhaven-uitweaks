using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;

using UnityEngine;
using UnityEngine.UI;

using MapRuleLibrary.Client;
using MapRuleLibrary.Party;
using MapRuleLibrary.YML.Achievements;
using MapRuleLibrary.YML.Quest;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;


namespace UITweaks;

[HarmonyPatch(typeof(UIAchievementInventory), "OnClaimReward")]
public static class AchievementClaimPatch
{
    private static void Postfix(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType, List<UIAchievementFilter> ___filters, UIAchievementSlot slot)
    {
        foreach (Transform childT in InventoryFilter.AllFilterButtons())
        {
            AchievementHoverPatch.HideNewTooltipIfNoLongerNew(childT.gameObject);
        }
        AchievementHoverPatch.ShowNewNotificationsForUnclaimedAchievements(___slotsType, ___filters, slot);
    }
}

[HarmonyPatch(typeof(UIAchievementInventory), "OnHovered")]
public static class AchievementHoverPatch
{
    private static void Prefix(UIAchievementSlot slot, bool hovered, out bool __state)
    {
        __state = slot.Achievement.IsNew;
    }

    private static void Postfix(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType, List<UIAchievementFilter> ___filters, UIAchievementSlot slot, bool hovered, bool __state)
    {
        if (!hovered)
        {
            return;
        }
        if (__state && !slot.Achievement.IsNew)
        {
            foreach (Transform childT in InventoryFilter.AllFilterButtons())
            {
                HideNewTooltipIfNoLongerNew(childT.gameObject);
            }
        }
        ShowNewNotificationsForUnclaimedAchievements(___slotsType, ___filters, slot);
    }

    // Also shows the new notification when there is an unclaimed achievement. This makes the
    // behavior consistent with the guildmaster mode button at the bottom of the screen.
    public static void ShowNewNotificationsForUnclaimedAchievements(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType, List<UIAchievementFilter> ___filters, UIAchievementSlot slot)
    {
        UIAchievementFilter thisFilter = ___filters.First((UIAchievementFilter it) => it.filter == slot.Type);
        if (___slotsType[slot.Type].Exists((UIAchievementSlot achSlot) => achSlot.Achievement.Achievement.AchievementType != EAchievementType.Trophy && achSlot.Achievement.State == EAchievementState.Completed))
        {
            thisFilter.ShowNewNotification(true);
        }
    }

    // Update the new notifications on the filter buttons whenever an achievement is hovered or claimed.
    public static void HideNewTooltipIfNoLongerNew(GameObject button)
    {
        if (!InventoryFilter.SlotsByCharacter.ContainsKey(button.name))
        {
            return;
        }
        foreach (UIAchievementSlot achSlot in InventoryFilter.SlotsByCharacter[button.name])
        {
            if (achSlot.Achievement.IsNew)
            {
                return;
            }
            if (achSlot.Achievement.Achievement != null)
            {
                if (achSlot.Achievement.Achievement.AchievementType != EAchievementType.Trophy && achSlot.Achievement.State == EAchievementState.Completed)
                {
                    return;
                }
            }
        }
        // There are no "new" achievements for this character. Disable the new notification.
        // However, if the character is still locked, set the image to a lock and keep it enabled.
        if (InventoryFilter.LockedCharacters.Contains(button.name) && InventoryFilter.LockSprite != null)
        {
            button.transform.Find("New Notification").gameObject.GetComponent<Image>().sprite = InventoryFilter.LockSprite;
            button.transform.Find("New Notification").gameObject.GetComponent<Image>().material = UIInfoTools.Instance.disabledGrayscaleMaterial;
            button.transform.Find("New Notification").gameObject.SetActive(true);
        }
        else
        {
            button.transform.Find("New Notification").gameObject.SetActive(false);
        }
    }
}

[HarmonyPatch]
public static class InventoryFilter
{
    public static GameObject ButtonsHolderRight = null;
    public static GameObject ButtonsHolderLeft = null;
    public static Sprite LockSprite = null;
    public static string CurrentFilterCharID = "";
    public static Dictionary<string, List<UIAchievementSlot>> SlotsByCharacter = new Dictionary<string, List<UIAchievementSlot>>();
    public static SortedSet<string> LockedCharacters = new SortedSet<string>();
    public static Dictionary<string, int> CharacterSortOrder = new Dictionary<string, int>();

    public static IEnumerable<Transform> AllFilterButtons()
    {
        return ButtonsHolderRight.transform.Cast<Transform>().Concat(ButtonsHolderLeft.transform.Cast<Transform>());
    }

    [HarmonyPatch(typeof(UITrainerWindow), "Show"), HarmonyPostfix]
    private static void Postfix(UITrainerWindow __instance)
    {
        CreateButtons(__instance);
    }

    [HarmonyPatch(typeof(UIAchievementInventory), "Show"), HarmonyPrefix]
    private static void InventoryShowPrefix()
    {
        SlotsByCharacter.Clear();
        LockedCharacters.Clear();
    }

    // FilterBy is called (amongst other places) at the end of UIAchievementInventory.Show().
    // We clear out SlotsByCharacter before Show() is called, so if FilterBy is called and the count is 0,
    // we know we're inside Show and should recreate it.
    // This also guarantees that the Postfix for FilterBy references the correct slots.
    [HarmonyPatch(typeof(UIAchievementInventory), "FilterBy"), HarmonyPrefix]
    private static void Prefix(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType, List<UIAchievementFilter> ___filters)
    {
        if (SlotsByCharacter.Count == 0)
        {
            RefreshSlotsInfo(___slotsType);
            ShowNewNotificationsForUnclaimedAchievements(___slotsType, ___filters);
        }
    }

    // When showing the "mercenaries" achievements, also show buttons and further filter the achievements.
    [HarmonyPatch(typeof(UIAchievementInventory), "FilterBy"), HarmonyPostfix]
    private static void Postfix(EAchievementType newFilter, Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType)
    {
        if (newFilter != EAchievementType.Mercenaries)
        {
            if (ButtonsHolderRight != null)
            {
                ButtonsHolderRight.SetActive(false);
                ButtonsHolderLeft.SetActive(false);
            }
            return;
        }
        if (ButtonsHolderRight != null)
        {
            ButtonsHolderRight.SetActive(true);
            ButtonsHolderLeft.SetActive(true);
        }
        List<UIAchievementSlot> mercList = ___slotsType[EAchievementType.Mercenaries];
        if (CurrentFilterCharID == "")
        {
            foreach (UIAchievementSlot slot in mercList)
            {
                slot.SetVisibility(true);
            }
        }
        else
        {
            foreach (UIAchievementSlot slot in mercList)
            {
                slot.SetVisibility(false);
            }
            if (SlotsByCharacter.ContainsKey(CurrentFilterCharID) && SlotsByCharacter[CurrentFilterCharID] != null)
            {
                foreach (UIAchievementSlot slot in SlotsByCharacter[CurrentFilterCharID])
                {
                    slot.SetVisibility(true);
                }
            }
        }
    }

    // Updates SlotsByCharacter and LockedCharacters.
    public static void RefreshSlotsInfo(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType)
    {
        List<UIAchievementSlot> mercList = ___slotsType[EAchievementType.Mercenaries];
        foreach (UIAchievementSlot slot in mercList)
        {
            FieldInfo _partyAchievement = AccessTools.Field(typeof(UIAchievementSlot), "_partyAchievement");
            CPartyAchievement ach = (CPartyAchievement) _partyAchievement.GetValue(slot);
            UpdateLockedCharacters(ach);
            var characterIDs = GetCharacterIDs(ach);
            foreach (string charID in characterIDs)
            {
                if (!SlotsByCharacter.ContainsKey(charID))
                {
                    SlotsByCharacter[charID] = new List<UIAchievementSlot>();
                }
                SlotsByCharacter[charID].Add(slot);
            }
        }
    }

    // Shows the new notification icon on the type filters if there are any unclaimed achievements.
    // This makes it consistent with the guildmaster mode button at the bottom of the screen.
    public static void ShowNewNotificationsForUnclaimedAchievements(Dictionary<EAchievementType, List<UIAchievementSlot>> ___slotsType, List<UIAchievementFilter> ___filters)
    {
        foreach (EAchievementType eType in ___slotsType.Keys)
        {
            UIAchievementFilter thisFilter = ___filters.First((UIAchievementFilter it) => it.filter == eType);
            if (thisFilter == null)
            {
                UITweaksPlugin.logger.LogWarning($"filter for {eType} is null");
                continue;
            }
            if (___slotsType[eType].Exists((UIAchievementSlot achSlot) => achSlot.Achievement.Achievement.AchievementType != EAchievementType.Trophy && achSlot.Achievement.State == EAchievementState.Completed))
            {
                thisFilter.ShowNewNotification(true);
            }
        }
    }

    private static SortedSet<string> GetCharacterIDs(CPartyAchievement ach)
    {
        SortedSet<string> characterIDs = new SortedSet<string>();
        foreach (RewardGroup rg in ach.Rewards)
        {
            characterIDs.UnionWith(rg.CharacterIDs);
            foreach (Reward reward in rg.Rewards)
            {
                if (reward.GiveToCharacterID.IsNOTNullOrEmpty() && reward.GiveToCharacterID != "party" && reward.GiveToCharacterID != "NoneID")
                {
                    characterIDs.Add(reward.GiveToCharacterID);
                }
                else if (reward.Item != null && !reward.Item.YMLData.ValidEquipCharacterClassIDs.IsNullOrEmpty())
                {
                    characterIDs.Add(reward.Item.YMLData.ValidEquipCharacterClassIDs.FirstOrDefault());
                }
                else if (reward.Type == ETreasureType.UnlockQuest)
                {
                    var quest = MapRuleLibraryClient.MRLYML.Quests.FirstOrDefault((CQuest s) => s.ID == reward.UnlockName);
                    if (quest?.QuestCharacterRequirements != null && quest.QuestCharacterRequirements.Count > 0)
                    {
                        characterIDs.Add(quest.QuestCharacterRequirements[0].RequiredCharacterID);
                    }
                }
            }
        }
        // Horrible hack. When claimed, the rewards completely disappear from the reward group, and there
        // is no other way of knowing that this achievement was for this character.
        if (ach.ID.LastIndexOf('_') >= 0 && ach.ID.EndsWith("ReachLevel2"))
        {
            int start = ach.ID.LastIndexOf('_') + 1;
            string extractedCharID = ach.ID.Substring(start, ach.ID.Length - start - "ReachLevel2".Length);
            characterIDs.Add(extractedCharID + "ID");
        }
        return characterIDs;
    }

    private static void UpdateLockedCharacters(CPartyAchievement ach)
    {
        if (ach.State == EAchievementState.RewardsClaimed || ach.State == EAchievementState.Completed)
        {
            return;
        }
        foreach (RewardGroup rg in ach.Rewards)
        {
            foreach (Reward reward in rg.Rewards)
            {
                if (reward.Type == ETreasureType.UnlockCharacter && !string.IsNullOrEmpty(reward.CharacterID))
                {
                    LockedCharacters.Add(reward.CharacterID);
                }
            }
        }
    }

    private static void CreateButtons(UITrainerWindow instance)
    {
        if (ButtonsHolderRight != null)
        {
            UnityEngine.Object.Destroy(ButtonsHolderRight);
            UnityEngine.Object.Destroy(ButtonsHolderLeft);
            ButtonsHolderRight = null;
            ButtonsHolderLeft = null;
        }
        CurrentFilterCharID = "";

        // We actually clone the mode buttons at the bottom of the screen, then heavily modify the layout.
        UIGuildmasterHUD hud = Singleton<UIGuildmasterHUD>.Instance;
        FieldInfo trainerButton = AccessTools.Field(typeof(UIGuildmasterHUD), "trainerButton");
        UIGuildmasterButton trainer = (UIGuildmasterButton) trainerButton.GetValue(hud);
        GameObject buttons = trainer.transform.parent.gameObject;

        FieldInfo _inventory = AccessTools.Field(typeof(UITrainerWindow), "inventory");
        UIAchievementInventory inventory = (UIAchievementInventory) _inventory.GetValue(instance);

        // Reposition to be to the left of the achievements inventory.
        RectTransform invTransform = (RectTransform) inventory.transform;
        Vector3 setPosition = inventory.transform.localPosition;
        RectTransform trainerTransform = (RectTransform) trainer.transform;
        setPosition.x -= 2 * trainerTransform.rect.width;
        setPosition.x -= invTransform.rect.width;
        setPosition.y -= invTransform.rect.height / 2;

        ButtonsHolderRight = GameObject.Instantiate(buttons, inventory.transform.parent);
        ButtonsHolderRight.name = "Filter Buttons Right";
        RectTransform filterTransform = (RectTransform) ButtonsHolderRight.transform;
        filterTransform.localPosition = setPosition;
        // Resize to be the same height as the achievements inventory, and twice the width of a button.
        filterTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2 * trainerTransform.rect.width);
        filterTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, invTransform.rect.height);

        ContentSizeFitter fitter = ButtonsHolderRight.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Change the layout mode to vertical.
        HorizontalLayoutGroupExtended hlge = ButtonsHolderRight.GetComponent<HorizontalLayoutGroupExtended>();
        float spacing = hlge.spacing;
        UnityEngine.Object.DestroyImmediate(hlge);
        VerticalLayoutGroupExtended vlge = ButtonsHolderRight.AddComponent<VerticalLayoutGroupExtended>();
        vlge.childAlignment = TextAnchor.LowerCenter;
        vlge.spacing = spacing;
        vlge.childControlHeight = true;
        vlge.childControlWidth = false;
        vlge.childForceExpandHeight = false;
        vlge.childForceExpandWidth = false;
        vlge.childScaleHeight = false;
        vlge.childScaleWidth = false;
        vlge.SetLayoutVertical();

        // Because this is a clone, we actually have to destroy every game object here except for the
        // template that will be used to instantiate new buttons. We will destroy the template at the end.
        List<GameObject> toDestroy = new List<GameObject>();
        GameObject template = null;
        foreach (Transform childT in ButtonsHolderRight.transform)
        {
            if (template != null)
            {
                toDestroy.Add(childT.gameObject);
                continue;
            }
            if (childT.gameObject.GetComponent<UIGuildmasterButton>() == null)
            {
                toDestroy.Add(childT.gameObject);
                continue;
            }
            template = childT.gameObject;
        }
        foreach (GameObject obj in toDestroy)
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }

        // Create a second copy because we'll need two columns of buttons.
        ButtonsHolderLeft = GameObject.Instantiate(ButtonsHolderRight, ButtonsHolderRight.transform.parent);
        ButtonsHolderLeft.name = "Filter Buttons Left";
        setPosition.x -= trainerTransform.rect.width;
        ButtonsHolderLeft.transform.localPosition = setPosition;
        GameObject templateClone = ButtonsHolderLeft.transform.GetChild(0).gameObject;

        // Grab the sprite with the lock image. We'll use it when creating class buttons.
        if (LockSprite == null)
        {
            FieldInfo _questMarker = AccessTools.Field(typeof(MapMarkersManager), "questMaker");  // [sic]
            UIQuestMapMarker questMarker = (UIQuestMapMarker) _questMarker.GetValue(Singleton<MapMarkersManager>.Instance);
            if (questMarker != null)
            {
                Transform lockTrns = questMarker.gameObject.transform.GetChild(0).Find("Lock");
                if (lockTrns != null)
                {
                    LockSprite = lockTrns.gameObject.GetComponent<Image>().sprite;
                }
            }
        }

        int count = 0;
        foreach (string charID in SlotsByCharacter.Keys.Append("EmptySlotID").OrderBy(n => GetCharacterSortOrder(n)))
        {
            CreateButtonForCharacter(charID, template, count % 2 == 0 ? ButtonsHolderLeft.transform : ButtonsHolderRight.transform, inventory);
            count++;
        }
        UnityEngine.Object.Destroy(template);
        UnityEngine.Object.Destroy(templateClone);

        ButtonsHolderRight.GetComponent<ToggleGroup>().allowSwitchOff = true;
        ButtonsHolderLeft.GetComponent<ToggleGroup>().allowSwitchOff = true;
        ButtonsHolderRight.GetComponent<ToggleGroup>().SetAllTogglesOff();
        ButtonsHolderLeft.GetComponent<ToggleGroup>().SetAllTogglesOff();
        foreach (Transform childT in AllFilterButtons())
        {
            if (childT.gameObject.name != "EmptySlotID")
            {
                childT.gameObject.GetComponent<Image>().material = null;
            }
        }
        ButtonsHolderRight.SetActive(false);
        ButtonsHolderLeft.SetActive(false);
    }

    private static void CreateButtonForCharacter(string charID, GameObject template, Transform parent, UIAchievementInventory inventory)
    {
        GameObject charButton = GameObject.Instantiate(template, parent);
        charButton.name = charID;
        charButton.GetComponent<ExtendedToggle>().onValueChanged.RemoveAllListeners();
        charButton.GetComponent<ExtendedToggle>().onSelected.RemoveAllListeners();
        charButton.GetComponent<ExtendedToggle>().onDeselected.RemoveAllListeners();
        charButton.GetComponent<ExtendedToggle>().onValueChanged.AddListener((selected) => OnSelected(selected, charID, inventory));

        if (charID == "EmptySlotID")
        {
            charButton.GetComponent<Image>().material = UIInfoTools.Instance.disabledGrayscaleMaterial;
            Color fullyTransparent = charButton.GetComponent<Image>().color;
            fullyTransparent.a = 0;
            charButton.GetComponent<Image>().color = fullyTransparent;
            charButton.GetComponent<ExtendedToggle>().interactable = false;
            UnityEngine.Object.Destroy(charButton.GetComponent<UIMapFTUECompleteStepToggleListener>());
            UnityEngine.Object.Destroy(charButton.GetComponent<ExtendedToggle>());
            return;
        }
        string shortName = charID;
        if (charID.EndsWith("ID"))
        {
            shortName = charID.Substring(0, charID.Length-2);
        }
        Sprite sprt = UIInfoTools.Instance.GetCharacterConfigUIFromString(shortName).questConfig.marker;
        charButton.GetComponent<Image>().sprite = sprt;
        charButton.GetComponent<ExtendedToggle>().interactable = true;
        // TODO: if you unlock a character, the new notification goes away.
        // But then you get new achievements for that character, so it needs to come back.
        // TODO: maybe combine this with the function for on hover / on claim above.
        bool isNew = false;
        foreach (UIAchievementSlot slot in SlotsByCharacter[charID])
        {
            if (slot.Achievement.IsNew)
            {
                isNew = true;
                break;
            }
            if (slot.Achievement.Achievement.AchievementType != EAchievementType.Trophy && slot.Achievement.State == EAchievementState.Completed)
            {
                isNew = true;
                break;
            }
        }
        if (isNew)
        {
            charButton.transform.Find("New Notification").gameObject.SetActive(true);
        }
        else if (LockedCharacters.Contains(charID) && LockSprite != null)
        {
            charButton.transform.Find("New Notification").gameObject.GetComponent<Image>().sprite = LockSprite;
            charButton.transform.Find("New Notification").gameObject.GetComponent<Image>().material = UIInfoTools.Instance.disabledGrayscaleMaterial;
            charButton.transform.Find("New Notification").gameObject.SetActive(true);
        }
    }

    private static void OnSelected(bool selected, string charID, UIAchievementInventory inventory)
    {
        FieldInfo _currentFilter = AccessTools.Field(typeof(UIAchievementInventory), "currentFilter");
        EAchievementType currentFilter = (EAchievementType) _currentFilter.GetValue(inventory);
        if (currentFilter != EAchievementType.Mercenaries)
        {
            return;  // one may be auto-selected when the window is first loaded
        }
        if (selected && CurrentFilterCharID != charID)
        {
            CurrentFilterCharID = charID;
            foreach (Transform childT in AllFilterButtons())
            {
                if (childT.gameObject.name != charID)
                {
                    childT.gameObject.GetComponent<Image>().material = UIInfoTools.Instance.disabledGrayscaleMaterial;
                }
                else
                {
                    childT.gameObject.GetComponent<Image>().material = null;
                }
            }
        }
        else if (CurrentFilterCharID == charID)
        {
            CurrentFilterCharID = "";
            foreach (Transform childT in AllFilterButtons())
            {
                if (childT.gameObject.name != "EmptySlotID")
                {
                    childT.gameObject.GetComponent<Image>().material = null;
                }
            }
        }
        inventory.FilterBy(EAchievementType.Mercenaries);
    }

    public static int GetCharacterSortOrder(string charID)
    {
        if (CharacterSortOrder.ContainsKey(charID))
        {
            return CharacterSortOrder[charID];
        }
        if (charID == "EmptySlotID")
        {
            return 506;
        }
        AbilityCardYMLData abilityCardYMLData = ScenarioRuleClient.SRLYML.AbilityCards.FirstOrDefault((AbilityCardYMLData x) => x.CharacterID == charID);
        int order = abilityCardYMLData == null ? 1000000 : abilityCardYMLData.ID;
        CharacterSortOrder[charID] = order;
        return order;
    }
}
