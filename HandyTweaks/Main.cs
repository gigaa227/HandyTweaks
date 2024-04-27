﻿using BepInEx;
using ConfigTweaks;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Net;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using System.Security.Permissions;
using Newtonsoft.Json.Linq;
using BepInEx.Configuration;
using System.Runtime.Remoting.Channels;
using UnityEngine.EventSystems;

namespace HandyTweaks
{
    [BepInPlugin("com.aidanamite.HandyTweaks", "Handy Tweaks", "1.3.0")]
    [BepInDependency("com.aidanamite.ConfigTweaks")]
    public class Main : BaseUnityPlugin
    {
        [ConfigField]
        public static KeyCode DoFarmStuff = KeyCode.KeypadMinus;
        [ConfigField]
        public static bool AutoSpendFarmGems = false;
        [ConfigField]
        public static bool BypassFarmGemCosts = false;
        [ConfigField]
        public static bool DoFarmStuffOnTimer = false;
        [ConfigField]
        public static bool CanPlaceAnywhere = false;
        [ConfigField]
        public static bool SkipTrivia = false;
        [ConfigField]
        public static KeyCode DontApplyGeometry = KeyCode.LeftShift;
        [ConfigField]
        public static KeyCode DontApplyTextures = KeyCode.LeftAlt;
        [ConfigField]
        public static bool SortStableQuestDragonsByValue = false;
        [ConfigField]
        public static bool ShowRacingEquipmentStats = false;
        [ConfigField]
        public static KeyCode ChangeDragonsGender = KeyCode.Equals;
        [ConfigField]
        public static bool InfiniteZoom = false;
        [ConfigField]
        public static float ZoomSpeed = 1;
        [ConfigField]
        public static bool DisableDragonAutomaticSkinUnequip = true;
        [ConfigField]
        public static bool ApplyDragonPrimaryToEmission = false;
        [ConfigField]
        public static bool AllowCustomizingSpecialDragons = false;
        [ConfigField]
        public static int StableQuestChanceBoost = 0;
        [ConfigField]
        public static float StableQuestDragonValueMultiplier = 1;
        [ConfigField]
        public static float StableQuestTimeMultiplier = 1;
        [ConfigField]
        public static bool BiggerInputBoxes = true;
        [ConfigField]
        public static bool MoreNameFreedom = true;
        [ConfigField]
        public static bool AutomaticFireballs = true;
        [ConfigField]
        public static bool AlwaysMaxHappiness = false;
        [ConfigField]
        public static KeyCode ChangeDragonFireballColour = KeyCode.KeypadMultiply;
        [ConfigField]
        public static Dictionary<string, bool> DisableHappyParticles = new Dictionary<string, bool>();
        [ConfigField]
        public static bool AlwaysShowArmourWings = false;
        [ConfigField]
        public static bool DisableCustomColourPicker = false;
        [ConfigField]
        public static bool CheckForModUpdates = true;
        [ConfigField]
        public static int UpdateCheckTimeout = 60;
        [ConfigField]
        public static int MaxConcurrentUpdateChecks = 4;

        public static Main instance;
        static List<(BaseUnityPlugin, string)> updatesFound = new List<(BaseUnityPlugin, string)>();
        static ConcurrentDictionary<WebRequest,bool> running = new ConcurrentDictionary<WebRequest, bool>();
        static int currentActive;
        static bool seenLogin = false;
        static GameObject waitingUI;
        static RectTransform textContainer;
        static Text waitingText;
        float waitingTime;
        public void Awake()
        {
            instance = this;
            if (CheckForModUpdates)
            {
                waitingUI = new GameObject("Waiting UI", typeof(RectTransform));
                var c = waitingUI.AddComponent<Canvas>();
                DontDestroyOnLoad(waitingUI);
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                var s = c.gameObject.AddComponent<CanvasScaler>();
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                s.matchWidthOrHeight = 1;
                s.referenceResolution = new Vector2(Screen.width, Screen.height);
                var backing = new GameObject("back", typeof(RectTransform)).AddComponent<Image>();
                backing.transform.SetParent(c.transform, false);
                backing.color = Color.black;
                backing.gameObject.layer = LayerMask.NameToLayer("UI");
                waitingText = new GameObject("text", typeof(RectTransform)).AddComponent<Text>();
                waitingText.transform.SetParent(backing.transform, false);
                waitingText.text = "Checking for mod updates (??? remaining)";
                waitingText.font = Font.CreateDynamicFontFromOSFont("Consolas", 100);
                waitingText.fontSize = 25;
                waitingText.color = Color.white;
                waitingText.alignment = TextAnchor.MiddleCenter;
                waitingText.material = new Material(Shader.Find("Unlit/Text"));
                waitingText.gameObject.layer = LayerMask.NameToLayer("UI");
                waitingText.supportRichText = true;
                textContainer = backing.GetComponent<RectTransform>();
                textContainer.anchorMin = new Vector2(0, 1);
                textContainer.anchorMax = new Vector2(0, 1);
                textContainer.offsetMin = new Vector2(0, -waitingText.preferredHeight - 40);
                textContainer.offsetMax = new Vector2(waitingText.preferredWidth + 40, 0);
                var tT = waitingText.GetComponent<RectTransform>();
                tT.anchorMin = new Vector2(0, 0);
                tT.anchorMax = new Vector2(1, 1);
                tT.offsetMin = new Vector2(20, 20);
                tT.offsetMax = new Vector2(-20, -20);
                foreach (var plugin in Resources.FindObjectsOfTypeAll<BaseUnityPlugin>())
                    CheckModVersion(plugin);
            }
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HandyTweaks.colorpicker"))
            {
                var b = AssetBundle.LoadFromStream(s);
                ColorPicker.UIPrefab = b.LoadAsset<GameObject>("ColorPicker");
                b.Unload(false);
            }
            new Harmony("com.aidanamite.HandyTweaks").PatchAll();
            Logger.LogInfo("Loaded");
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        bool CanStartCheck()
        {
            if (currentActive < MaxConcurrentUpdateChecks)
            {
                currentActive++;
                return true;
            }
            return false;
        }
        void CheckStopped() => currentActive--;
        public async void CheckModVersion(BaseUnityPlugin plugin)
        {
            string url = null;
            bool isGit = true;
            var f = plugin.GetType().GetField("UpdateUrl", ~BindingFlags.Default);
            if (f != null)
            {
                var v = f.GetValue(plugin);
                if (v is string s)
                {
                    url = s;
                    isGit = false;
                }
            }
            f = plugin.GetType().GetField("GitKey", ~BindingFlags.Default);
            if (f != null)
            {
                var v = f.GetValue(plugin);
                if (v is string s)
                    url = "https://api.github.com/repos/" + s + "/releases/latest";
            }
            if (url == null)
            {
                var split = plugin.Info.Metadata.GUID.Split('.');
                if (split.Length >= 2)
                {
                    if (split[0] == "com" && split.Length >= 3)
                        url = $"https://api.github.com/repos/{split[1]}/{split[split.Length - 1]}/releases/latest";
                    else
                        url = $"https://api.github.com/repos/{split[0]}/{split[split.Length - 1]}/releases/latest";
                }
            }
            if (url == null)
            {
                Logger.LogInfo($"No update url found for {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})");
                return;
            }
            var request = WebRequest.CreateHttp(url);
            request.Timeout = UpdateCheckTimeout * 1000;
            request.UserAgent = "SoDMod-HandyTweaks-UpdateChecker";
            request.Accept = isGit ? "application/vnd.github+json" : "raw";
            request.Method = "GET";
            running[request] = true;
            try
            {
                while (!CanStartCheck())
                    await System.Threading.Tasks.Task.Delay(100);
                using (var req = request.GetResponseAsync())
                {
                    await req;
                    if (req.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    {
                        var res = req.Result;
                        var v = isGit ? res.GetJsonEntry("tag_name") : res.ReadContent();
                        if (string.IsNullOrEmpty(v))
                            Logger.LogInfo($"Update check failed for {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})\nURL: {url}\nReason: Responce was null");
                        if (Version.TryParse(v, out var newVersion))
                        {
                            if (plugin.Info.Metadata.Version == newVersion)
                                Logger.LogInfo($"{plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}) is up-to-date");
                            else if (plugin.Info.Metadata.Version > newVersion)
                                Logger.LogInfo($"{plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}) is newer than the latest release. Release is {newVersion}, current is {plugin.Info.Metadata.Version}");
                            else
                            {
                                Logger.LogInfo($"{plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}) has an update available. Latest is {newVersion}, current is {plugin.Info.Metadata.Version}");
                                updatesFound.Add((plugin, newVersion.ToString()));
                            }
                        }
                        else
                            Logger.LogInfo($"Update check failed for {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})\nURL: {url}\nReason: Responce could not be parsed {(v.Length > 100 ? $"\"{v.Remove(100)}...\" (FullLength={v.Length})" : $"\"{v}\"")}");
                    }
                    else
                        Logger.LogInfo($"Update check failed for {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})\nURL: {url}\nReason: No responce");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Update check failed for {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})\nURL: {url}\nReason: {e.GetType().FullName}: {e.Message}");
                if (!(e is WebException))
                    Logger.LogError(e);
            } finally
            {
                CheckStopped();
                running.TryRemove(request, out _);
            }
        }

        float timer;
        public void Update()
        {
            if (!seenLogin && UiLogin.pInstance)
                seenLogin = true;
            if (running != null && running.Count == 0 && seenLogin)
            {
                running = null;
                Destroy(waitingUI);
                if (updatesFound.Count == 1)
                    GameUtilities.DisplayOKMessage("PfKAUIGenericDB", $"Mod {updatesFound[0].Item1.Info.Metadata.Name} has an update available\nCurrent: {updatesFound[0].Item1.Info.Metadata.Version}\nLatest: {updatesFound[0].Item2}", null, "");
                else if (updatesFound.Count > 1)
                {
                    var s = new StringBuilder();
                    s.Append(updatesFound.Count);
                    s.Append(" mod updates available:");
                    for (int i = 0; i < updatesFound.Count; i++)
                    {
                        s.Append("\n");
                        if (i == 4)
                        {
                            s.Append("(");
                            s.Append(updatesFound.Count - 4);
                            s.Append(" more) ...");
                            break;
                        }
                        s.Append(updatesFound[i].Item1.Info.Metadata.Name);
                        s.Append(" ");
                        s.Append(updatesFound[i].Item1.Info.Metadata.Version);
                        s.Append(" > ");
                        s.Append(updatesFound[i].Item2);
                    }
                    GameUtilities.DisplayOKMessage("PfKAUIGenericDB", s.ToString(), null, "");
                }
            }
            if ((timer -= Time.deltaTime) <= 0 && (Input.GetKeyDown(DoFarmStuff) || DoFarmStuffOnTimer) && MyRoomsIntMain.pInstance is FarmManager f)
            {
                timer = 0.2f;
                foreach (var i in Resources.FindObjectsOfTypeAll<FarmItem>())
                    if (i && i.gameObject.activeInHierarchy && i.pCurrentStage != null && !i.IsWaitingForWsCall())
                    {
                        if (i is CropFarmItem c)
                        {
                            if (c.pCurrentStage._Name == "NoInteraction")
                            {
                                if (BypassFarmGemCosts)
                                    c.GotoNextStage();
                                else if (AutoSpendFarmGems && c.CheckGemsAvailable(c.GetSpeedupCost()))
                                    c.GotoNextStage(true);
                            }
                            else
                                c.GotoNextStage();
                        }
                        else if (i is FarmSlot s)
                        {
                            if (!s.IsCropPlaced())
                            {
                                var items = CommonInventoryData.pInstance.GetItems(s._SeedsCategory);
                                if (items != null)
                                    foreach (var seed in items)
                                        if (seed != null && seed.Quantity > 0)
                                            s.OnContextAction(seed.Item.ItemName);
                                break;
                            }
                        }
                        else if (i is AnimalFarmItem a)
                        {
                            if (a.pCurrentStage._Name.Contains("Feed"))
                            {
                                a.ConsumeFeed();
                                if (a.IsCurrentStageFeedConsumed())
                                    a.GotoNextStage(false);
                            }
                            else if (a.pCurrentStage._Name.Contains("Harvest"))
                                a.GotoNextStage(false);
                            else
                            {
                                if (BypassFarmGemCosts)
                                    a.GotoNextStage();
                                else if (AutoSpendFarmGems && a.CheckGemsAvailable(a.GetSpeedupCost()))
                                    a.GotoNextStage(true);
                            }
                        }
                        else if (i is ComposterFarmItem d)
                        {
                            if (d.pCurrentStage._Name.Contains("Harvest"))
                                d.GotoNextStage();
                            else if (d.pCurrentStage._Name.Contains("Feed"))
                                foreach (var consumable in d._CompostConsumables)
                                    if (consumable != null)
                                    {
                                        var userItemData = CommonInventoryData.pInstance.FindItem(consumable.ItemID);
                                        if (userItemData != null && consumable.Amount <= userItemData.Quantity)
                                        {
                                            d.SetCurrentUsedConsumableCriteria(consumable);
                                            d.GotoNextStage(false);
                                            break;
                                        }
                                    }
                            
                        }
                        else if (i is FishTrapFarmItem t)
                        {
                            if (t.pCurrentStage._Name.Contains("Harvest"))
                                t.GotoNextStage();
                            else if (t.pCurrentStage._Name.Contains("Feed"))
                                foreach (var consumable in t._FishTrapConsumables)
                                    if (consumable != null)
                                    {
                                        var userItemData = CommonInventoryData.pInstance.FindItem(consumable.ItemID);
                                        if (userItemData != null && consumable.Amount <= userItemData.Quantity)
                                        {
                                            t.SetCurrentUsedConsumableCriteria(consumable);
                                            t.GotoNextStage(false);
                                            break;
                                        }
                                    }
                        }
                    }
            }
            if (Input.GetKeyDown(ChangeDragonFireballColour) && AvAvatar.pState != AvAvatarState.PAUSED && AvAvatar.pState != AvAvatarState.NONE && AvAvatar.GetUIActive() && SanctuaryManager.pCurPetInstance)
            {
                AvAvatar.SetUIActive(false);
                AvAvatar.pState = AvAvatarState.PAUSED;
                var p = SanctuaryManager.pCurPetInstance.pData;
                var ep = ExtendedPetData.Get(p);
                KAUICursorManager.SetDefaultCursor("Loading", true);
                Patch_SelectName.SkipNameChecks = true;
                RsResourceManager.LoadAssetFromBundle(GameConfig.GetKeyData("SelectNameAsset"), (a,b,c,d,e) =>
                {
                    if (b == RsResourceLoadEvent.COMPLETE)
                    {
                        KAUICursorManager.SetDefaultCursor("Arrow", true);
                        GameObject ui = null;
                        try
                        {
                            ui = Instantiate((GameObject)d);
                            var s = ui.GetComponent<UiSelectName>();
                            s.FindItem("TxtTitle").SetText("Select your fireball colour");
                            s.FindItem("TxtHint").SetText("Enter colour here");
                            s.FindItem("TxtSugestedNames").SetText("Suggested colours:");
                            s.FindItem("TxtStatus").SetText("Colour may be either a simple colour name or hex number");
                            s.Independent = false;
                            var rand = new System.Random();
                            s.SetNames(ep.FireballColor?.ToHex() ?? "none", ExtentionMethods.colorPresets.GetRandom(6).Select(x => rand.Next(4) == 0 ? x.Value.ToHex() : x.Key).ToArray());
                            s.SetCallback((w, x, y, z) =>
                            {
                                if (w == UiSelectName.Status.Accepted)
                                {
                                    KAUICursorManager.SetDefaultCursor("Loading", true);
                                    ep.FireballColor = x.TryParseColor(out var r) ? (Color?)r : null;
                                    p.SaveDataReal(g =>
                                    {
                                        KAUICursorManager.SetDefaultCursor("Arrow", true);
                                        Patch_SelectName.SkipNameChecks = false;
                                        if (g.RaisedPetSetResult == RaisedPetSetResult.Success)
                                            OnPopupClose();
                                        else
                                            GameUtilities.DisplayOKMessage("PfKAUIGenericDB", "Fireball colour save failed", gameObject, "OnPopupClose");
                                    }, null, false);
                                }
                                else if (w == UiSelectName.Status.Closed)
                                {
                                    Patch_SelectName.SkipNameChecks = false;
                                    OnPopupClose();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            if (ui)
                                Destroy(ui);
                            Logger.LogError(ex);
                            Patch_SelectName.SkipNameChecks = false;
                            GameUtilities.DisplayOKMessage("PfKAUIGenericDB", "An error occured", gameObject, "OnPopupClose");
                        }
                    }
                    else if (b == RsResourceLoadEvent.ERROR)
                    {
                        Patch_SelectName.SkipNameChecks = false;
                        KAUICursorManager.SetDefaultCursor("Arrow", true);
                        GameUtilities.DisplayOKMessage("PfKAUIGenericDB", "Load failed", gameObject, "OnPopupClose");
                    }
                }, typeof(GameObject), false, null);
            }
            if (Input.GetKeyDown(ChangeDragonsGender) && AvAvatar.pState != AvAvatarState.PAUSED && AvAvatar.pState != AvAvatarState.NONE && AvAvatar.GetUIActive() && SanctuaryManager.pCurPetInstance)
            {
                AvAvatar.SetUIActive(false);
                AvAvatar.pState = AvAvatarState.PAUSED;
                if (SanctuaryManager.pCurPetInstance.pData.Gender != Gender.Male && SanctuaryManager.pCurPetInstance.pData.Gender != Gender.Female)
                    GameUtilities.DisplayOKMessage("PfKAUIGenericDB", $"{SanctuaryManager.pCurPetInstance.pData.Name} does not have a gender. Unable to change it", gameObject, "OnPopupClose");
                else
                {
                    changingPet = SanctuaryManager.pCurPetInstance;
                    GameUtilities.DisplayGenericDB("PfKAUIGenericDB", $"Are you sure you want to change {changingPet.pData.Name} to {(changingPet.pData.Gender == Gender.Male ? "fe" : "")}male?", "Change Dragon Gender", gameObject, "ChangeDragonGender", "OnPopupClose", null, "OnPopupClose", true);
                }
            }
            waitingTime += Time.deltaTime;
            if (waitingText)
            {
                if (waitingTime >= 1)
                {
                    textContainer.offsetMin = new Vector2(0, -waitingText.preferredHeight - 40);
                    textContainer.offsetMax = new Vector2(waitingText.preferredWidth + 40, 0);
                    waitingTime -= 1;
                }
                var t = $"Checking for mod updates ({running.Count} remaining)";
                var s = new StringBuilder();
                for (int i = 0; i < t.Length; i++)
                {
                    s.Append("<color=#");
                    s.Append(ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(0, 0, (float)(Math.Sin((i / (double)t.Length - waitingTime) * Math.PI * 2) / 4 + 0.75))));
                    s.Append(">");
                    s.Append(t[i]);
                    s.Append("</color>");
                }
                waitingText.text = s.ToString();
            }
            if (AlwaysMaxHappiness && SanctuaryManager.pCurPetInstance)
            {
                var cur = SanctuaryManager.pCurPetInstance.GetPetMeter(SanctuaryPetMeterType.HAPPINESS).mMeterValData.Value;
                var max = SanctuaryData.GetMaxMeter(SanctuaryPetMeterType.HAPPINESS, SanctuaryManager.pCurPetInstance.pData);
                if (cur < max)
                    SanctuaryManager.pCurPetInstance.UpdateMeter(SanctuaryPetMeterType.HAPPINESS, max - cur);
            }
        }
        SanctuaryPet changingPet;
        void ChangeDragonGender()
        {
            if (!changingPet || changingPet.pData == null)
                return;
            changingPet.pData.Gender = changingPet.pData.Gender == Gender.Male ? Gender.Female : Gender.Male;
            changingPet.SaveData();
            OnPopupClose();
        }
        void OnPopupClose()
        {
            AvAvatar.pState = AvAvatarState.IDLE;
            AvAvatar.SetUIActive(true);
        }

        static Dictionary<string, (PetStatType, string, string)> FlightFieldToType = new Dictionary<string, (PetStatType, string, string)>
        {
            { "_YawTurnRate",(PetStatType.TURNRATE,"TRN","") },
            { "_PitchTurnRate",(PetStatType.PITCHRATE,"PCH", "") },
            { "_Acceleration",(PetStatType.ACCELERATION,"ACL", "") },
            { "_Speed",(PetStatType.MAXSPEED,"FSP", "Pet ") }
        };
        static Dictionary<string, (string, string)> PlayerFieldToType = new Dictionary<string, (string, string)>
        {
            { "_MaxForwardSpeed",("Walk Speed","WSP") },
            { "_Gravity",("Gravity","GRV") },
            { "_Height",("Height","HGT") },
            { "_PushPower",("Push Power","PSH") }
        };
        static Dictionary<SanctuaryPetMeterType, (string,string)> MeterToName = new Dictionary<SanctuaryPetMeterType, (string, string)>
        {
            { SanctuaryPetMeterType.ENERGY, ("Energy","NRG") },
            { SanctuaryPetMeterType.HAPPINESS, ("Happiness","HAP") },
            { SanctuaryPetMeterType.HEALTH, ("Health","DHP") },
            { SanctuaryPetMeterType.RACING_ENERGY, ("Racing Energy","RNR") },
            { SanctuaryPetMeterType.RACING_FIRE, ("Racing Fire","RFR") }
        };
        static Dictionary<string, CustomStatInfo> statCache = new Dictionary<string, CustomStatInfo>();
        public static CustomStatInfo GetCustomStatInfo(string AttributeName)
        {
            if (AttributeName == null)
                return null;
            if (!statCache.TryGetValue(AttributeName, out var v))
            {
                var name = AttributeName;
                var abv = "???";
                var found = false;
                if (AttributeName.TryGetAttributeField(out var field))
                {
                    if (FlightFieldToType.TryGetValue(field, out var type))
                    {
                        found = true;
                        name = type.Item3 + SanctuaryData.GetDisplayTextFromPetStat(type.Item1);
                        abv = type.Item2;
                    }
                    else if (PlayerFieldToType.TryGetValue(field,out var type3))
                    {
                        found = true;
                        (name, abv) = type3;
                    }
                }
                if (!found && Enum.TryParse<SanctuaryPetMeterType>(AttributeName, true, out var type2) && MeterToName.TryGetValue(type2, out var meterName))
                {
                    found = true;
                    (name, abv) = meterName;
                }
                statCache[AttributeName] = v = new CustomStatInfo(AttributeName,name,abv,found);
            }
            return v;
        }

        public static int GemCost;
        public static int CoinCost;
        public static List<ItemData> Buying;
        public void ConfirmBuyAll()
        {
            if ( GemCost > Money.pGameCurrency || CoinCost > Money.pCashCurrency)
            {
                GameUtilities.DisplayOKMessage("PfKAUIGenericDB", GemCost > Money.pGameCurrency ? CoinCost > Money.pCashCurrency ? "Not enough gems and coins" : "Not enough gems" : "Not enough coins", null, "");
                return;
            }
            foreach (var i in Buying)
                CommonInventoryData.pInstance.AddPurchaseItem(i.ItemID, 1, "HandyTweaks.BuyAll");
            KAUICursorManager.SetExclusiveLoadingGear(true);
            CommonInventoryData.pInstance.DoPurchase(0,0,x =>
            {
                KAUICursorManager.SetExclusiveLoadingGear(false);
                GameUtilities.DisplayOKMessage("PfKAUIGenericDB", x.Success ? "Purchase complete" : "Purchase failed", null, "");
                if (x.Success)
                    KAUIStore.pInstance.pChooseMenu.ChangeCategory(KAUIStore.pInstance.pFilter, true);

            });
        }

        public void DoNothing() { }


        static Main()
        {
            if (!TomlTypeConverter.CanConvert(typeof(Dictionary<string, bool>)))
                TomlTypeConverter.AddConverter(typeof(Dictionary<string, bool>), new TypeConverter()
                {
                    ConvertToObject = (str, type) =>
                    {
                        var d = new Dictionary<string, bool>();
                        if (str == null)
                            return d;
                        var split = str.Split('|');
                        foreach (var i in split)
                            if (i.Length != 0)
                            {
                                var parts = i.Split(',');
                                if (parts.Length != 2)
                                    Debug.LogWarning($"Could not load entry \"{i}\". Entries must have exactly 2 values divided by commas");
                                else
                                {
                                    if (d.ContainsKey(parts[0]))
                                        Debug.LogWarning($"Duplicate entry name \"{parts[0]}\" from \"{i}\". Only last entry will be kept");
                                    var value = false;
                                    if (bool.TryParse(parts[1], out var v))
                                            value = v;
                                        else
                                            Debug.LogWarning($"Value \"{parts[1]}\" in \"{i}\". Could not be parsed as a bool");
                                    d[parts[0]] = value;
                                }
                            }
                        return d;
                    },
                    ConvertToString = (obj, type) =>
                    {
                        if (!(obj is Dictionary<string, bool> d))
                            return "";
                        var str = new StringBuilder();
                        var k = d.Keys.ToList();
                        k.Sort();
                        foreach (var key in k)
                        {
                            if (str.Length > 0)
                                str.Append("|");
                            str.Append(key);
                            str.Append(",");
                            str.Append(d[key].ToString(CultureInfo.InvariantCulture));
                        }
                        return str.ToString();
                    }
                });
        }
    }

    public class CustomStatInfo
    {
        public readonly string AttributeName;
        public readonly string DisplayName;
        public readonly string Abreviation;
        public readonly bool Valid;
        public CustomStatInfo(string Att, string Dis, string Abv, bool Val)
        {
            AttributeName = Att;
            DisplayName = Dis;
            Abreviation = Abv;
            Valid = Val;
        }
    }

    public enum AimMode
    {
        Default,
        MouseWhenNoTargets,
        FindTargetNearMouse,
        AlwaysMouse
    }

    static class ExtentionMethods
    {
        static MethodInfo _IsCropPlaced = typeof(FarmSlot).GetMethod("IsCropPlaced", ~BindingFlags.Default);
        public static bool IsCropPlaced(this FarmSlot item) => (bool)_IsCropPlaced.Invoke(item, new object[0]);
        static MethodInfo _OnContextAction = typeof(MyRoomItem).GetMethod("OnContextAction", ~BindingFlags.Default);
        public static void OnContextAction(this MyRoomItem item, string actionName) => _OnContextAction.Invoke(item, new[] { actionName });
        static MethodInfo _IsCurrentStageFeedConsumed = typeof(AnimalFarmItem).GetMethod("IsCurrentStageFeedConsumed", ~BindingFlags.Default);
        public static bool IsCurrentStageFeedConsumed(this AnimalFarmItem item) => (bool)_IsCurrentStageFeedConsumed.Invoke(item, new object[0]);
        static MethodInfo _ConsumeFeed = typeof(AnimalFarmItem).GetMethod("ConsumeFeed", ~BindingFlags.Default);
        public static void ConsumeFeed(this AnimalFarmItem item) => _ConsumeFeed.Invoke(item, new object[0]);
        static FieldInfo _mCurrentUsedConsumableCriteria = typeof(ComposterFarmItem).GetField("mCurrentUsedConsumableCriteria", ~BindingFlags.Default);
        public static void SetCurrentUsedConsumableCriteria(this ComposterFarmItem item, ItemStateCriteriaConsumable consumable) => _mCurrentUsedConsumableCriteria.SetValue(item, consumable);
        static FieldInfo _mCurrentUsedConsumableCriteria2 = typeof(FishTrapFarmItem).GetField("mCurrentUsedConsumableCriteria", ~BindingFlags.Default);
        public static void SetCurrentUsedConsumableCriteria(this FishTrapFarmItem item, ItemStateCriteriaConsumable consumable) => _mCurrentUsedConsumableCriteria2.SetValue(item, consumable);
        static MethodInfo _GetSpeedupCost = typeof(FarmItem).GetMethod("GetSpeedupCost", ~BindingFlags.Default);
        public static int GetSpeedupCost(this FarmItem item) => (int)_GetSpeedupCost.Invoke(item, new object[0]);
        static MethodInfo _CheckGemsAvailable = typeof(FarmItem).GetMethod("CheckGemsAvailable", ~BindingFlags.Default);
        public static bool CheckGemsAvailable(this FarmItem item, int count) => (bool)_CheckGemsAvailable.Invoke(item, new object[] { count });
        static FieldInfo _mIsWaitingForWsCall = typeof(FarmItem).GetField("mIsWaitingForWsCall", ~BindingFlags.Default);
        public static bool IsWaitingForWsCall(this FarmItem item) => (bool)_mIsWaitingForWsCall.GetValue(item);
        static MethodInfo _SaveAndExitQuiz = typeof(UiQuizPopupDB).GetMethod("SaveAndExitQuiz", ~BindingFlags.Default);
        public static void SaveAndExitQuiz(this UiQuizPopupDB item) => _SaveAndExitQuiz.Invoke(item, new object[0]);
        static MethodInfo _CreateDragonWiget = typeof(UiStableQuestDragonsMenu).GetMethod("CreateDragonWiget", ~BindingFlags.Default);
        public static void CreateDragonWiget(this UiStableQuestDragonsMenu menu, RaisedPetData rpData) => _CreateDragonWiget.Invoke(menu, new object[] { rpData });
        static MethodInfo _ShowStatInfo = typeof(UiStatsCompareMenu).GetMethod("ShowStatInfo", ~BindingFlags.Default);
        public static void ShowStatInfo(this UiStatsCompareMenu instance, KAWidget widget, string baseStat, string statName, string compareStat, string diffVal, StatCompareResult compareResult = StatCompareResult.Equal, bool showCompare = false) =>
            _ShowStatInfo.Invoke(instance, new object[] { widget, baseStat, statName, compareStat, diffVal, (int)compareResult, showCompare });
        static FieldInfo _mModifierFieldMap = typeof(AvAvatarController).GetField("mModifierFieldMap", ~BindingFlags.Default);
        public static bool TryGetAttributeField(this string att, out string fieldName)
        {
            if (att != null && _mModifierFieldMap.GetValue(null) is Dictionary<string, string> d)
                return d.TryGetValue(att, out fieldName);
            fieldName = null;
            return false;
        }
        public static string GetAttributeField(this string att) => att.TryGetAttributeField(out var f) ? f : null;
        static FieldInfo _mContentMenuCombat = typeof(UiStatPopUp).GetField("mContentMenuCombat", ~BindingFlags.Default);
        public static KAUIMenu GetContentMenuCombat(this UiStatPopUp item) => (KAUIMenu)_mContentMenuCombat.GetValue(item);
        static FieldInfo _mInventory = typeof(CommonInventoryData).GetField("mInventory", ~BindingFlags.Default);
        public static Dictionary<int, List<UserItemData>> FullInventory(this CommonInventoryData inv) => (Dictionary<int, List<UserItemData>>)_mInventory.GetValue(inv);
        static FieldInfo _mCachedItemData = typeof(KAUIStoreChooseMenu).GetField("mCachedItemData", ~BindingFlags.Default);
        public static Dictionary<ItemData, int> GetCached(this KAUIStoreChooseMenu menu) => (Dictionary<ItemData, int>)_mCachedItemData.GetValue(menu);
        static MethodInfo _RemoveDragonSkin = typeof(UiDragonCustomization).GetMethod("RemoveDragonSkin", ~BindingFlags.Default);
        public static void RemoveDragonSkin(this UiDragonCustomization menu) => _RemoveDragonSkin.Invoke(menu, new object[0]);

        public static string ReadContent(this WebResponse response, Encoding encoding = null)
        {
            using (var stream = response.GetResponseStream())
            {
                var b = new byte[stream.Length];
                stream.Read(b, 0, b.Length);
                return (encoding ?? Encoding.UTF8).GetString(b);
            }
        }
        public static string GetJsonEntry(this WebResponse response, string key, Encoding encoding = null)
        {
            using (var stream = response.GetResponseStream())
            {
                var reader = System.Runtime.Serialization.Json.JsonReaderWriterFactory.CreateJsonReader(stream, new System.Xml.XmlDictionaryReaderQuotas() {  });
                while (reader.Name != key && reader.Read())
                { }
                if (reader.Name == key && reader.Read())
                    return reader.Value;
                return null;
            }
        }

        public static bool IsRankLocked(this ItemData data, out int rid, int rankType)
        {
            rid = 0;
            if (data.RewardTypeID > 0)
                rankType = data.RewardTypeID;
            if (data.Points != null && data.Points.Value > 0)
            {
                rid = data.Points.Value;
                UserAchievementInfo userAchievementInfoByType = UserRankData.GetUserAchievementInfoByType(rankType);
                return userAchievementInfoByType == null || userAchievementInfoByType.AchievementPointTotal == null || rid > userAchievementInfoByType.AchievementPointTotal.Value;
            }
            if (data.RankId != null && data.RankId.Value > 0)
            {
                rid = data.RankId.Value;
                UserRank userRank = (rankType == 8) ? PetRankData.GetUserRank(SanctuaryManager.pCurPetData) : UserRankData.GetUserRankByType(rankType);
                return userRank == null || rid > userRank.RankID;
            }
            return false;
        }

        public static bool HasPrereqItem(this ItemData data)
        {
            if (data.Relationship == null)
                return true;
            ItemDataRelationship[] relationship = data.Relationship;
            foreach (var itemDataRelationship in data.Relationship)
                if (itemDataRelationship.Type == "Prereq")
                    return (ParentData.pIsReady && ParentData.pInstance.HasItem(itemDataRelationship.ItemId)) || CommonInventoryData.pInstance.FindItem(itemDataRelationship.ItemId) != null;
            return true;
        }
        public static Dictionary<string, Color> colorPresets = typeof(Color).GetProperties().Where(x => x.PropertyType == x.DeclaringType && x.GetGetMethod(false)?.IsStatic == true).ToDictionary(x => x.Name.ToLowerInvariant(),x => (Color)x.GetValue(null));
        public static bool TryParseColor(this string clr,out Color color)
        {
            clr = clr.ToLowerInvariant();
            color = default;
            if (colorPresets.TryGetValue(clr,out var v))
                color = v;
            else if (uint.TryParse(clr,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var n))
                color = new Color32((byte)(n / 0x10000 & 0xFF), (byte)(n / 0x100 & 0xFF), (byte)(n & 0xFF), 255);
            else
                return false;
            return true;
        }
        public static string ToHex(this Color32 color) => color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this Color color) => ((Color32)color).ToHex();
        public static Color Shift(this Color oc, Color nc)
        {
            var s = Math.Max(Math.Max(oc[0], oc[1]), oc[2]);
            return new Color(nc.r * s, nc.g * s, nc.b * s, nc.a * oc.a);
        }
        public static ParticleSystem.MinMaxGradient Shift(this ParticleSystem.MinMaxGradient o, Color newColor)
        {
            if (o.mode == ParticleSystemGradientMode.Color)
                return new ParticleSystem.MinMaxGradient(o.color.Shift(newColor));

            if (o.mode == ParticleSystemGradientMode.Gradient)
                return new ParticleSystem.MinMaxGradient(new Gradient()
                {
                    mode = o.gradient.mode,
                    alphaKeys = o.gradient.alphaKeys,
                    colorKeys = o.gradient.colorKeys.Select(x => new GradientColorKey(x.color.Shift(newColor), x.time)).ToArray()
                });

            if (o.mode == ParticleSystemGradientMode.TwoColors)
                return new ParticleSystem.MinMaxGradient(o.colorMin.Shift(newColor), o.colorMax.Shift(newColor));

            if (o.mode == ParticleSystemGradientMode.TwoGradients)
                return new ParticleSystem.MinMaxGradient(new Gradient()
                {
                    mode = o.gradientMin.mode,
                    alphaKeys = o.gradientMin.alphaKeys,
                    colorKeys = o.gradientMin.colorKeys.Select(x => new GradientColorKey(x.color.Shift(newColor), x.time)).ToArray()
                }, new Gradient()
                {
                    mode = o.gradientMax.mode,
                    alphaKeys = o.gradientMax.alphaKeys,
                    colorKeys = o.gradientMax.colorKeys.Select(x => new GradientColorKey(x.color.Shift(newColor), x.time)).ToArray()
                });

            return o;
        }
        public static List<T> GetRandom<T>(this ICollection<T> c, int count)
        {
            var r = new System.Random();
            var n = c.Count;
            if (count >= n)
                return c.ToList();
            var l = new List<T>(count);
            if (count > 0)
                foreach (var i in c)
                    if (r.Next(n--) < count)
                    {
                        count--;
                        l.Add(i);
                        if (count == 0)
                            break;
                    }
            return l;
        }
    }
    public enum StatCompareResult
    {
        Equal,
        Greater,
        Lesser
    }

    [HarmonyPatch(typeof(UiMyRoomBuilder), "Update")]
    static class Patch_RoomBuilder
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Stfld && code[i].operand is FieldInfo f && f.Name == "mCanPlace")
                    code.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_RoomBuilder), nameof(EditCanPlace))));
            return code;
        }
        static bool EditCanPlace(bool original) => original || Main.CanPlaceAnywhere;
    }

    [HarmonyPatch(typeof(UiQuizPopupDB))]
    static class Patch_InstantAnswer
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start(UiQuizPopupDB __instance, ref bool ___mIsQuestionAttemped, ref bool ___mCheckForTaskCompletion)
        {
            if (Main.SkipTrivia)
            {
                ___mCheckForTaskCompletion = true;
                ___mIsQuestionAttemped = true;
                __instance._MessageObject.SendMessage(__instance._QuizAnsweredMessage, true, SendMessageOptions.DontRequireReceiver);
                __instance.SaveAndExitQuiz();
            }
        }
        [HarmonyPatch("IsQuizAnsweredCorrect")]
        [HarmonyPostfix]
        static void IsQuizAnsweredCorrect(ref bool __result)
        {
            if (Main.SkipTrivia)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    static class Patch_ApplyTexture
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AvatarData), "SetStyleTexture", new[] { typeof(AvatarData.InstanceInfo), typeof(string), typeof(string), typeof(int) });
            yield return AccessTools.Method(typeof(CustomAvatarState), "SetPartTexture");
            yield return AccessTools.Method(typeof(CustomAvatarState), "SetTextureData");
            yield return AccessTools.Method(typeof(UiAvatarCustomizationMenu), "SetPartTextureByIndex");
            yield return AccessTools.Method(typeof(UiAvatarCustomizationMenu), "UpdatePartTexture");
        }
        static bool Prefix() => !Input.GetKey(Main.DontApplyTextures);
    }

    [HarmonyPatch(typeof(AvatarData), "SetGeometry", typeof(AvatarData.InstanceInfo), typeof(string), typeof(string), typeof(int))]
    static class Patch_ApplyGeometry
    {
        static bool Prefix() => !Input.GetKey(Main.DontApplyGeometry);
    }

    [HarmonyPatch(typeof(UiStableQuestDragonsMenu), "LoadDragonsList")]
    static class Patch_LoadStableQuestDragonsList
    {
        static bool Prefix(UiStableQuestDragonsMenu __instance)
        {
            if (!Main.SortStableQuestDragonsByValue)
                return true;
            __instance.ClearItems();
            
            var l = new SortedSet<(float, RaisedPetData)>(new ComparePetValue());
            if (RaisedPetData.pActivePets != null)
                foreach (RaisedPetData[] array in RaisedPetData.pActivePets.Values)
                    if (array != null)
                        foreach (RaisedPetData pet in array)
                            if (StableData.GetByPetID(pet.RaisedPetID) != null && pet.pStage >= RaisedPetStage.BABY && pet.IsPetCustomized())
                                l.Add((TimedMissionManager.pInstance.GetWinProbabilityForPet(UiStableQuestMain.pInstance._StableQuestDetailsUI.pCurrentMissionData, pet.RaisedPetID),pet));
            foreach (var p in l)
                __instance.CreateDragonWiget(p.Item2);
            __instance.pMenuGrid.repositionNow = true;
            return false;
        }
    }

    class ComparePetValue : IComparer<(float, RaisedPetData)>
    {
        public int Compare((float, RaisedPetData) a, (float, RaisedPetData) b)
        {
            var c = b.Item1.CompareTo(a.Item1);
            return c == 0 ? 1 : c;
        }
    }

    [HarmonyPatch]
    static class Patch_ShowFlightCompare
    {
        static UiStatCompareDB.ItemCompareDetails equipped;
        static UiStatCompareDB.ItemCompareDetails unequipped;
        [HarmonyPatch(typeof(UiStatCompareDB), "Initialize")]
        [HarmonyPrefix]
        static void UiStatCompareDB_Initialize(UiStatCompareDB.ItemCompareDetails inLeftItem, UiStatCompareDB.ItemCompareDetails inRightItem)
        {
            equipped = inLeftItem;
            unequipped = inRightItem;
        }
        [HarmonyPatch(typeof(UiStatsCompareMenu), "Populate")]
        [HarmonyPostfix]
        static void UiStatsCompareMenu_Populate(UiStatsCompareMenu __instance, bool showCompare, ItemStat[] equippedStats, ItemStat[] unequippedStats)
        {
            if (!Main.ShowRacingEquipmentStats)
                return;
            bool shouldClear = !(equippedStats?.Length > 0 || unequippedStats?.Length > 0);
            void Show(string name, string stat1, string stat2)
            {
                if (stat1 == null && stat2 == null)
                    return;
                if (shouldClear)
                {
                    __instance.ClearItems();
                    shouldClear = false;
                }
                KAWidget kawidget2 = __instance.DuplicateWidget(__instance._Template, UIAnchor.Side.Center);
                __instance.AddWidget(kawidget2);
                kawidget2.SetVisibility(true);
                string text = null;
                string text2 = null;
                string diffVal = null;
                var num = 0f;
                var num2 = 0f;
                if (stat1 != null)
                {
                    float.TryParse(stat1, out num);
                    text = Math.Round(num * 100) + "%";
                }
                if (stat2 != null)
                {
                    float.TryParse(stat2, out num2);
                    text2 = Math.Round(num2 * 100) + "%";
                }
                var statCompareResult = (num == num2) ? StatCompareResult.Equal : (num2 > num) ? StatCompareResult.Greater : StatCompareResult.Lesser;
                if (statCompareResult != StatCompareResult.Equal)
                    diffVal = Math.Round(Math.Abs(num - num2) * 100) + "%";
                __instance.ShowStatInfo(kawidget2, text, name, text2, diffVal, statCompareResult, showCompare);
            }
            var s = new SortedSet<string>();
            foreach (var att in new[] { equipped?._ItemData?.Attribute, unequipped?._ItemData?.Attribute })
                if (att != null)
                    foreach (var a in att)
                    {
                        if (a == null || s.Contains(a.Key))
                            continue;
                        var n = Main.GetCustomStatInfo( a.Key);
                        if (n != null && n.Valid)
                            s.Add(a.Key);
                    }
            foreach (var f in s)
                Show(
                    Main.GetCustomStatInfo(f).DisplayName,
                    equipped?._ItemData?.GetAttribute<string>(f, null),
                    unequipped?._ItemData?.GetAttribute<string>(f, null));
        }
        [HarmonyPatch(typeof(UiStoreStatCompare), "UpdateStatsCompareData")]
        [HarmonyPostfix]
        static void UiStoreStatCompare_UpdateStatsCompareData(UiStoreStatCompare __instance, List<UiStoreStatCompare.StatDataContainer> ___mStatDataList, KAUIMenu ___mContentMenu, int previewIndex, List<PreviewItemData> previewList)
        {
            if (!Main.ShowRacingEquipmentStats)
                return;
            ___mStatDataList.RemoveAll(x => x._EquippedStat == x._ModifiedStat);
            void Show(string name, string abv, float equipped, float unequipped)
            {
                var statDataContainer = new UiStoreStatCompare.StatDataContainer();
                statDataContainer._StatName = name;
                statDataContainer._AbvStatName = abv;
                statDataContainer._EquippedStat = equipped;
                statDataContainer._ModifiedStat = unequipped;
                statDataContainer._DiffStat = statDataContainer._ModifiedStat - statDataContainer._EquippedStat;
                ___mStatDataList.Add(statDataContainer);
                if (equipped != unequipped)
                {
                    var kawidget = ___mContentMenu.AddWidget(___mContentMenu._Template.name);
                    kawidget.FindChildItem("AbvStatWidget", true).SetText(statDataContainer._AbvStatName);
                    kawidget.FindChildItem("StatDiffWidget", true).SetText(Math.Round(Math.Abs(equipped - unequipped)) + "%");
                    var arrowWidget = kawidget.FindChildItem("ArrowWidget", true);
                    arrowWidget.SetVisibility(true);
                    arrowWidget.SetRotation(Quaternion.Euler(0f, 0f, 0f));
                    if (statDataContainer._DiffStat == 0f)
                    {
                        arrowWidget.SetVisibility(false);
                    }
                    else if (statDataContainer._DiffStat < 0f)
                    {
                        arrowWidget.pBackground.color = Color.red;
                        arrowWidget.SetRotation(Quaternion.Euler(0f, 0f, 180f));
                    }
                    else
                    {
                        arrowWidget.pBackground.color = Color.green;
                    }
                    kawidget.SetVisibility(true);
                }
            }
            var s = new SortedSet<string>();
            var d = new Dictionary<string, (float, float)>();
            var e = new Dictionary<string, (ItemData, ItemData)>();
            foreach (var part in AvatarData.pInstance.Part)
                if (part != null)
                {
                    var equipped = part.UserInventoryId > 0 ? CommonInventoryData.pInstance.FindItemByUserInventoryID(part.UserInventoryId.Value)?.Item : null;
                    if (equipped != null)
                    {
                        var key = part.PartType;
                        if (key.StartsWith("DEFAULT_"))
                            key = key.Remove(0, 8);
                        var t = e.GetOrCreate(key);
                        e[key] = (equipped, t.Item2);
                    }
                }
            foreach (var preview in previewIndex == -1 ? previewList as IEnumerable<PreviewItemData> : new[] { previewList[previewIndex] })
                if (preview.pItemData != null)
                {
                    var key = AvatarData.GetPartName(preview.pItemData);
                    if (key.StartsWith("DEFAULT_"))
                        key = key.Remove(0, 8);
                    var t = e.GetOrCreate(key);
                    if (t.Item2 == null)
                        e[key] = (t.Item1, preview.pItemData);
                }
            foreach (var p in e)
            {
                var item2 = p.Value.Item2 ?? p.Value.Item1;
                Debug.Log($"\n{p.Key}\n - [{p.Value.Item1?.Attribute?.Join(x => x.Key + "=" + x.Value)}]\n - [{item2?.Attribute?.Join(x => x.Key + "=" + x.Value)}]");
                if (p.Value.Item1?.Attribute != null)
                    foreach (var a in p.Value.Item1.Attribute)
                    {
                        if (a == null)
                            continue;
                        var cs = Main.GetCustomStatInfo(a.Key);
                        if (cs == null || !cs.Valid)
                            continue;
                        if (!float.TryParse(a.Value, out var value))
                            continue;
                        s.Add(a.Key);
                        var t = d.GetOrCreate(a.Key);
                        d[a.Key] = (t.Item1 + value, t.Item2);
                    }
                if (item2?.Attribute != null)
                    foreach (var a in item2.Attribute)
                    {
                        if (a == null)
                            continue;
                        var cs = Main.GetCustomStatInfo(a.Key);
                        if (cs == null || !cs.Valid)
                            continue;
                        if (!float.TryParse(a.Value, out var value))
                            continue;
                        s.Add(a.Key);
                        var t = d.GetOrCreate(a.Key);
                        d[a.Key] = (t.Item1, t.Item2 + value);
                    }
            }
            foreach (var i in s)
            {
                var t = d[i];
                var c = Main.GetCustomStatInfo(i);
                if (t.Item1 != t.Item2)
                    Show(c.DisplayName, c.Abreviation, t.Item1 * 100, t.Item2 * 100);
            }
        }

        [HarmonyPatch(typeof(UiAvatarCustomization), "ShowAvatarStats")]
        [HarmonyPostfix]
        static void UiAvatarCustomization_ShowAvatarStats(UiAvatarCustomization __instance, UiStatPopUp ___mUiStats)
        {
            if (!Main.ShowRacingEquipmentStats)
                return;
            void Show(string name, string value)
            {
                KAWidget kawidget = ___mUiStats.GetContentMenuCombat().AddWidget(___mUiStats.GetContentMenuCombat()._Template.name);
                kawidget.FindChildItem("CombatStatWidget", true).SetText(name);
                kawidget.FindChildItem("CombatStatValueWidget", true).SetText(value);
            }
            var custom = __instance.pCustomAvatar;
            var e = new HashSet<string>();
            var s = new SortedSet<string>();
            var d = new Dictionary<string, float>();
            foreach (var part in AvatarData.pInstance.Part)
                if (part != null)
                {
                    var equipped = custom == null
                        ? part.UserInventoryId > 0
                            ? CommonInventoryData.pInstance.FindItemByUserInventoryID(part.UserInventoryId.Value)?.Item
                            : null
                        : CommonInventoryData.pInstance.FindItemByUserInventoryID(custom.GetInventoryId(part.PartType))?.Item;
                    if (equipped != null)
                    {
                        var key = part.PartType;
                        if (key.StartsWith("DEFAULT_"))
                            key = key.Remove(0, 8);
                        if (!e.Add(key))
                            continue;
                        if (equipped.Attribute != null)
                            foreach (var a in equipped.Attribute)
                            {
                                if (a == null)
                                    continue;
                                var cs = Main.GetCustomStatInfo(a.Key);
                                if (cs == null || !cs.Valid)
                                    continue;
                                if (!float.TryParse(a.Value, out var value))
                                    continue;
                                s.Add(a.Key);
                                d[a.Key] = d.GetOrCreate(a.Key) + value;
                            }
                    }
                }
            foreach (var k in s)
                Show(Main.GetCustomStatInfo(k).DisplayName, Math.Round(d[k] * 100) + "%");
        }
    }

    [HarmonyPatch(typeof(BaseUnityPlugin), MethodType.Constructor, new Type[0])]
    static class Patch_CreatePluginObj
    {
        static void Postfix(BaseUnityPlugin __instance)
        {
            if (Main.CheckForModUpdates)
                Main.instance.CheckModVersion(__instance);
        }
    }

    [HarmonyPatch(typeof(KAUIStore))]
    static class Patch_Store
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start(KAUIStore __instance, KAWidget ___mBtnPreviewBuy)
        {
            var n = __instance.DuplicateWidget(___mBtnPreviewBuy, UIAnchor.Side.BottomLeft);
            n.name = "btnBuyAll";
            n.SetText("Buy All");
            n.SetVisibility(true);
            n.SetInteractive(true);
            var p = ___mBtnPreviewBuy.transform.position;
            p.x = -p.x * 0.7f;
            n.transform.position = p;
        }

        [HarmonyPatch("OnClick")]
        [HarmonyPostfix]
        static void OnClick(KAUIStore __instance, KAWidget item)
        {
            if (item.name == "btnBuyAll")
            {
                var byCatergory = CommonInventoryData.pInstance.FullInventory();
                
                var all = new List<ItemData>();
                var check = new HashSet<int>();
                var gems = 0;
                var coins = 0;
                var cache = KAUIStore.pInstance.pChooseMenu.GetCached();
                foreach (var ite in cache.Keys)
                    if (ite != null
                        && !ite.IsBundleItem()
                        && !ite.HasCategory(Category.MysteryBox)
                        && !ite.HasCategory(Category.DragonTickets)
                        && !ite.HasCategory(Category.DragonAgeUp)
                        && (!ite.Locked || SubscriptionInfo.pIsMember)
                        && (__instance.pCategoryMenu.pDisableRankCheck || !ite.IsRankLocked(out _, __instance.pStoreInfo._RankTypeID))
                        && ite.HasPrereqItem()
                        && CommonInventoryData.pInstance.GetQuantity(ite.ItemID) <= 0
                        && check.Add(ite.ItemID))
                    {
                        all.Add(ite);
                        if (ite.GetPurchaseType() == 1)
                            coins += ite.GetFinalCost();
                        else
                            gems += ite.GetFinalCost();
                    }
                if (all.Count == 0)
                    GameUtilities.DisplayOKMessage("PfKAUIGenericDB", "No items left to buy", null, "");
                else
                {
                    Main.CoinCost = coins;
                    Main.GemCost = gems;
                    Main.Buying = all;
                    GameUtilities.DisplayGenericDB("PfKAUIGenericDB", $"Buying these {Main.Buying.Count} items will cost {(gems > 0 ? coins > 0 ? $"{coins} coins and {gems} gems" : $"{gems} gems" : coins > 0 ? $"{coins} coins" : "nothing")}. Are you sure you want to buy these?", "Buy All", Main.instance.gameObject, nameof(Main.ConfirmBuyAll), nameof(Main.DoNothing), null, null, true);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CaAvatarCam), "LateUpdate")]
    static class Patch_AvatarCam
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.Name == "mMaxCameraDistance")
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_AvatarCam), nameof(EditMaxZoom))));
                else if (code[i].opcode == OpCodes.Ldc_R4 && (float)code[i].operand == 0.25f)
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_AvatarCam), nameof(EditZoomSpeed))));
            return code;
        }
        static float EditMaxZoom(float original) => Main.InfiniteZoom ? float.PositiveInfinity : original;
        static float EditZoomSpeed(float original) => original * Main.ZoomSpeed;
    }

    [HarmonyPatch(typeof(UiDragonCustomization), "RemoveDragonSkin")]
    static class Patch_ChangeDragonColor
    {
        static bool Prefix() => !Main.DisableDragonAutomaticSkinUnequip;
    }

    [HarmonyPatch(typeof(SanctuaryPet), "UpdateShaders")]
    static class Patch_UpdatePetShaders
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.InsertRange(code.FindIndex(x => x.opcode == OpCodes.Ldloc_S && ((x.operand is LocalBuilder l && l.LocalIndex == 6) || (x.operand is IConvertible i && i.ToInt32(CultureInfo.InvariantCulture) == 6))) + 1,
                new[] 
                {
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_UpdatePetShaders), nameof(EditMat)))
                });
            return code;
        }
        static Material EditMat(Material material, Color primary)
        {
            if (material.HasProperty("_EmissiveColor"))
            {
                if (Main.ApplyDragonPrimaryToEmission)
                {
                    var e = MaterialEdit.Get(material).OriginalEmissive;
                    material.SetColor("_EmissiveColor", new Color(primary.r * e.strength, primary.g * e.strength, primary.b * e.strength, primary.a * e.alpha));
                }
                else
                    material.SetColor("_EmissiveColor", MaterialEdit.Get(material).OriginalEmissive.original);
            }
            return material;
        }
    }

    public class MaterialEdit
    {
        static ConditionalWeakTable<Material, MaterialEdit> data = new ConditionalWeakTable<Material, MaterialEdit>();
        public static MaterialEdit Get(Material material)
        {
            if (data.TryGetValue(material, out var edit)) return edit;
            edit = data.GetOrCreateValue(material);
            if (material.HasProperty("_EmissiveColor"))
            {
                var c = material.GetColor("_EmissiveColor");
                edit.OriginalEmissive = (Math.Max(Math.Max(c.r,c.g),c.b),c.a, c);
            }
            return edit;
        }
        public (float strength, float alpha, Color original) OriginalEmissive;
    }

    [HarmonyPatch(typeof(SanctuaryData), "GetPetCustomizationType", typeof(int))]
    static class Patch_PetCustomization
    {
        static bool Prefix(ref PetCustomizationType __result)
        {
            if (Main.AllowCustomizingSpecialDragons)
            {
                __result = PetCustomizationType.Default;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UserNotifyDragonTicket))]
    static class Patch_OpenCloseCustomization
    {
        public static (string, string)? closed;
        [HarmonyPatch("ActivateDragonCreationUIObj")]
        [HarmonyPrefix]
        static void ActivateDragonCreationUIObj()
        {
            if (KAUIStore.pInstance)
            {
                closed = (KAUIStore.pInstance.pCategory, KAUIStore.pInstance.pStoreInfo._Name);
                KAUIStore.pInstance.ExitStore();
            }
        }
        [HarmonyPatch("OnStableUIClosed")]
        [HarmonyPostfix]
        static void OnStableUIClosed()
        {
            if (closed != null)
            {
                var t = closed.Value;
                closed = null;
                StoreLoader.Load(true, t.Item1, t.Item2, null, UILoadOptions.AUTO, "", null);
            }
        }
    }

    [HarmonyPatch(typeof(SanctuaryData), "GetLocalizedPetName")]
    static class Patch_GetPetName
    {
        static void Postfix(RaisedPetData raisedPetData, ref string __result)
        {
            if (__result.Length == 15 && __result.StartsWith("Dragon-") && uint.TryParse(__result.Remove(0, 7), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                __result = SanctuaryData.GetPetDefaultName(raisedPetData.PetTypeID);
        }
    }

    [HarmonyPatch]
    static class Patch_GetStableQuestDuration
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StableQuestSlotWidget), "HandleAdButtons");
            yield return AccessTools.Method(typeof(StableQuestSlotWidget), "StateChangeInit");
            yield return AccessTools.Method(typeof(StableQuestSlotWidget), "Update");
            yield return AccessTools.Method(typeof(TimedMissionManager), "CheckMissionCompleted", new[] { typeof(TimedMissionSlotData) });
            yield return AccessTools.Method(typeof(TimedMissionManager), "CheckMissionSuccess");
            yield return AccessTools.Method(typeof(TimedMissionManager), "GetCompletionTime");
            yield return AccessTools.Method(typeof(TimedMissionManager), "GetPetEngageTime");
            yield return AccessTools.Method(typeof(TimedMissionManager), "StartMission");
            yield return AccessTools.Method(typeof(UiStableQuestDetail), "HandleAdButton");
            yield return AccessTools.Method(typeof(UiStableQuestDetail), "MissionLogIndex");
            yield return AccessTools.Method(typeof(UiStableQuestDetail), "SetSlotData");
            yield return AccessTools.Method(typeof(UiStableQuestMissionStart), "RefreshUi");
            yield return AccessTools.Method(typeof(UiStableQuestSlotsMenu), "OnAdWatched");
            yield break;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].operand is FieldInfo f && f.Name == "Duration" && f.DeclaringType == typeof(TimedMission))
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, typeof(Patch_GetStableQuestDuration).GetMethod(nameof(EditDuration), ~BindingFlags.Default)));
            return code;
        }

        static int EditDuration(int original) => (int)Math.Round(original * Main.StableQuestTimeMultiplier);
    }

    [HarmonyPatch]
    static class Patch_GetStableQuestBaseChance
    {
        static IEnumerable<MethodBase> TargetMethods() => from m in typeof(TimedMissionManager).GetMethods(~BindingFlags.Default) where m.Name == "GetWinProbability" select m;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].operand is FieldInfo f && f.Name == "WinFactor" && f.DeclaringType == typeof(TimedMission))
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, typeof(Patch_GetStableQuestBaseChance).GetMethod(nameof(EditChance), ~BindingFlags.Default)));
            return code;
        }

        static int EditChance(int original) => original + Main.StableQuestChanceBoost;
    }

    [HarmonyPatch(typeof(TimedMissionManager), "GetWinProbabilityForPet")]
    static class Patch_GetStableQuestPetChance
    {
        static void Postfix(ref float __result) => __result *= Main.StableQuestDragonValueMultiplier;
    }

    [HarmonyPatch]
    static class Patch_GetInputLength
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KAUIStoreBuyPopUp), "RefreshValues");
            yield return AccessTools.Method(typeof(UIInput), "Insert");
            yield return AccessTools.Method(typeof(UIInput), "Validate", new[] { typeof(string) });
            yield return AccessTools.Method(typeof(UiItemTradeGenericDB), "RefreshQuantity");
            yield return AccessTools.Method(typeof(UiPrizeCodeEnterDB), "Start");
            yield break;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].operand is FieldInfo f && f.Name == "characterLimit" && f.DeclaringType == typeof(UIInput))
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, typeof(Patch_GetInputLength).GetMethod(nameof(EditLength), ~BindingFlags.Default)));
            return code;
        }

        static int EditLength(int original) => Main.BiggerInputBoxes ? (int)Math.Min((long)original * original, int.MaxValue) : original;
    }

    [HarmonyPatch(typeof(UIInput),"Validate",typeof(string),typeof(int),typeof(char))]
    static class Patch_CanInput
    {
        static bool Prefix(UIInput __instance, string text, int pos, char ch, ref char __result)
        {
            if (Main.MoreNameFreedom && (__instance.validation == UIInput.Validation.Alphanumeric || __instance.validation == UIInput.Validation.Username || __instance.validation == UIInput.Validation.Name))
            {
                var cat = char.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format || cat == UnicodeCategory.OtherNotAssigned)
                    __result = '\0';
                else
                    __result = ch;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KAEditBox), "ValidateText", typeof(string), typeof(int), typeof(char))]
    static class Patch_CanInput2
    {
        static bool Prefix(KAEditBox __instance, string text, int charIndex, char addedChar, ref char __result)
        {
            if (Main.MoreNameFreedom && (__instance._CheckValidityOnInput && __instance._RegularExpression != null && __instance._RegularExpression.Contains("a-z")))
            {
                var cat = char.GetUnicodeCategory(addedChar);
                if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format || cat == UnicodeCategory.OtherNotAssigned)
                    __result = '\0';
                else
                    __result = addedChar;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UiAvatarControls), "Update")]
    static class Patch_ControlsUpdate
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var flag = false;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo m && flag)
                {
                    if (m.Name == "GetButtonDown")
                        code[i] = new CodeInstruction(OpCodes.Call, typeof(Patch_ControlsUpdate).GetMethod(nameof(ButtonDown), ~BindingFlags.Default));
                    else if (m.Name == "GetButtonUp")
                        code[i] = new CodeInstruction(OpCodes.Call, typeof(Patch_ControlsUpdate).GetMethod(nameof(ButtonUp), ~BindingFlags.Default));
                    flag = false;
                }
                else if (code[i].operand is string str)
                    flag = str == "DragonFire";
            return code;
        }
        static bool ButtonDown(string button) => Main.AutomaticFireballs ? KAInput.GetButton(button) : KAInput.GetButtonDown(button);
        static bool ButtonUp(string button) => Main.AutomaticFireballs ? KAInput.GetButton(button) : KAInput.GetButtonUp(button);
    }

    [HarmonyPatch(typeof(RacingManager),"AddPenalty")]
    static class Patch_AddRacingCooldown
    {
        public static bool Prefix() => RacingManager.Instance.State >= RacingManagerState.RaceCountdown;
    }

    [HarmonyPatch]
    static class Patch_ColorDragonShot
    {
        static MaterialPropertyBlock props = new MaterialPropertyBlock();
        [HarmonyPatch(typeof(ObAmmo),"Activate")]
        [HarmonyPrefix]
        static void ActivateAmmo_Pre(ObAmmo __instance, WeaponManager inManager)
        {
            if (!inManager || !inManager.IsLocal || !(inManager is PetWeaponManager p) || !p.SanctuaryPet)
                return;
            var d = ExtendedPetData.Get(p.SanctuaryPet.pData);
            if (d.FireballColor == null)
                return;
            var color = d.FireballColor.Value;
            Debug.Log($"Changing fireball {__instance.name} to {color.ToHex()}");
            foreach (var r in __instance.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(props);
                foreach (var m in r.sharedMaterials)
                    if (m && m.shader)
                    {
                        var c = m.shader.GetPropertyCount();
                        for (var i = 0; i < c; i++)
                            if (m.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            {
                                var n = m.shader.GetPropertyNameId(i);
                                props.SetColor(n, m.GetColor(n).Shift(color));
                            }
                    }
                r.SetPropertyBlock(props);
            }
            foreach (var ps in __instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                m.startColor = m.startColor.Shift(color);
                var s = ps.colorBySpeed;
                s.color = s.color.Shift(color);
                var l = ps.colorOverLifetime;
                l.color = l.color.Shift(color);
            }
        }
    }

    [HarmonyPatch(typeof(RaisedPetData))]
    static class Patch_PetData
    {
        [HarmonyPatch("ParseResStringEx")]
        static void Postfix(string s, RaisedPetData __instance)
        {
            foreach (var i in s.Split('*'))
            {
                var values = i.Split('$');
                if (values.Length >= 2 && values[0] == ExtendedPetData.FIREBALLCOLOR_KEY && values[1].TryParseColor(out var color))
                    ExtendedPetData.Get(__instance).FireballColor = color;
            }
        }
        [HarmonyPatch("SaveToResStringEx")]
        static void Postfix(RaisedPetData __instance, ref string __result)
        {
            var d = ExtendedPetData.Get(__instance);
            if (d.FireballColor != null)
            __result += ExtendedPetData.FIREBALLCOLOR_KEY + "$" + d.FireballColor.Value.ToHex() + "*";
        }
    }

    public class ExtendedPetData
    {
        static ConditionalWeakTable<RaisedPetData, ExtendedPetData> table = new ConditionalWeakTable<RaisedPetData, ExtendedPetData>();
        public static ExtendedPetData Get(RaisedPetData data) => table.GetOrCreateValue(data);

        public const string FIREBALLCOLOR_KEY = "HTFC"; // Handy Tweaks Fireball Colour
        public Color? FireballColor;
    }

    [HarmonyPatch(typeof(UiSelectName),"OnClick")]
    static class Patch_SelectName
    {
        public static bool SkipNameChecks = false;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var lbl = iL.DefineLabel();
            code[code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo m && m.Name == "get_Independent"), x => x.opcode == OpCodes.Ldarg_0)].labels.Add(lbl);
            code.InsertRange(code.FindIndex(x => x.opcode == OpCodes.Stloc_0) + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Patch_SelectName),nameof(SkipNameChecks))),
                new CodeInstruction(OpCodes.Brtrue,lbl)
            });
            return code;
        }
    }

    [HarmonyPatch(typeof(SanctuaryPet), "PetMoodParticleAllowed")]
    static class Patch_MoodParticleAllowed
    {
        static void Postfix(SanctuaryPet __instance, ref bool __result)
        {
            var n = __instance.GetTypeInfo()._Name;
            if (Main.DisableHappyParticles.TryGetValue(n,out var v))
            {
                if (v)
                    __result = false;
            }
            else
            {
                Main.DisableHappyParticles[n] = false;
                Main.instance.Config.Save();
            }
        }
    }

    [HarmonyPatch(typeof(AvAvatarController), "ShowArmorWing")]
    static class Patch_ArmorWingsVisible
    {
        static void Prefix(ref bool show)
        {
            if (Main.AlwaysShowArmourWings)
                show = true;
        }
    }

    [HarmonyPatch(typeof(UiDragonCustomization),"SetColorSelector")]
    static class Patch_DragonCustomization
    {
        static void Postfix(UiDragonCustomization __instance)
        {
            if (Main.DisableCustomColourPicker)
                return;
            if (__instance.mIsUsedInJournal && !__instance.mFreeCustomization && CommonInventoryData.pInstance.GetQuantity(__instance.mUiJournalCustomization._DragonTicketItemID) <= 0)
                return;
            var ui = ColorPicker.OpenUI((x) =>
            {
                if (__instance.mSelectedColorBtn == __instance.mPrimaryColorBtn)
                    __instance.mPrimaryColor = x;
                else if (__instance.mSelectedColorBtn == __instance.mSecondaryColorBtn)
                    __instance.mSecondaryColor = x;
                else if (__instance.mSelectedColorBtn == __instance.mTertiaryColorBtn)
                    __instance.mTertiaryColor = x;
                __instance.mSelectedColorBtn.pBackground.color = x;
                __instance.mRebuildTexture = true;
                __instance.RemoveDragonSkin();
                __instance.mIsResetAvailable = true;
                __instance.RefreshResetBtn();
                __instance.mMenu.mModified = true;
            });
            ui.Requires = () => __instance && __instance.isActiveAndEnabled && __instance.GetVisibility();
            ui.current = __instance.mSelectedColorBtn.pBackground.color;
        }
    }

    [HarmonyPatch(typeof(UiAvatarCustomization), "SetSkinColorPickersVisibility")]
    static class Patch_AvatarCustomization
    {
        static void Postfix(UiAvatarCustomization __instance)
        {
            if (Main.DisableCustomColourPicker)
                return;
            var ui = ColorPicker.OpenUI((x) =>
            {
                __instance.mSelectedColorBtn.pBackground.color = x;
                __instance.SetColor(x);
            });
            ui.Requires = () =>
                __instance
                && __instance.isActiveAndEnabled
                && __instance.GetVisibility()
                && (
                    (__instance.pColorPalette && __instance.pColorPalette.GetVisibility())
                    ||
                    (__instance.mSkinColorPalette && __instance.mSkinColorPalette.GetVisibility())
                );
            ui.current = __instance.mSelectedColorBtn.pBackground.color;
        }
    }

    public class ColorPicker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Display;
        public (Slider slider, InputField input) Red;
        public (Slider slider, InputField input) Green;
        public (Slider slider, InputField input) Blue;
        (Slider slider, InputField input) this[int channel] => channel == 0 ? Red : channel == 1 ? Green : channel == 2 ? Blue : default;
        public Button Close;
        public event Action<Color> OnChange;
        public event Action OnClose;
        public Func<bool> Requires;

        KAUI handle;
        Color _c;
        public Color current
        {
            get => _c;
            set
            {
                updating = true;
                _c = value;
                for (int i = 0; i < 3; i++)
                {
                    this[i].slider.value = _c[i];
                    this[i].input.text = Math.Round(_c[i] * 255).ToString();
                }
                Display.color = _c;
                updating = false;
                OnChange?.Invoke(_c);
            }
        }
        T GetComponent<T>(string path) where T : Component => transform.Find(path).GetComponent<T>();
        void Awake()
        {
            handle = gameObject.AddComponent<KAUI>();

            Display = GetComponent<Image>("ColorView");
            Red = (GetComponent<Slider>("RInputs/Slider"), GetComponent<InputField>("RInputs/Input"));
            Green = (GetComponent<Slider>("GInputs/Slider"), GetComponent<InputField>("GInputs/Input"));
            Blue = (GetComponent<Slider>("BInputs/Slider"), GetComponent<InputField>("BInputs/Input"));
            Close = GetComponent<Button>("CloseButton");

            Close.onClick.AddListener(CloseUI);
            Red.slider.onValueChanged.AddListener((x) => UpdateColor(0, x, false));
            Green.slider.onValueChanged.AddListener((x) => UpdateColor(1, x, false));
            Blue.slider.onValueChanged.AddListener((x) => UpdateColor(2, x, false));
            Red.input.onValueChanged.AddListener((x) => UpdateColor(0, int.TryParse(x, out var v) ? v / 255f : 0, text: false));
            Green.input.onValueChanged.AddListener((x) => UpdateColor(1, int.TryParse(x, out var v) ? v / 255f : 0, text: false));
            Blue.input.onValueChanged.AddListener((x) => UpdateColor(2, int.TryParse(x, out var v) ? v / 255f : 0, text: false));
        }
        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            KAUI.SetExclusive(handle);
        }
        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            KAUI.RemoveExclusive(handle);
        }
        void CloseUI()
        {
            Destroy(GetComponentInParent<Canvas>().gameObject);
        }
        void OnDestroy()
        {
            KAUI.RemoveExclusive(GetComponent<KAUI>());
            OnClose?.Invoke();
        }
        bool updating = false;
        void UpdateColor(byte channel, float newValue, bool slider = true, bool text = true)
        {
            if (updating)
                return;
            updating = true;
            if (slider)
                this[channel].slider.value = newValue;
            if (text)
                this[channel].input.text = Math.Round(newValue * 255).ToString();
            _c[channel] = newValue;
            Display.color = _c;
            updating = false;
            OnChange?.Invoke(_c);
        }
        void Update()
        {
            if (Input.GetKeyUp(KeyCode.Escape) && KAUI._GlobalExclusiveUI == handle)
                CloseUI();
            if (Requires != null && !Requires())
                CloseUI();
        }

        public static GameObject UIPrefab;
        static ColorPicker open;
        public static ColorPicker OpenUI(Action<Color> onChange = null, Action onClose = null)
        {
            if (!open)
                open = Instantiate(UIPrefab).transform.Find("Picker").gameObject.AddComponent<ColorPicker>();
            open.OnChange = onChange;
            open.OnClose = onClose;
            return open;
        } 
    }
}