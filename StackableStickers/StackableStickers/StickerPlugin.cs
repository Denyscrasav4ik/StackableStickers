using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StackableStickers;

[BepInPlugin("denyscrasav4ik.thedumbfactory.stackablestickers", "Stackable Stickers", "1.0.0")]
public class StickerPlugin : BaseUnityPlugin
{
    private void Awake() => new Harmony("denyscrasav4ik.thedumbfactory.stackablestickers").PatchAll();
}

[HarmonyPatch(typeof(StickerManager))]
internal static class StickerManagerPatches
{
    public static readonly Dictionary<int, List<StickerStateData>> SlotStacks = new Dictionary<int, List<StickerStateData>>();

    private static readonly AccessTools.FieldRef<StickerManager, StickerManager.StickerAppliedDelegate> OnStickerAppliedRef =
        AccessTools.FieldRefAccess<StickerManager, StickerManager.StickerAppliedDelegate>("OnStickerApplied");

    private static List<StickerStateData> GetOrCreateStack(int slot)
    {
        if (!SlotStacks.TryGetValue(slot, out var stack))
        {
            stack = new List<StickerStateData>();
            SlotStacks[slot] = stack;
        }
        return stack;
    }

    [HarmonyPatch("ApplySticker")]
    [HarmonyPrefix]
    private static bool ApplySticker_Prefix(StickerManager __instance, StickerStateData sticker, int slot)
    {
        if (slot < 0 || slot >= __instance.activeStickerData.Length)
            return true;

        var stack = GetOrCreateStack(slot);

        if (sticker.sticker == Sticker.GlueStick)
        {
            __instance.UpgradeSlot(slot);
            OnStickerAppliedRef(__instance)?.Invoke();
            return false;
        }

        if (sticker.sticker == Sticker.Nothing)
        {
            if (stack.Count > 0)
                stack.RemoveAt(stack.Count - 1);

            if (stack.Count > 0)
                __instance.activeStickerData[slot] = stack[stack.Count - 1];
            else
                __instance.activeStickerData[slot] = new StickerStateData(Sticker.Nothing, 0, opened: true, sticky: false);
        }
        else
        {
            if (stack.Count == 0 && __instance.activeStickerData[slot] != null && __instance.activeStickerData[slot].sticker != Sticker.Nothing)
                stack.Add(__instance.activeStickerData[slot]);

            int currentLevel = Singleton<BaseGameManager>.Instance != null ? Singleton<BaseGameManager>.Instance.CurrentLevel : 0;
            var newSticker = new StickerStateData(sticker.sticker, currentLevel, opened: true, sticker.sticky);

            stack.Add(newSticker);
            __instance.activeStickerData[slot] = newSticker;
            __instance.appliedStickerRemainingNotebooks[slot] = __instance.notebooksDecayStickers ? 5 : 0;
        }

        OnStickerAppliedRef(__instance)?.Invoke();
        return false;
    }

    [HarmonyPatch("StickerValue")]
    [HarmonyPrefix]
    private static bool StickerValue_Prefix(StickerManager __instance, Sticker sticker, ref int __result)
    {
        int count = 0;
        for (int i = 0; i < __instance.activeStickerData.Length; i++)
        {
            if (SlotStacks.TryGetValue(i, out var stack) && stack.Count > 0)
            {
                foreach (var sData in stack)
                {
                    if (sData.sticker == sticker)
                        count++;
                }
            }
            else if (__instance.activeStickerData[i] != null && __instance.activeStickerData[i].sticker == sticker)
                count++;
        }

        __result = count;
        return false;
    }

    [HarmonyPatch("ClearAppliedStickers")]
    [HarmonyPrefix]
    private static bool ClearAppliedStickers_Prefix(StickerManager __instance)
    {
        for (int i = 0; i < __instance.activeStickerData.Length; i++)
        {
            if (!__instance.activeStickerData[i].sticky && !__instance.SlotUpgraded(i))
            {
                if (SlotStacks.TryGetValue(i, out var stack))
                    stack.Clear();
                __instance.activeStickerData[i] = new StickerStateData(Sticker.Nothing, 0, opened: true, sticky: false);
            }
        }

        OnStickerAppliedRef(__instance)?.Invoke();
        return false;
    }

    [HarmonyPatch("StickerCanBeCovered")]
    [HarmonyPrefix]
    private static bool StickerCanBeCovered_Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    [HarmonyPatch("GetLocalizedAppliedStickerDescription")]
    [HarmonyPrefix]
    private static bool GetLocalizedAppliedStickerDescription_Prefix(StickerManager __instance, int slot, ref string __result)
    {
        if (SlotStacks.TryGetValue(slot, out var stack) && stack.Count > 0)
        {
            List<string> descriptions = new List<string>();
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var sData = stack[i];
                if (sData.sticker == Sticker.Nothing)
                    continue;

                string title = Singleton<LocalizationManager>.Instance.GetLocalizedText($"StickerTitle_{sData.sticker}");
                string desc = Singleton<LocalizationManager>.Instance.GetLocalizedText($"StickerDescription_{sData.sticker}");
                descriptions.Add($"{title}\n{desc}");
            }

            if (descriptions.Count > 0)
            {
                __result = string.Join("\n\n\n", descriptions);
                return false;
            }
        }
        return true;
    }
}

[HarmonyPatch(typeof(StickerScreenController))]
internal static class StickerScreenControllerPatch
{
    private static readonly AccessTools.FieldRef<StickerScreenController, Image[]> AppliedStickerImageRef =
        AccessTools.FieldRefAccess<StickerScreenController, Image[]>("appliedStickerImage");

    private static readonly AccessTools.FieldRef<StickerManager, StickerData[]> StickerDataRef =
        AccessTools.FieldRefAccess<StickerManager, StickerData[]>("stickerData");

    [HarmonyPatch("InitializeStickers")]
    [HarmonyPostfix]
    private static void InitializeStickers_Postfix(StickerScreenController __instance)
    {
        var appliedStickerImages = AppliedStickerImageRef(__instance);
        if (appliedStickerImages == null) return;

        var stickerManager = Singleton<StickerManager>.Instance;
        if (stickerManager == null) return;

        var stickerDataArray = StickerDataRef(stickerManager);

        for (int slot = 0; slot < appliedStickerImages.Length; slot++)
        {
            if (appliedStickerImages[slot] == null) continue;

            appliedStickerImages[slot].color = Color.white;

            List<GameObject> oldLayers = new List<GameObject>();
            foreach (Transform child in appliedStickerImages[slot].transform)
            {
                if (child.name == "StackStickerLayer")
                    oldLayers.Add(child.gameObject);
            }
            foreach (var go in oldLayers)
                Object.Destroy(go);

            if (StickerManagerPatches.SlotStacks.TryGetValue(slot, out var stack) && stack.Count > 0)
            {
                appliedStickerImages[slot].color = new Color(1f, 1f, 1f, 0f);

                float offsetPx = 6f;

                for (int i = 0; i < stack.Count; i++)
                {
                    var stickerState = stack[i];
                    if (stickerState.sticker == Sticker.Nothing) continue;

                    GameObject layerGo = new GameObject("StackStickerLayer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    layerGo.transform.SetParent(appliedStickerImages[slot].transform, false);

                    RectTransform rect = layerGo.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.one;
                    rect.anchoredPosition = new Vector2(i * offsetPx, -i * offsetPx);

                    Image img = layerGo.GetComponent<Image>();
                    if (stickerDataArray != null && (int)stickerState.sticker < stickerDataArray.Length)
                        img.sprite = stickerDataArray[(int)stickerState.sticker].sprite;
                    img.raycastTarget = false;
                }
            }
        }
    }
}
