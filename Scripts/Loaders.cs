using Anvil;
using FistVR;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using Valve.VR.InteractionSystem;
using Stratum.Extensions;
using Stratum;
using System.Reflection;

namespace OtherLoader
{
    public class ItemLoader
    {

        //Anatomy of a BundleID
        // [Mod Path] : [Bundle Name]
        // Combining these two gives you the path to the asset bundle


        public Empty LoadAssembly(FileSystemInfo handle)
        {
            OtherLogger.Log("Loading Assembly: " + handle.FullName, OtherLogger.LogType.Loading);
            Assembly.LoadFile(handle.FullName);
            return new Empty();
        }

        //Immediate Loaders
        public IEnumerator StartAssetLoadUnordered(FileSystemInfo handle)
        {
            return StartAssetLoad(handle, LoadOrderType.LoadUnordered, true);
        }

        public IEnumerator StartAssetLoadFirst(FileSystemInfo handle)
        {
            return StartAssetLoad(handle, LoadOrderType.LoadFirst, true);
        }

        public IEnumerator StartAssetLoadLast(FileSystemInfo handle)
        {
            return StartAssetLoad(handle, LoadOrderType.LoadLast, true);
        }

        //On-Demand Loaders
        public IEnumerator StartAssetDataLoad(FileSystemInfo handle)
        {
            return StartAssetLoad(handle, LoadOrderType.LoadFirst, false);
        }

        public IEnumerator RegisterAssetLoadFirstLate(FileSystemInfo handle)
        {
            return RegisterAssetLoadLate(handle, LoadOrderType.LoadFirst);
        }

        public IEnumerator RegisterAssetLoadUnorderedLate(FileSystemInfo handle)
        {
            return RegisterAssetLoadLate(handle, LoadOrderType.LoadUnordered);
        }

        public IEnumerator RegisterAssetLoadLastLate(FileSystemInfo handle)
        {
            return RegisterAssetLoadLate(handle, LoadOrderType.LoadLast);
        }

        public void LoadDirectAssets(CoroutineStarter starter, string folderPath, string guid, string[] dependancies, string[] loadFirst, string[] loadAny, string[] loadLast)
        {
            foreach (string bundleFirst in loadFirst)
            {
                if (!string.IsNullOrEmpty(bundleFirst))
                {
                    starter(StartAssetLoadDirect(folderPath, bundleFirst, guid, dependancies, LoadOrderType.LoadFirst, false));
                }
                
            }

            foreach (string bundleAny in loadAny)
            {
                if (!string.IsNullOrEmpty(bundleAny))
                {
                    starter(StartAssetLoadDirect(folderPath, bundleAny, guid, dependancies, LoadOrderType.LoadUnordered, false));
                }
            }

            foreach (string bundleLast in loadLast)
            {
                if (!string.IsNullOrEmpty(bundleLast))
                {
                    starter(StartAssetLoadDirect(folderPath, bundleLast, guid, dependancies, LoadOrderType.LoadLast, false));
                }
            }
        }

        public IEnumerator StartAssetLoad(FileSystemInfo handle, LoadOrderType loadOrder, bool allowUnload)
        {
            FileInfo file = handle.ConsumeFile();

            string bundleID = file.FullName.Replace(file.Name, "") + " : " + file.Name;

            return LoadAssetsFromPathAsync(file.FullName, bundleID, "", new string[] { }, loadOrder, allowUnload).TryCatch(e =>
            {
                OtherLogger.LogError("Failed to load mod (" + bundleID + ")");
                OtherLogger.LogError(e.ToString());
                LoaderStatus.UpdateProgress(bundleID, 1);
                LoaderStatus.RemoveActiveLoader(bundleID, true);
            });
        }


        public IEnumerator StartAssetLoadDirect(string folderPath, string bundleName, string guid, string[] dependancies, LoadOrderType loadOrder, bool allowUnload)
        {
            OtherLogger.Log("Direct Loading Bundle (" + bundleName + ")", OtherLogger.LogType.General);

            string bundlePath = Path.Combine(folderPath, bundleName);
            string lateName = "late_" + bundleName;
            string latePath = Path.Combine(folderPath, lateName);
            string bundleID = bundlePath.Replace(bundleName, "") + " : " + bundleName;
            IEnumerator afterLoad = null;

            if (File.Exists(latePath))
            {
                afterLoad = RegisterAssetLoadLate(latePath, lateName, loadOrder);
            }

            return LoadAssetsFromPathAsync(bundlePath, bundleID, guid, dependancies, loadOrder, allowUnload, afterLoad).TryCatch(e =>
            {
                OtherLogger.LogError("Failed to load mod (" + bundleID + ")");
                OtherLogger.LogError(e.ToString());
                LoaderStatus.UpdateProgress(bundleID, 1);
                LoaderStatus.RemoveActiveLoader(bundleID, true);
            });
        }


        public IEnumerator RegisterAssetLoadLate(FileSystemInfo handle, LoadOrderType loadOrder)
        {
            FileInfo file = handle.ConsumeFile();

            return RegisterAssetLoadLate(file.FullName, file.Name, loadOrder);
        }


        public IEnumerator RegisterAssetLoadLate(string bundlePath, string bundleName, LoadOrderType loadOrder)
        {
            //In order to get this bundle to load later, we want to replace the file path for the already loaded FVRObject
            string bundleID = bundlePath.Replace(bundleName, "") + " : " + bundleName.Replace("late_", "");
            OtherLoader.ManagedBundles[bundleID] = bundlePath;
            LoaderStatus.TrackLoader(bundleID, loadOrder);

            AnvilCallbackBase anvilCallbackBase;
            if (AnvilManager.m_bundles.TryGetValue(bundleID, out anvilCallbackBase))
            {
                AnvilManager.m_bundles.m_lookup.Remove(bundleID);
                AnvilManager.m_bundles.m_loading.Remove(anvilCallbackBase);

                if (OtherLoader.LogLoading.Value)
                {
                    OtherLogger.Log("Registered asset bundle to load later (" + bundlePath + ")", OtherLogger.LogType.General);
                    OtherLogger.Log("This bundle will replace the data bundle (" + bundleID + ")", OtherLogger.LogType.Loading);
                }
                else
                {
                    OtherLogger.Log("Registered asset bundle to load later (" + bundleName + ")", OtherLogger.LogType.General);
                    OtherLogger.Log("This bundle will replace the data bundle (" + LoaderUtils.GetBundleNameFromUniqueID(bundleID) + ")", OtherLogger.LogType.Loading);
                }
            }
            else
            {
                OtherLogger.LogError("Tried to register bundle to load later, but pre-bundle had not yet been loaded! (" + bundleID + ")");
            }

            yield return null;
        }


        public void LoadLegacyAssets(CoroutineStarter starter)
        {
            if (!Directory.Exists(OtherLoader.MainLegacyDirectory)) Directory.CreateDirectory(OtherLoader.MainLegacyDirectory);

            OtherLogger.Log("Plugins folder found (" + Paths.PluginPath + ")", OtherLogger.LogType.General);

            List<string> legacyPaths = Directory.GetDirectories(Paths.PluginPath, "LegacyVirtualObjects", SearchOption.AllDirectories).ToList();
            legacyPaths.Add(OtherLoader.MainLegacyDirectory);

            foreach(string legacyPath in legacyPaths)
            {
                OtherLogger.Log("Legacy folder found (" + legacyPath + ")", OtherLogger.LogType.General);

                foreach (string bundlePath in Directory.GetFiles(legacyPath, "*", SearchOption.AllDirectories))
                {
                    //Only allow files without file extensions to be loaded (assumed to be an asset bundle)
                    if (Path.GetFileName(bundlePath) != Path.GetFileNameWithoutExtension(bundlePath))
                    {
                        continue;
                    }

                    string bundleID = bundlePath.Replace(Path.GetFileName(bundlePath), "") + " : " + Path.GetFileName(bundlePath);

                    IEnumerator routine = LoadAssetsFromPathAsync(bundlePath, bundleID, "", new string[] { }, LoadOrderType.LoadUnordered, true).TryCatch<Exception>(e =>
                    {
                        OtherLogger.LogError("Failed to load mod (" + bundleID + ")");
                        OtherLogger.LogError(e.ToString());
                        LoaderStatus.UpdateProgress(bundleID, 1);
                        LoaderStatus.RemoveActiveLoader(bundleID, true);
                    });

                    starter(routine);
                }
            }
        }


        


        private IEnumerator LoadAssetsFromPathAsync(string path, string bundleID, string guid, string[] dependancies, LoadOrderType loadOrder, bool allowUnload, IEnumerator afterLoad = null)
        {
            //Start tracking this bundle and then wait a frame for everything else to be tracked
            LoaderStatus.TrackLoader(bundleID, loadOrder);
            yield return null;

            //If there are many active loaders at once, we should wait our turn
            while (!LoaderStatus.CanOrderedModLoad(bundleID))
            {
                yield return null;
            }

            //Begin the loading process
            LoaderStatus.AddActiveLoader(bundleID);

            if (OtherLoader.LogLoading.Value)
                OtherLogger.Log("Beginning async loading of asset bundle (" + bundleID + ")", OtherLogger.LogType.General);
            //else
                //OtherLogger.Log("Beginning async loading of asset bundle (" + LoaderUtils.GetBundleNameFromUniqueID(bundleID) + ")", OtherLogger.LogType.General);


            //Load the bundle and apply it's contents
            float time = Time.time;
            LoaderStatus.UpdateProgress(bundleID, UnityEngine.Random.Range(.1f, .3f));

            AnvilCallback<AssetBundle> bundle = LoaderUtils.LoadAssetBundle(path);
            yield return bundle;

            LoaderStatus.UpdateProgress(bundleID, 0.9f);

            yield return ApplyLoadedAssetBundleAsync(bundle, bundleID).TryCatch(e =>
            {
                OtherLogger.LogError("Failed to load mod (" + bundleID + ")");
                OtherLogger.LogError(e.ToString());
                LoaderStatus.UpdateProgress(bundleID, 1);
                LoaderStatus.RemoveActiveLoader(bundleID, true);
            });

            
            //Log that the bundle is loaded
            if (OtherLoader.LogLoading.Value)
                OtherLogger.Log($"[{(Time.time - time).ToString("0.000")} s] Completed loading bundle ({bundleID})", OtherLogger.LogType.General);
            else
                OtherLogger.Log($"[{(Time.time - time).ToString("0.000")} s] Completed loading bundle ({LoaderUtils.GetBundleNameFromUniqueID(bundleID)})", OtherLogger.LogType.General);



            if (allowUnload && OtherLoader.OptimizeMemory.Value)
            {
                OtherLogger.Log("Unloading asset bundle (Optimize Memory is true)", OtherLogger.LogType.Loading);
                bundle.Result.Unload(false);
            }
            else
            {
                AnvilManager.m_bundles.Add(bundleID, bundle);
            }

            OtherLoader.ManagedBundles.Add(bundleID, path);
            LoaderStatus.UpdateProgress(bundleID, 1);
            LoaderStatus.RemoveActiveLoader(bundleID, !(OtherLoader.OptimizeMemory.Value && allowUnload));

            if(afterLoad != null)
            {
                yield return afterLoad;
            }
        }




        private IEnumerator ApplyLoadedAssetBundleAsync(AnvilCallback<AssetBundle> bundle, string bundleID)
        {
            //Load the mechanical accuracy entries
            AssetBundleRequest accuracyCharts = bundle.Result.LoadAllAssetsAsync<FVRFireArmMechanicalAccuracyChart>();
            yield return accuracyCharts;
            LoadMechanicalAccuracyEntries(accuracyCharts.allAssets);

            //Load all the FVRObjects
            AssetBundleRequest fvrObjects = bundle.Result.LoadAllAssetsAsync<FVRObject>();
            yield return fvrObjects;
            LoadFVRObjects(bundleID, fvrObjects.allAssets);

            //Now all the FVRObjects are loaded, we can load the bullet data
            AssetBundleRequest bulletData = bundle.Result.LoadAllAssetsAsync<FVRFireArmRoundDisplayData>();
            yield return bulletData;
            LoadBulletData(bulletData.allAssets);

            //Before we load the spawnerIDs, we must add any new spawner category definitions
            AssetBundleRequest spawnerCats = bundle.Result.LoadAllAssetsAsync<ItemSpawnerCategoryDefinitions>();
            yield return spawnerCats;
            LoadSpawnerCategories(spawnerCats.allAssets);

            //Load the legacy spawner IDs
            AssetBundleRequest spawnerIDs = bundle.Result.LoadAllAssetsAsync<ItemSpawnerID>();
            yield return spawnerIDs;
            LoadSpawnerIDs(spawnerIDs.allAssets);

            //Load the spawner entries for the new spawner
            AssetBundleRequest spawnerEntries = bundle.Result.LoadAllAssetsAsync<ItemSpawnerEntry>();
            yield return spawnerEntries;
            LoadSpawnerEntries(spawnerEntries.allAssets);

            //handle handling grab/release/slot sets
            AssetBundleRequest HandlingGrabSet = bundle.Result.LoadAllAssetsAsync<HandlingGrabSet>();
            yield return HandlingGrabSet;
            LoadHandlingGrabSetEntries(HandlingGrabSet.allAssets);

            AssetBundleRequest HandlingReleaseSet = bundle.Result.LoadAllAssetsAsync<HandlingReleaseSet>();
            yield return HandlingReleaseSet;
            LoadHandlingReleaseSetEntries(HandlingReleaseSet.allAssets);

            AssetBundleRequest HandlingSlotSet = bundle.Result.LoadAllAssetsAsync<HandlingReleaseIntoSlotSet>();
            yield return HandlingSlotSet;
            LoadHandlingSlotSetEntries(HandlingSlotSet.allAssets);

            //audio bullet impact sets; handled similarly to the ones above
            AssetBundleRequest BulletImpactSet = bundle.Result.LoadAllAssetsAsync<AudioBulletImpactSet>();
            yield return BulletImpactSet;
            LoadImpactSetEntries(BulletImpactSet.allAssets);

            AssetBundleRequest AudioImpactSet = bundle.Result.LoadAllAssetsAsync<AudioImpactSet>();
            yield return AudioImpactSet;
            LoadAudioImpactSetEntries(AudioImpactSet.allAssets);

            AssetBundleRequest Quickbelts = bundle.Result.LoadAllAssetsAsync<GameObject>();
            yield return Quickbelts;
            LoadQuickbeltEntries(Quickbelts.allAssets);

        }



        private void LoadSpawnerEntries(UnityEngine.Object[] allAssets)
        { //nothing fancy; just dumps them into the lists above and logs it
            foreach (ItemSpawnerEntry entry in allAssets)
            {
                OtherLogger.Log("Loading new item spawner entry: " + entry.EntryPath, OtherLogger.LogType.Loading);
                entry.IsModded = true;
                PopulateEntryPaths(entry);
            }
        }

        private void LoadHandlingGrabSetEntries(UnityEngine.Object[] allAssets)
        { //nothing fancy; just dumps them into the lists above and logs it
            foreach (HandlingGrabSet grabSet in allAssets)
            {
                OtherLogger.Log("Loading new handling grab set entry: " + grabSet.name, OtherLogger.LogType.Loading);
                ManagerSingleton<SM>.Instance.m_handlingGrabDic.Add(grabSet.Type, grabSet);
            }
        }

        private void LoadHandlingReleaseSetEntries(UnityEngine.Object[] allAssets)
        {
            foreach (HandlingReleaseSet releaseSet in allAssets)
            {
                OtherLogger.Log("Loading new handling release set entry: " + releaseSet.name, OtherLogger.LogType.Loading);
                ManagerSingleton<SM>.Instance.m_handlingReleaseDic.Add(releaseSet.Type, releaseSet);
            }
        }

        private void LoadHandlingSlotSetEntries(UnityEngine.Object[] allAssets)
        {
            foreach (HandlingReleaseIntoSlotSet slotSet in allAssets)
            {
                OtherLogger.Log("Loading new handling QB slot set entry: " + slotSet.name, OtherLogger.LogType.Loading);
                ManagerSingleton<SM>.Instance.m_handlingReleaseIntoSlotDic.Add(slotSet.Type, slotSet);
            }
        }

        private void LoadImpactSetEntries(UnityEngine.Object[] allAssets)
        {
            foreach (AudioBulletImpactSet impactSet in allAssets)
            {
                OtherLogger.Log("Loading new bullet impact set entry: " + impactSet.name, OtherLogger.LogType.Loading);
                //this is probably the stupidest workaround, but it works and it's short. it just adds impactset to the impact sets
                ManagerSingleton<SM>.Instance.AudioBulletImpactSets.Concat(new AudioBulletImpactSet[] {impactSet});
                ManagerSingleton<SM>.Instance.m_bulletHitDic.Add(impactSet.Type, impactSet);
            }
        }

        private void LoadAudioImpactSetEntries(UnityEngine.Object[] allAssets)
        {
            foreach (AudioImpactSet AIS in allAssets)
            {
                //resize SM's AIS list to its length + 1, insert AIS into list
                OtherLogger.Log("Loading new Audio Impact Set: " + AIS.name, OtherLogger.LogType.Loading);
                Array.Resize(ref ManagerSingleton<SM>.Instance.AudioImpactSets, ManagerSingleton<SM>.Instance.AudioImpactSets.Length + 1);
                ManagerSingleton<SM>.Instance.AudioImpactSets[ManagerSingleton<SM>.Instance.AudioImpactSets.Length - 1] = AIS;
                //clears impactdic
                ManagerSingleton<SM>.Instance.m_impactDic = new Dictionary<ImpactType, Dictionary<MatSoundType, Dictionary<AudioImpactIntensity, AudioEvent>>>();
                //remakes impactdic
                ManagerSingleton<SM>.Instance.generateImpactDictionary(); //TODO: this is an inefficient method. pls dont remake
                //the dictionary every time a new one is added! oh well. it works
            }
        }

        private void LoadQuickbeltEntries(UnityEngine.Object[] allAssets)
        {
            foreach (GameObject quickbelt in allAssets)
            {
                string[] QBnameSplit = quickbelt.name.Split('_');
                if (QBnameSplit.Length > 1)
                {
                    if (QBnameSplit[QBnameSplit.Length - 2] == "QuickBelt")
                    {
                        OtherLogger.Log("Adding QuickBelt " + quickbelt.name, OtherLogger.LogType.Loading);
                        Array.Resize(ref GM.Instance.QuickbeltConfigurations, GM.Instance.QuickbeltConfigurations.Length + 1);
                        GM.Instance.QuickbeltConfigurations[GM.Instance.QuickbeltConfigurations.Length - 1] = quickbelt;
                    }
                }
            }
        }


        private void LoadMechanicalAccuracyEntries(UnityEngine.Object[] allAssets)
        {
            foreach(FVRFireArmMechanicalAccuracyChart chart in allAssets)
            {
                foreach(FVRFireArmMechanicalAccuracyChart.MechanicalAccuracyEntry entry in chart.Entries)
                {
                    OtherLogger.Log("Loading new mechanical accuracy entry: " + entry.Class, OtherLogger.LogType.Loading);

                    if (!AM.SMechanicalAccuracyDic.ContainsKey(entry.Class)){
                        AM.SMechanicalAccuracyDic.Add(entry.Class, entry);
                    }
                    else
                    {
                        OtherLogger.LogError("Duplicate mechanical accuracy class found, will not use one of them! Make sure you're using unique mechanical accuracy classes!");
                    }
                }
            }
        }


        private void LoadSpawnerCategories(UnityEngine.Object[] allAssets)
        {
            foreach (ItemSpawnerCategoryDefinitions newLoadedCats in allAssets)
            {
                foreach (ItemSpawnerCategoryDefinitions.Category newCategory in newLoadedCats.Categories)
                {
                    OtherLogger.Log("Loading New ItemSpawner Category! Name (" + newCategory.DisplayName + "), Value (" + newCategory.Cat + ")", OtherLogger.LogType.Loading);


                    //If the loaded categories already contains this new category, we want to add subcategories
                    if (IM.CDefs.Categories.Any(o => o.Cat == newCategory.Cat))
                    {
                        OtherLogger.Log("Category already exists! Adding subcategories", OtherLogger.LogType.Loading);

                        foreach (ItemSpawnerCategoryDefinitions.Category currentCat in IM.CDefs.Categories)
                        {
                            if(currentCat.Cat == newCategory.Cat)
                            {
                                foreach(ItemSpawnerCategoryDefinitions.SubCategory newSubCat in newCategory.Subcats)
                                {
                                    //Only add this new subcategory if it is unique
                                    if(!IM.CDefSubInfo.ContainsKey(newSubCat.Subcat))
                                    {
                                        OtherLogger.Log("Adding subcategory: " + newSubCat.DisplayName, OtherLogger.LogType.Loading);

                                        List<ItemSpawnerCategoryDefinitions.SubCategory> currSubCatList = currentCat.Subcats.ToList();
                                        currSubCatList.Add(newSubCat);
                                        currentCat.Subcats = currSubCatList.ToArray();

                                        IM.CDefSubs[currentCat.Cat].Add(newSubCat);

                                        if (!IM.CDefSubInfo.ContainsKey(newSubCat.Subcat)) IM.CDefSubInfo.Add(newSubCat.Subcat, newSubCat);
                                        if (!IM.SCD.ContainsKey(newSubCat.Subcat)) IM.SCD.Add(newSubCat.Subcat, new List<ItemSpawnerID>());
                                    }

                                    else
                                    {
                                        OtherLogger.LogError("SubCategory type is already being used, and SubCategory will not be added! Make sure your subcategory is using a unique type! SubCategory Type: " + newSubCat.Subcat);
                                    }
                                }
                            }
                        }
                    }

                    //If new category, we can just add the whole thing
                    else
                    {
                        OtherLogger.Log("This is a new primary category", OtherLogger.LogType.Loading);

                        List<ItemSpawnerCategoryDefinitions.Category> currentCatsList = IM.CDefs.Categories.ToList();
                        currentCatsList.Add(newCategory);
                        IM.CDefs.Categories = currentCatsList.ToArray();

                        OtherLogger.Log("Num CDefs: " + IM.CDefs.Categories.Length, OtherLogger.LogType.Loading);

                        if (!IM.CDefSubs.ContainsKey(newCategory.Cat)) IM.CDefSubs.Add(newCategory.Cat, new List<ItemSpawnerCategoryDefinitions.SubCategory>());
                        if (!IM.CDefInfo.ContainsKey(newCategory.Cat)) IM.CDefInfo.Add(newCategory.Cat, newCategory);
                        if (!IM.CD.ContainsKey(newCategory.Cat)) IM.CD.Add(newCategory.Cat, new List<ItemSpawnerID>());

                        foreach(ItemSpawnerCategoryDefinitions.SubCategory newSubCat in newCategory.Subcats)
                        {
                            IM.CDefSubs[newCategory.Cat].Add(newSubCat);

                            if (!IM.CDefSubInfo.ContainsKey(newSubCat.Subcat)) IM.CDefSubInfo.Add(newSubCat.Subcat, newSubCat);
                            if (!IM.SCD.ContainsKey(newSubCat.Subcat)) IM.SCD.Add(newSubCat.Subcat, new List<ItemSpawnerID>());
                        }
                    }
                }
            }
        }


        private void LoadSpawnerIDs(UnityEngine.Object[] allAssets)
        {
            foreach (ItemSpawnerID id in allAssets)
            {
                OtherLogger.Log("Adding Itemspawner ID! Category: " + id.Category + ", SubCategory: " + id.SubCategory, OtherLogger.LogType.Loading);

                //Try to set the main object of this ID as a secondary if the main is null (so that it gets tagged properly)
                if (id.MainObject == null && id.Secondaries.Length > 0)
                {
                    id.MainObject = id.Secondaries.Select(o => o.MainObject).FirstOrDefault(o => o != null);
                    if (id.MainObject != null)
                    {
                        id.ItemID = id.MainObject.ItemID;
                    }
                    else
                    {
                        OtherLogger.Log("Could not select a secondary object for ItemSpawnerID, it will not appear in spawner: Display Name: " + id.DisplayName, OtherLogger.LogType.Loading);
                    }
                }


                if(id.MainObject != null)
                {
                    if (id.UnlockCost == 0) id.UnlockCost = id.MainObject.CreditCost;

                    IM.RegisterItemIntoMetaTagSystem(id);
                    if (!id.IsDisplayedInMainEntry) HideItemFromCategories(id);
                }

                
                
                if (IM.CD.ContainsKey(id.Category) && IM.SCD.ContainsKey(id.SubCategory)) {
                    IM.CD[id.Category].Add(id);
                    IM.SCD[id.SubCategory].Add(id);

                    if (!ManagerSingleton<IM>.Instance.SpawnerIDDic.ContainsKey(id.ItemID))
                    {
                        ManagerSingleton<IM>.Instance.SpawnerIDDic[id.ItemID] = id;

                        
                        //Add this spawner ID to our entry tree structure
                        if (Enum.IsDefined(typeof(ItemSpawnerID.EItemCategory), id.Category))
                        {
                            //TODO this should be done without having to loop through potentially all spawner entries, I bet this could become expensive
                            foreach (KeyValuePair<ItemSpawnerV2.PageMode, List<string>> pageItems in IM.Instance.PageItemLists)
                            {
                                if (pageItems.Value.Contains(id.ItemID))
                                {
                                    OtherLogger.Log("Adding SpawnerID to spawner entry tree", OtherLogger.LogType.Loading);
                                    ItemSpawnerEntry SpawnerEntry = ScriptableObject.CreateInstance<ItemSpawnerEntry>();
                                    SpawnerEntry.LegacyPopulateFromID(pageItems.Key, id, true);
                                    PopulateEntryPaths(SpawnerEntry, id);
                                    break;
                                }
                            }

                            OtherLogger.Log("Could not add item to new spawner because it was not tagged properly! ItemID: " + id.ItemID, OtherLogger.LogType.Loading);
                        }
                        else
                        {
                            OtherLogger.Log("Adding SpawnerID to spawner entry tree under custom category", OtherLogger.LogType.Loading);
                            ItemSpawnerEntry SpawnerEntry = ScriptableObject.CreateInstance<ItemSpawnerEntry>();
                            SpawnerEntry.LegacyPopulateFromID(ItemSpawnerV2.PageMode.Firearms, id, true);
                            PopulateEntryPaths(SpawnerEntry, id);
                        }
                    }
                }

                else
                {
                    OtherLogger.LogError("ItemSpawnerID could not be added, because either the main category or subcategory were not loaded! Item will not appear in the itemspawner! Item Display Name: " + id.DisplayName);
                }
            }
        }


        private void HideItemFromCategories(ItemSpawnerID ID)
        {
            foreach(List<string> pageItems in IM.Instance.PageItemLists.Values)
            {
                pageItems.Remove(ID.MainObject.ItemID);
            }
        }



        private void LoadFVRObjects(string bundleID, UnityEngine.Object[] allAssets)
        {
            foreach (FVRObject item in allAssets)
            {
                if (item == null) continue;

                OtherLogger.Log("Loading FVRObject: " + item.ItemID, OtherLogger.LogType.Loading);

                if (IM.OD.ContainsKey(item.ItemID))
                {
                    OtherLogger.LogError("The ItemID of FVRObject is already used! Item will not be loaded! ItemID: " + item.ItemID);
                    continue;
                }
                item.m_anvilPrefab.Bundle = bundleID;

                if(item.CreditCost == 0) item.CalcCreditCost(); //calculate credit cost if not set
                
                IM.OD.Add(item.ItemID, item);
                ManagerSingleton<IM>.Instance.odicTagCategory.AddOrCreate(item.Category).Add(item);
                ManagerSingleton<IM>.Instance.odicTagFirearmEra.AddOrCreate(item.TagEra).Add(item);
                ManagerSingleton<IM>.Instance.odicTagFirearmSize.AddOrCreate(item.TagFirearmSize).Add(item);
                ManagerSingleton<IM>.Instance.odicTagFirearmAction.AddOrCreate(item.TagFirearmAction).Add(item);
                ManagerSingleton<IM>.Instance.odicTagAttachmentMount.AddOrCreate(item.TagAttachmentMount).Add(item);
                ManagerSingleton<IM>.Instance.odicTagAttachmentFeature.AddOrCreate(item.TagAttachmentFeature).Add(item);
                item.IsModContent = true;

                foreach (FVRObject.OTagFirearmFiringMode mode in item.TagFirearmFiringModes)
                {
                    ManagerSingleton<IM>.Instance.odicTagFirearmFiringMode.AddOrCreate(mode).Add(item);
                }
                foreach (FVRObject.OTagFirearmFeedOption feed in item.TagFirearmFeedOption)
                {
                    ManagerSingleton<IM>.Instance.odicTagFirearmFeedOption.AddOrCreate(feed).Add(item);
                }
                foreach (FVRObject.OTagFirearmMount mount in item.TagFirearmMounts)
                {
                    ManagerSingleton<IM>.Instance.odicTagFirearmMount.AddOrCreate(mount).Add(item);
                }
            }
        }



        private void LoadBulletData(UnityEngine.Object[] allAssets)
        {
            foreach (FVRFireArmRoundDisplayData data in allAssets)
            {
                if (data == null) continue;

                OtherLogger.Log("Loading ammo type: " + data.Type, OtherLogger.LogType.Loading);

                if (!AM.STypeDic.ContainsKey(data.Type))
                {
                    OtherLogger.Log("This is a new ammo type! Adding it to dictionary", OtherLogger.LogType.Loading);
                    AM.STypeDic.Add(data.Type, new Dictionary<FireArmRoundClass, FVRFireArmRoundDisplayData.DisplayDataClass>());
                }
                else
                {
                    OtherLogger.Log("This is an existing ammo type, will add subclasses to this type", OtherLogger.LogType.Loading);
                }

                if (!AM.STypeList.Contains(data.Type))
                {
                    AM.STypeList.Add(data.Type);
                }

                if (!AM.SRoundDisplayDataDic.ContainsKey(data.Type))
                {
                    AM.SRoundDisplayDataDic.Add(data.Type, data);
                }

                //If this Display Data already exists, then we should add our classes to the existing display data class list
                else
                {
                    List<FVRFireArmRoundDisplayData.DisplayDataClass> classes = new List<FVRFireArmRoundDisplayData.DisplayDataClass>(AM.SRoundDisplayDataDic[data.Type].Classes);
                    classes.AddRange(data.Classes);
                    AM.SRoundDisplayDataDic[data.Type].Classes = classes.ToArray();
                }

                if (!AM.STypeClassLists.ContainsKey(data.Type))
                {
                    AM.STypeClassLists.Add(data.Type, new List<FireArmRoundClass>());
                }

                foreach (FVRFireArmRoundDisplayData.DisplayDataClass roundClass in data.Classes)
                {
                    OtherLogger.Log("Loading ammo class: " + roundClass.Class, OtherLogger.LogType.Loading);
                    if (!AM.STypeDic[data.Type].ContainsKey(roundClass.Class))
                    {
                        OtherLogger.Log("This is a new ammo class! Adding it to dictionary", OtherLogger.LogType.Loading);
                        AM.STypeDic[data.Type].Add(roundClass.Class, roundClass);
                    }
                    else
                    {
                        OtherLogger.LogError("Ammo class already exists for bullet type! Bullet will not be loaded! Type: " + data.Type + ", Class: " + roundClass.Class);
                        return;
                    }

                    if (!AM.STypeClassLists[data.Type].Contains(roundClass.Class))
                    {
                        AM.STypeClassLists[data.Type].Add(roundClass.Class);
                    }
                }
            }
        }



        /// <summary>
        /// Converts legacy ItemSpawnerIDs into a new tree based format, and adds this converted info to a global dictionary
        /// </summary>
        /// <param name="Page"></param>
        /// <param name="ID"></param>
        public static void PopulateEntryPaths(ItemSpawnerEntry entry, ItemSpawnerID spawnerID = null)
        {
            string[] pathSegments = entry.EntryPath.Split('/');
            string currentPath = "";

            for(int i = 0; i < pathSegments.Length; i++)
            {
                //If we are at the full path length for this entry, we can just assign the entry
                if(i == pathSegments.Length - 1)
                {
                    EntryNode previousNode = OtherLoader.SpawnerEntriesByPath[currentPath];
                    currentPath += (i == 0 ? "" : "/") + pathSegments[i];

                    //If there is already an node at this path, we should just update it. Otherwise, add it as a new node
                    EntryNode node;
                    if (OtherLoader.SpawnerEntriesByPath.ContainsKey(currentPath))
                    {
                        node = OtherLoader.SpawnerEntriesByPath[currentPath];
                        node.entry = entry;
                    }
                    else
                    {
                        node = new EntryNode(entry);
                        OtherLoader.SpawnerEntriesByPath[currentPath] = node;
                        previousNode.childNodes.Add(node);
                    }

                    if (IM.OD.ContainsKey(entry.MainObjectID))
                    {
                        OtherLoader.SpawnerEntriesByID[entry.MainObjectID] = entry;
                    }
                }


                //If we are at the page level, just check to see if we need to add a page node
                else if(i == 0)
                {
                    currentPath += (i == 0 ? "" : "/") + pathSegments[i];

                    if (!OtherLoader.SpawnerEntriesByPath.ContainsKey(currentPath))
                    {
                        EntryNode pageNode = new EntryNode();
                        pageNode.entry.EntryPath = currentPath;
                        OtherLoader.SpawnerEntriesByPath[currentPath] = pageNode;
                    }
                }

                //If these are just custom categories of any depth, just add the ones that aren't already loaded
                else
                {
                    EntryNode previousNode = OtherLoader.SpawnerEntriesByPath[currentPath];
                    currentPath += (i == 0 ? "" : "/") + pathSegments[i];

                    if (!OtherLoader.SpawnerEntriesByPath.ContainsKey(currentPath))
                    {
                        EntryNode node = new EntryNode();
                        node.entry.EntryPath = currentPath;
                        node.entry.IsDisplayedInMainEntry = true;

                        //Now this section below is for legacy support
                        if(spawnerID != null)
                        {
                            //If this is meatfortress category, do that
                            if (i == 1 && spawnerID.Category == ItemSpawnerID.EItemCategory.MeatFortress)
                            {
                                node.entry.EntryIcon = IM.CDefInfo[ItemSpawnerID.EItemCategory.MeatFortress].Sprite;
                                node.entry.DisplayName = IM.CDefInfo[ItemSpawnerID.EItemCategory.MeatFortress].DisplayName;
                            }

                            //If this is a modded main category, do that
                            else if (i == 1 && !Enum.IsDefined(typeof(ItemSpawnerID.EItemCategory), spawnerID.Category))
                            {
                                if (IM.CDefInfo.ContainsKey(spawnerID.Category))
                                {
                                    node.entry.EntryIcon = IM.CDefInfo[spawnerID.Category].Sprite;
                                    node.entry.DisplayName = IM.CDefInfo[spawnerID.Category].DisplayName;
                                }
                            }

                            //If this is a subcategory (modded or not), do that
                            else if (IM.CDefSubInfo.ContainsKey(spawnerID.SubCategory))
                            {
                                node.entry.EntryIcon = IM.CDefSubInfo[spawnerID.SubCategory].Sprite;
                                node.entry.DisplayName = IM.CDefSubInfo[spawnerID.SubCategory].DisplayName;
                            }

                            node.entry.IsModded = IM.OD[spawnerID.MainObject.ItemID].IsModContent;
                        }
                        
                        previousNode.childNodes.Add(node);
                        OtherLoader.SpawnerEntriesByPath[currentPath] = node;
                    }
                }
            }
        }


    }

}