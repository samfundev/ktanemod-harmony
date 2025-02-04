using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using System.Linq;
using Assets.Scripts.Mods;
using Assets.Scripts.Services;
using Assets.Scripts.Settings;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace HarmonyModAssembly
{
    public static class Patcher
    {
        public static string ModInfoFile = "modInfo.json";
        public static Texture HarmonyTexture;
        public static string SettingsPath;

        public static bool ToggleModInfo()
        {
            if (ModInfoFile == "modInfo.json")
            {
                ModInfoFile = "modInfo_Harmony.json";
                return false;
            }
            ModInfoFile = "modInfo.json";
            return true;
        }

        public static void Patch(Texture _HarmonyTexture)
        {
            HarmonyTexture = _HarmonyTexture;
            SettingsPath = Path.Combine(Path.Combine(Application.persistentDataPath, "Modsettings"), "Harmony.json");
            new Harmony("qkrisi.harmonymod").PatchAll();
        }
    }

    [HarmonyPatch(typeof(ModManager), "GetModInfoFromPath")]
    [HarmonyPriority(Priority.First)]
    public static class ModInfoPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr && (string) instruction.operand == "modInfo.json")
                    yield return new CodeInstruction(OpCodes.Ldsfld,
                        typeof(Patcher).GetField("ModInfoFile", AccessTools.all));
                else yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(ManageModsScreen), "OnEnable")]
    [HarmonyPriority(Priority.First)]
    public static class WorkshopPatch
    {
        public static bool Prefix(ManageModsScreen __instance, ref List<ModInfo> ___installedMods, ref List<ModInfo> ___fullListOfMods, ref List<ModInfo> ___allSubscribedMods)
        {
            ModManager.Instance.ReloadModMetaData();
            ___installedMods = ModManager.Instance.InstalledModInfos.Values
                .Where(info => File.Exists(Path.Combine(info.FilePath, Patcher.ModInfoFile))).ToList();
            ___allSubscribedMods = AbstractServices.Instance.GetSubscribedMods();
            ___fullListOfMods = Patcher.ModInfoFile == "modInfo.json"
                ? ___installedMods.Union(___allSubscribedMods).ToList()
                : ___installedMods;
            ___fullListOfMods.Sort((ModInfo a, ModInfo b) => a.Title.CompareTo(b.Title));
            Traverse.Create(__instance).Method("ShowMods").GetValue();
            return false;
        }
    }

    [HarmonyPatch(typeof(ModManagerState), "ReturnToSetupState")]
    [HarmonyPriority(Priority.First)]
    public static class SetupPatch
    {
        public static bool Prefix()
        {
            bool cont = Patcher.ToggleModInfo();
            ManualButtonPatch.AutoCloseManager = false;
            if (cont)
                return true;

            // Make the game think this is a first load so that the UseModAlways setting can apply again.
            typeof(ModManagerState).GetField("isFirstLoad", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            // "Re-enter" the Mod Manager
            SceneManager.Instance.ModManagerState.EnterTransitionComplete();
            return false;
        }
    }

    [HarmonyPatch(typeof(ModManagerManualInstructionScreen), "HandleOpenManualFolder")]
    [HarmonyPriority(Priority.First)]
    public static class ManualButtonPatch
    {
        public static bool AutoCloseManager;
        
        public static bool Prefix()
        {
            bool value = SetupPatch.Prefix();
            if(value)
                return true;
            AutoCloseManager = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ModManagerState), "ShouldShowManualInstructions")]
    [HarmonyPriority(Priority.First)]
    public static class InstructionPatch
    {
        public static bool Prefix(out bool __result)
        {
            __result = !PlayerSettingsManager.Instance.PlayerSettings.UseModsAlways;
            return false;
        }
    }

    [HarmonyPatch(typeof(ModManagerHoldable), "GoToModManagerState")]
    [HarmonyPriority(Priority.First)]
    public static class ModManagerPatch
    {
        public static void Prefix()
        {
            ChangeButtonText.AllowAutoContinue = false;
        }
    }

    [HarmonyPatch(typeof(MenuScreen), "EnterScreenComplete")]
    [HarmonyPriority(Priority.First)]
    public static class ChangeButtonText
    {
        public static bool AllowAutoContinue = true;
        
        static HarmonySettings GetSettings()
        {
            try
            {
                return JsonConvert.DeserializeObject<HarmonySettings>(File.ReadAllText(Patcher.SettingsPath));
            }
            catch (IOException)
            {
                try
                {
                    File.WriteAllText(Patcher.SettingsPath, JsonConvert.SerializeObject(new HarmonySettings()));
                }
                catch {}
            }
            catch {}
            return new HarmonySettings();
        }
        
        public static void Postfix(MenuScreen __instance)
        {
            if (Patcher.ModInfoFile == "modInfo.json")
            {
                if (__instance is ModManagerManualInstructionScreen screen)
                {
                    screen.ContinueButton.GetComponentInChildren<TextMeshProUGUI>(true)
                        .text = "Manage Harmony mods";
                    screen.OpenManualFolderButton.GetComponentInChildren<TextMeshProUGUI>(true).text =
                        "Skip Harmony manager";
                    MonoBehaviour.Destroy(screen.GetComponentInChildren<RawImage>());
                    var texts = screen.GetComponentsInChildren<TextMeshProUGUI>(true);
                    texts[1].text =
                        "Click this button if you'd like to skip the Harmony mod manager and load the Harmony mods that according to the previous configuration!";
                    texts[5].text =
                        "Or click this button if you'd like to select which Harmony mods should be enabled or disabled!";
                    texts[5].transform.localPosition = new Vector3(texts[5].transform.localPosition.x + 190,
                        texts[5].transform.localPosition.y - 90, texts[5].transform.localPosition.z);
                    if (GetSettings().AutoSkipFinalizeScreen)
                        screen.OpenManualFolderButton.OnInteract();
                }
            }
            else if (ManualButtonPatch.AutoCloseManager && __instance is ModManagerManualInstructionScreen ManualScreen)
                ManualScreen.ContinueButton.OnInteract();
            else if (__instance is ModManagerMainMenuScreen MenuScreen)
            {
                MenuScreen.SteamWorkshopBrowserButton.gameObject.SetActive(false);
                MenuScreen.GetComponentInChildren<TextMeshProUGUI>().text = "Harmony Mod Manager";
                var image = MenuScreen.GetComponentInChildren<RawImage>();
                image.texture = Patcher.HarmonyTexture;
                image.transform.localScale = new Vector3(image.transform.localScale.x + 1,
                    image.transform.localScale.y, image.transform.localScale.z);
                MenuScreen.ManageModsButton.GetComponentInChildren<TextMeshProUGUI>(true).text =
                    "Manage Harmony mods";
                if (ManualButtonPatch.AutoCloseManager)
                    MenuScreen.ReturnToGameButton.OnInteract();
            }
            else if(__instance is ManageModsScreen ManagerScreen)
                ManagerScreen.GetComponentInChildren<TextMeshProUGUI>().text = "Manage installed Harmony mods";
        }
    }
}