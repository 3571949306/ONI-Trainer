using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ONIPreviewHelper
{
    public static class Entry
    {
        const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly object s_lock = new object();

        static Assembly s_asmCs;
        static Assembly s_asmFp;
        static Assembly s_asmCore;
        static bool s_isHeadlessWorker;
        static string s_headlessGameDir;
        static Dictionary<string, string> s_headlessPo;
        static readonly object s_poLock = new object();

        static Type s_tWorld;
        static FieldInfo s_fWorldInst;
        static Type s_tGrid;
        static FieldInfo s_fGridW, s_fGridH, s_fGridC, s_fGridWm, s_fGridHm, s_fGridCellSize, s_fGridHalfCellSize;
        static FieldInfo s_fGridElemIdx, s_fGridMass, s_fGridTemp, s_fGridDisIdx, s_fGridDisCnt;
        static MethodInfo s_mGridGetElemIdx, s_mGridGetMass, s_mGridGetTemp, s_mGridGetDisIdx, s_mGridGetDisCnt;
        static PropertyInfo s_pAppIsFocused;
        static Type s_tSelectTool, s_tCellSelObj;
        static FieldInfo s_fSelectToolInst, s_fSelectSelected, s_fSelectCell, s_fCsoSelectedCell, s_fCsoMouseCell;
        static int s_lastTargetCell = -1;
        static MethodInfo s_mGridIsValidCell, s_mGridPosToCell;
        static Type s_tGridSettings;
        static MethodInfo s_mGridReset;
        static Type s_tSettingsCache;
        static FieldInfo s_fScWorlds, s_fScClusters;
        static MethodInfo s_mScGetClusterData, s_mScGetRandomTraits, s_mScGetWorldData;
        static Type s_tTemplateCache;
        static MethodInfo s_mTcInit;
        static PropertyInfo s_pTcInitted;
        static FieldInfo s_fTcInitted;
        static Type s_tWorldGen;
        static FieldInfo s_fWgVoidElem, s_fWgVacuumElem, s_fWgIsStarting;
        static MethodInfo s_mWgLoadSettingsInternal, s_mWgSetupDefaultElems;
        static ConstructorInfo s_cWg4;
        static MethodInfo s_mWgSetSize, s_mWgSetPos, s_mWgSetHiddenY, s_mWgInitialise, s_mWgGenOffline, s_mWgFinalizeStart, s_mWgRenderOffline;
        static PropertyInfo s_pWgData, s_pWgSettings;
        static Type s_tBestFit;
        static MethodInfo s_mBestFitWorlds;
        static Type s_tElementLoader;
        static FieldInfo s_fElements;
        static Type s_tSimData;
        static Type s_tVector2I;
        static ConstructorInfo s_cVector2I;
        static FieldInfo s_fV2ix, s_fV2iy;
        static Type s_tGeyserGenericConfig;
        static MethodInfo s_mGeyserGenConfigs;
        static Type s_tStrings, s_tStringEntry;
        static MethodInfo s_mStringsTryGet;
        static FieldInfo s_fStringEntryVal;
        static Type s_tKRandom;
        static ConstructorInfo s_cKRandomInt;
        static MethodInfo s_mKRandomNext2, s_mKRandomNextDbl;


        static Type s_tGeyserCfg, s_tGeyserInstCfg, s_tGeyserType;
        static MethodInfo s_mGcFindType;
        static FieldInfo s_fGcPresetType, s_fGcPresetMin, s_fGcPresetMax, s_fGcTypesList;
        static FieldInfo s_fGicTypeId, s_fGicRateRoll, s_fGicIterLenRoll, s_fGicIterPctRoll, s_fGicYearLenRoll, s_fGicYearPctRoll;
        static MethodInfo s_mGicInit, s_mGicGetAvg, s_mGicGetEmit, s_mGicGetTemp, s_mGicGetOnDur, s_mGicGetIterLen, s_mGicGetYearOnDur, s_mGicGetYearLen, s_mGicGetType;
        static FieldInfo s_fGtId, s_fGtIdHash, s_fGtMinRate, s_fGtMaxRate, s_fGtMinYearPct, s_fGtMaxYearPct;
        static FieldInfo s_fGtMinIterLen, s_fGtMaxIterLen, s_fGtMinIterPct, s_fGtMaxIterPct, s_fGtMinYearLen, s_fGtMaxYearLen;
        static Type s_tCgs;
        static FieldInfo s_fCgsInstance;
        static MethodInfo s_mCgsGetStoryCode, s_mCgsParseStoryCode, s_mCgsGetMixCode, s_mCgsParseMixCode, s_mCgsGetOtherCode, s_mCgsGetStories, s_mCgsGetActiveWorldMix;
        static Type s_tWorldgenMixing, s_tMutatedLayout;
        static MethodInfo s_mDoWorldMixing;
        static Type s_tAssets, s_tTag, s_tHashedString;
        static MethodInfo s_mAssetsGetPrefab;
        static ConstructorInfo s_cTagStr;
        static Type s_tKCrashReporter, s_tDebug;
        static FieldInfo s_fKcrIgnoreAll, s_fDebugLoggingDisabled;


        static MethodInfo s_mWgGenLayout, s_mWgDrawWorldBorder, s_mWgPlaceTemplateSpawners;
        static FieldInfo s_fWgMyRandom, s_fWgPoiBounds, s_fWgClaimedPoiCells;
        static Type s_tTemplateSpawning, s_tTemplateSpawner, s_tSeededRandom, s_tTemplateContainer, s_tPrefab;
        static MethodInfo s_mTsDetermineTemplates, s_mSimDataInit;
        static ConstructorInfo s_cSeededRandomInt;
        static FieldInfo s_fTspPos, s_fTspContainer, s_fElemIdx, s_fSimDataCells;
        static PropertyInfo s_pTcBuildings, s_pTcOtherEntities, s_pTcPickupables;
        static PropertyInfo s_pPrefabId, s_pPrefabX, s_pPrefabY;
        static object s_cachedSimData;
        static int s_cachedSimDataCells;


        static bool s_rasterInit;
        static FieldInfo s_fWgOverworldCells, s_fPolyVerts, s_fVec2X, s_fVec2Y;
        static PropertyInfo s_pTcPoly, s_pTcNode, s_pNodeType, s_pSubZoneType, s_pPolyBounds, s_pRectXMax, s_pRectYMax;
        static MethodInfo s_mWgsHasSubworld, s_mWgsGetSubWorld;


        sealed class CachedGeyserPool
        {
            public List<GeyserPoolEntry> GenericPool;
            public Dictionary<string, GeyserPoolEntry> AllByPrefab;
            public Dictionary<string, GeyserPoolEntry> AllByRaw;
        }
        static readonly Dictionary<string, CachedGeyserPool> s_geyserPoolCache =
            new Dictionary<string, CachedGeyserPool>(StringComparer.OrdinalIgnoreCase);


        static bool s_inSaveResolved;
        static Type s_tComponents, s_tStudyable, s_tModifiers, s_tAmounts, s_tAttrLevels, s_tTraits;
        static Type s_tAttrMod, s_tPopFX, s_tPlayerCtrl, s_tSimMessages, s_tSaveLoader, s_tUnityObj, s_tSimHashes;
        static FieldInfo s_fCompLiveMinions, s_fCompGeysers, s_fGeyserStudyable;
        static FieldInfo s_fStudyStudied, s_fStudyMarked, s_fModAmounts, s_fModAttrs;
        static FieldInfo s_fTraitList, s_fTraitPositive, s_fAttrModDesc, s_fTraitDesc, s_fDbTraits;
        static FieldInfo s_fPopInst, s_fPopReady, s_fPopSpritePlus, s_fPcInst;
        static PropertyInfo s_pSaveLoaderInst;
        static MethodInfo s_mSaveGetPath, s_mStudyCancel, s_mStudyRefresh, s_mStudyTrigger;
        static MethodInfo s_mGetComponent, s_mAmountsGet, s_mAmtGetMax, s_mAmtSetVal;
        static MethodInfo s_mAttrsGet, s_mAttrInstAdd, s_mAttrInstRemove, s_mAttrLevelGet, s_mAttrLevelSet;
        static FieldInfo s_fAttrInstMods, s_fAttrLevelVal;
        static MethodInfo s_mTraitsRemove, s_mTraitsAdd, s_mTraitsHasStr, s_mTraitGetName, s_mTraitGetTooltip;
        static MethodInfo s_mDbGet, s_mResTryGet, s_mPcGetCursor, s_mPopSpawnFx, s_mSimReplaceElem, s_mUnityFindObjects;
        static Type s_tDb, s_tDupStats;
        static ConstructorInfo s_cAttrMod6;
        static object s_simVacuumEnum;
        static Type s_tVec3;


        static Type s_tGeyser, s_tKPrefabId, s_tKSelectable, s_tClusterMgr, s_tCamCtrl, s_tClusterGridEntity;
        static FieldInfo s_fGeyserConfig, s_fCamCtrlInst, s_fClusterMgrInst, s_fWorldName, s_fWorldOverrideName, s_fPrefabTag;
        static MethodInfo s_mGeyserUpdateMod, s_mGeyserRefreshMeter, s_mGeyserGenName, s_mKSelectGetName;
        static MethodInfo s_mClusterGetWorld, s_mClusterSetActiveWorld, s_mWorldGetOffset, s_mKMonoTrigger;
        static MethodInfo s_mCamSetTargetPos, s_mUtilKInst2, s_mUtilKDest1, s_mGoGetComp, s_mGoSetActive, s_mGetMyWorldId;
        static PropertyInfo s_pTagName, s_pCompTransform, s_pGoTransform, s_pCgeName;
        static MethodInfo s_mTrGetPos;
        static ConstructorInfo s_cVec3F3;
        static List<FieldInfo> s_geyserMemoizerFields;

        static string s_lastGenError;
        static Delegate s_progressCb;
        static Delegate s_errorCb;
        static Action<Array, ushort[], short[]> s_extractCells;
        static Action<Array, ushort> s_fillCellElemIdx;

        static readonly float[] s_quantileSteps = new float[]
        {
            0.05f, 0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 0.95f
        };

        static readonly string[] s_dupeAttrIds = new string[]
        {
            "Construction", "Digging", "Machinery", "Athletics", "Learning",
            "Cooking", "Caring", "Strength", "Art", "Botanist", "Ranching", "SpaceNavigation"
        };

        static readonly string[] s_workAttrIds = new string[]
        {
            "Construction", "Digging", "Machinery", "Athletics"
        };

        static void LogLastError(object errInfoObj)
        {
            if (errInfoObj == null) return;
            try
            {
                Type t = errInfoObj.GetType();
                FieldInfo fd = t.GetField("errorDesc", ALL);
                FieldInfo fe = t.GetField("exception", ALL);
                string desc = fd != null ? (fd.GetValue(errInfoObj) as string) : null;
                Exception ex = fe != null ? (fe.GetValue(errInfoObj) as Exception) : null;
                if (!string.IsNullOrEmpty(desc)) s_lastGenError = desc;
                else if (ex != null) s_lastGenError = ex.Message;
            }
            catch { }
        }

        static bool InitAssemblies(out string err)
        {
            err = null;
            if (s_asmCs != null && s_asmFp != null && s_asmCore != null) return true;
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                string n = asms[i].GetName().Name;
                if (string.Equals(n, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) s_asmCs = asms[i];
                else if (string.Equals(n, "Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)) s_asmFp = asms[i];
                else if (string.Equals(n, "UnityEngine.CoreModule", StringComparison.OrdinalIgnoreCase)) s_asmCore = asms[i];
            }
            if (s_asmCs == null || s_asmFp == null || s_asmCore == null)
            {
                err = "missing-game-assemblies";
                return false;
            }
            return true;
        }

        static bool InitReflection(out string err)
        {
            if (s_tWorld != null && s_mWgRenderOffline != null && s_extractCells != null)
            {
                err = null;
                return true;
            }
            if (!InitAssemblies(out err)) return false;

            try
            {
                s_tWorld = s_asmCs.GetType("World", true);
                s_fWorldInst = s_tWorld.GetField("<Instance>k__BackingField", ALL);

                s_tGrid = s_asmCs.GetType("Grid", true);
                s_fGridW = s_tGrid.GetField("WidthInCells", ALL);
                s_fGridH = s_tGrid.GetField("HeightInCells", ALL);
                s_fGridC = s_tGrid.GetField("CellCount", ALL);
                s_fGridWm = s_tGrid.GetField("WidthInMeters", ALL);
                s_fGridHm = s_tGrid.GetField("HeightInMeters", ALL);
                s_fGridCellSize = s_tGrid.GetField("CellSizeInMeters", ALL);
                s_fGridHalfCellSize = s_tGrid.GetField("HalfCellSizeInMeters", ALL);

                s_tGridSettings = s_asmCs.GetType("GridSettings", true);
                s_mGridReset = s_tGridSettings.GetMethod("Reset", ALL, null, new Type[] { typeof(int), typeof(int) }, null);

                s_tSettingsCache = s_asmFp.GetType("ProcGen.SettingsCache", true);
                s_fScWorlds = s_tSettingsCache.GetField("worlds", ALL);
                s_fScClusters = s_tSettingsCache.GetField("clusterLayouts", ALL);
                s_mScGetRandomTraits = s_tSettingsCache.GetMethod("GetRandomTraits", ALL);

                Type tClusterLayouts = s_asmFp.GetType("ProcGen.ClusterLayouts", true);
                s_mScGetClusterData = tClusterLayouts.GetMethod("GetClusterData", ALL, null, new Type[] { typeof(string) }, null);

                Type tWorlds = s_asmFp.GetType("ProcGen.Worlds", true);
                s_mScGetWorldData = tWorlds.GetMethod("GetWorldData", ALL, null, new Type[] { typeof(string) }, null);

                s_tTemplateCache = s_asmCs.GetType("TemplateCache", true);
                s_mTcInit = s_tTemplateCache.GetMethod("Init", ALL);
                s_pTcInitted = s_tTemplateCache.GetProperty("Initted", ALL);
                s_fTcInitted = s_tTemplateCache.GetField("<Initted>k__BackingField", ALL);

                s_tWorldGen = s_asmCs.GetType("ProcGenGame.WorldGen", true);
                s_fWgVoidElem = s_tWorldGen.GetField("voidElement", ALL);
                s_fWgVacuumElem = s_tWorldGen.GetField("vacuumElement", ALL);
                s_fWgIsStarting = s_tWorldGen.GetField("isStartingWorld", ALL);
                s_mWgLoadSettingsInternal = s_tWorldGen.GetMethod("LoadSettings_Internal", ALL, null, new Type[] { typeof(bool), typeof(bool) }, null);
                s_mWgSetupDefaultElems = s_tWorldGen.GetMethod("SetupDefaultElements", ALL);
                s_cWg4 = s_tWorldGen.GetConstructor(ALL, null, new Type[] { typeof(string), typeof(List<string>), typeof(List<string>), typeof(bool) }, null);
                s_mWgSetSize = s_tWorldGen.GetMethod("SetWorldSize", ALL, null, new Type[] { typeof(int), typeof(int) }, null);
                s_mWgSetHiddenY = s_tWorldGen.GetMethod("SetHiddenYOffset", ALL, null, new Type[] { typeof(int) }, null);
                s_mWgInitialise = s_tWorldGen.GetMethod("Initialise", ALL);
                s_mWgGenOffline = s_tWorldGen.GetMethod("GenerateOffline", ALL);
                s_mWgFinalizeStart = s_tWorldGen.GetMethod("FinalizeStartLocation", ALL);
                s_mWgRenderOffline = s_tWorldGen.GetMethod("RenderOffline", ALL);
                s_pWgData = s_tWorldGen.GetProperty("data", ALL);
                s_pWgSettings = s_tWorldGen.GetProperty("Settings", ALL);

                s_tBestFit = s_asmCs.GetType("BestFit", true);
                s_mBestFitWorlds = s_tBestFit.GetMethod("BestFitWorlds", ALL);

                s_tElementLoader = s_asmCs.GetType("ElementLoader", true);
                s_fElements = s_tElementLoader.GetField("elements", ALL);
                Type tElement = s_asmCs.GetType("Element", true);
                s_fElemIdx = tElement.GetField("idx", ALL);

                s_tSimData = s_asmCs.GetType("ProcGenGame.WorldgenSimData", true);
                s_fSimDataCells = s_tSimData.GetField("cells", ALL);

                s_tVector2I = s_asmFp.GetType("Vector2I", true);
                s_cVector2I = s_tVector2I.GetConstructor(ALL, null, new Type[] { typeof(int), typeof(int) }, null);
                s_fV2ix = s_tVector2I.GetField("x", ALL);
                s_fV2iy = s_tVector2I.GetField("y", ALL);
                s_mWgSetPos = s_tWorldGen.GetMethod("SetPosition", ALL, null, new Type[] { s_tVector2I }, null);

                s_tGeyserGenericConfig = s_asmCs.GetType("GeyserGenericConfig", true);
                s_mGeyserGenConfigs = s_tGeyserGenericConfig.GetMethod("GenerateConfigs", ALL);

                s_tStrings = s_asmFp.GetType("Strings", true);
                s_tStringEntry = s_asmFp.GetType("StringEntry", true);
                s_fStringEntryVal = s_tStringEntry.GetField("String", ALL);
                s_mStringsTryGet = s_tStrings.GetMethod("TryGet", ALL, null, new Type[] { typeof(string), s_tStringEntry.MakeByRefType() }, null);

                s_tKRandom = s_asmFp.GetType("KRandom", true);
                s_cKRandomInt = s_tKRandom.GetConstructor(ALL, null, new Type[] { typeof(int) }, null);
                s_mKRandomNext2 = s_tKRandom.GetMethod("Next", ALL, null, new Type[] { typeof(int), typeof(int) }, null);
                s_mKRandomNextDbl = s_tKRandom.GetMethod("NextDouble", ALL, null, Type.EmptyTypes, null);


                s_tGeyserCfg = s_asmCs.GetType("GeyserConfigurator", false);
                if (s_tGeyserCfg != null)
                {
                    s_fGcPresetType = s_tGeyserCfg.GetField("presetType", ALL);
                    s_fGcPresetMin = s_tGeyserCfg.GetField("presetMin", ALL);
                    s_fGcPresetMax = s_tGeyserCfg.GetField("presetMax", ALL);
                    s_fGcTypesList = s_tGeyserCfg.GetField("geyserTypes", ALL);
                    s_tHashedString = s_asmFp.GetType("HashedString", false);
                    if (s_tHashedString != null)
                        s_mGcFindType = s_tGeyserCfg.GetMethod("FindType", ALL, null, new Type[] { s_tHashedString }, null);
                    s_tGeyserInstCfg = s_tGeyserCfg.GetNestedType("GeyserInstanceConfiguration", ALL);
                    s_tGeyserType = s_tGeyserCfg.GetNestedType("GeyserType", ALL);
                    if (s_tGeyserInstCfg != null)
                    {
                        s_fGicTypeId = s_tGeyserInstCfg.GetField("typeId", ALL);
                        s_fGicRateRoll = s_tGeyserInstCfg.GetField("rateRoll", ALL);
                        s_fGicIterLenRoll = s_tGeyserInstCfg.GetField("iterationLengthRoll", ALL);
                        s_fGicIterPctRoll = s_tGeyserInstCfg.GetField("iterationPercentRoll", ALL);
                        s_fGicYearLenRoll = s_tGeyserInstCfg.GetField("yearLengthRoll", ALL);
                        s_fGicYearPctRoll = s_tGeyserInstCfg.GetField("yearPercentRoll", ALL);
                        s_mGicInit = s_tGeyserInstCfg.GetMethod("Init", ALL, null, new Type[] { typeof(bool) }, null);
                        s_mGicGetAvg = s_tGeyserInstCfg.GetMethod("GetAverageEmission", ALL);
                        s_mGicGetEmit = s_tGeyserInstCfg.GetMethod("GetEmitRate", ALL);
                        s_mGicGetTemp = s_tGeyserInstCfg.GetMethod("GetTemperature", ALL);
                        s_mGicGetOnDur = s_tGeyserInstCfg.GetMethod("GetOnDuration", ALL);
                        s_mGicGetIterLen = s_tGeyserInstCfg.GetMethod("GetIterationLength", ALL);
                        s_mGicGetYearOnDur = s_tGeyserInstCfg.GetMethod("GetYearOnDuration", ALL);
                        s_mGicGetYearLen = s_tGeyserInstCfg.GetMethod("GetYearLength", ALL);
                        s_mGicGetType = s_tGeyserInstCfg.GetMethod("get_geyserType", ALL);
                    }
                    if (s_tGeyserType != null)
                    {
                        s_fGtId = s_tGeyserType.GetField("id", ALL);
                        s_fGtIdHash = s_tGeyserType.GetField("idHash", ALL);
                        s_fGtMinRate = s_tGeyserType.GetField("minRatePerCycle", ALL);
                        s_fGtMaxRate = s_tGeyserType.GetField("maxRatePerCycle", ALL);
                        s_fGtMinYearPct = s_tGeyserType.GetField("minYearPercent", ALL);
                        s_fGtMaxYearPct = s_tGeyserType.GetField("maxYearPercent", ALL);
                        s_fGtMinIterLen = s_tGeyserType.GetField("minIterationLength", ALL);
                        s_fGtMaxIterLen = s_tGeyserType.GetField("maxIterationLength", ALL);
                        s_fGtMinIterPct = s_tGeyserType.GetField("minIterationPercent", ALL);
                        s_fGtMaxIterPct = s_tGeyserType.GetField("maxIterationPercent", ALL);
                        s_fGtMinYearLen = s_tGeyserType.GetField("minYearLength", ALL);
                        s_fGtMaxYearLen = s_tGeyserType.GetField("maxYearLength", ALL);
                    }
                }

                s_tCgs = s_asmCs.GetType("CustomGameSettings", false);
                if (s_tCgs != null)
                {
                    s_fCgsInstance = s_tCgs.GetField("instance", ALL);
                    s_mCgsGetStoryCode = s_tCgs.GetMethod("GetStoryTraitSettingsCode", ALL);
                    s_mCgsParseStoryCode = s_tCgs.GetMethod("ParseAndApplyStoryTraitSettingsCode", ALL, null, new Type[] { typeof(string) }, null);
                    s_mCgsGetMixCode = s_tCgs.GetMethod("GetMixingSettingsCode", ALL);
                    s_mCgsParseMixCode = s_tCgs.GetMethod("ParseAndApplyMixingSettingsCode", ALL, null, new Type[] { typeof(string) }, null);
                    s_mCgsGetOtherCode = s_tCgs.GetMethod("GetOtherSettingsCode", ALL);
                    s_mCgsGetStories = s_tCgs.GetMethod("GetCurrentStories", ALL);
                    s_mCgsGetActiveWorldMix = s_tCgs.GetMethod("GetActiveWorldMixingSettings", ALL);
                }

                s_tWorldgenMixing = s_asmCs.GetType("ProcGenGame.WorldgenMixing", false);
                s_tMutatedLayout = s_asmFp.GetType("ProcGen.MutatedClusterLayout", false);
                if (s_tWorldgenMixing != null)
                    s_mDoWorldMixing = s_tWorldgenMixing.GetMethod("DoWorldMixing", ALL);

                s_tAssets = s_asmCs.GetType("Assets", false);
                s_tTag = s_asmFp.GetType("Tag", false);
                if (s_tAssets != null && s_tTag != null)
                {
                    s_cTagStr = s_tTag.GetConstructor(ALL, null, new Type[] { typeof(string) }, null);
                    s_mAssetsGetPrefab = s_tAssets.GetMethod("GetPrefab", ALL, null, new Type[] { s_tTag }, null);
                }

                s_tKCrashReporter = s_asmCs.GetType("KCrashReporter", false);
                if (s_tKCrashReporter != null)
                    s_fKcrIgnoreAll = s_tKCrashReporter.GetField("ignoreAll", ALL);
                s_tDebug = s_asmFp.GetType("Debug", false);
                if (s_tDebug != null)
                    s_fDebugLoggingDisabled = s_tDebug.GetField("s_loggingDisabled", ALL);


                s_mWgGenLayout = s_tWorldGen.GetMethod("GenerateLayout", ALL);
                s_mWgDrawWorldBorder = s_tWorldGen.GetMethod("DrawWorldBorder", ALL);
                s_mWgPlaceTemplateSpawners = s_tWorldGen.GetMethod("PlaceTemplateSpawners", ALL);
                s_fWgMyRandom = s_tWorldGen.GetField("myRandom", ALL);
                s_fWgPoiBounds = s_tWorldGen.GetField("POIBounds", ALL);
                s_fWgClaimedPoiCells = s_tWorldGen.GetField("claimedPOICells", ALL);
                s_tTemplateSpawning = s_asmCs.GetType("ProcGenGame.TemplateSpawning", false);
                if (s_tTemplateSpawning != null)
                    s_mTsDetermineTemplates = s_tTemplateSpawning.GetMethod("DetermineTemplatesForWorld", ALL);
                s_tTemplateSpawner = s_asmCs.GetType("ProcGenGame.TemplateSpawning+TemplateSpawner", false);
                if (s_tTemplateSpawner != null)
                {
                    s_fTspPos = s_tTemplateSpawner.GetField("position", ALL);
                    s_fTspContainer = s_tTemplateSpawner.GetField("container", ALL);
                }
                s_tSeededRandom = s_asmFp.GetType("SeededRandom", false);
                if (s_tSeededRandom != null)
                    s_cSeededRandomInt = s_tSeededRandom.GetConstructor(ALL, null, new Type[] { typeof(int) }, null);
                s_mSimDataInit = s_tSimData.GetMethod("Init", ALL, null, new Type[] { typeof(int) }, null);

                s_tTemplateContainer = s_asmCs.GetType("TemplateContainer", false);
                if (s_tTemplateContainer != null)
                {
                    s_pTcBuildings = s_tTemplateContainer.GetProperty("buildings", ALL);
                    s_pTcOtherEntities = s_tTemplateContainer.GetProperty("otherEntities", ALL);
                    s_pTcPickupables = s_tTemplateContainer.GetProperty("pickupables", ALL);
                }
                s_tPrefab = s_asmCs.GetType("TemplateClasses.Prefab", false);
                if (s_tPrefab != null)
                {
                    s_pPrefabId = s_tPrefab.GetProperty("id", ALL);
                    s_pPrefabX = s_tPrefab.GetProperty("location_x", ALL);
                    s_pPrefabY = s_tPrefab.GetProperty("location_y", ALL);
                }

                if (s_progressCb == null)
                {
                    Type cbType = s_asmCs.GetType("ProcGenGame.WorldGen+OfflineCallbackFunction", true);
                    MethodInfo invoke = cbType.GetMethod("Invoke");
                    ParameterInfo[] ps = invoke.GetParameters();
                    Type[] ptypes = new Type[ps.Length];
                    for (int i = 0; i < ps.Length; i++) ptypes[i] = ps[i].ParameterType;
                    DynamicMethod dmProg = new DynamicMethod("ONI_ProgCb", invoke.ReturnType, ptypes, typeof(Entry).Module, true);
                    ILGenerator il = dmProg.GetILGenerator();
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Ret);
                    s_progressCb = dmProg.CreateDelegate(cbType);
                }

                if (s_errorCb == null)
                {
                    Type errInfoType = s_asmCs.GetType("OfflineWorldGen+ErrorInfo", true);
                    Type actType = typeof(Action<>).MakeGenericType(errInfoType);
                    DynamicMethod dmErr = new DynamicMethod("ONI_ErrCb", typeof(void), new Type[] { errInfoType }, typeof(Entry).Module, true);
                    ILGenerator il = dmErr.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    if (errInfoType.IsValueType) il.Emit(OpCodes.Box, errInfoType);
                    il.Emit(OpCodes.Call, typeof(Entry).GetMethod("LogLastError", BindingFlags.NonPublic | BindingFlags.Static));
                    il.Emit(OpCodes.Ret);
                    s_errorCb = dmErr.CreateDelegate(actType);
                }

                if (s_extractCells == null)
                {
                    Type simCellType = s_asmCs.GetType("Sim+Cell", true);
                    FieldInfo fElemIdx = simCellType.GetField("elementIdx", ALL);
                    FieldInfo fTemp = simCellType.GetField("temperature", ALL);

                    DynamicMethod dmExt = new DynamicMethod(
                        "ONI_ExtractCells",
                        typeof(void),
                        new Type[] { typeof(Array), typeof(ushort[]), typeof(short[]) },
                        typeof(Entry).Module,
                        true);
                    ILGenerator il = dmExt.GetILGenerator();
                    LocalBuilder lCells = il.DeclareLocal(simCellType.MakeArrayType());
                    LocalBuilder lIdx = il.DeclareLocal(typeof(int));
                    LocalBuilder lLen = il.DeclareLocal(typeof(int));
                    LocalBuilder lCelRef = il.DeclareLocal(simCellType.MakeByRefType());
                    LocalBuilder lTemp = il.DeclareLocal(typeof(int));
                    Label loopCond = il.DefineLabel();
                    Label loopBody = il.DefineLabel();
                    Label clampLowOk = il.DefineLabel();
                    Label clampHighOk = il.DefineLabel();

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, simCellType.MakeArrayType());
                    il.Emit(OpCodes.Stloc, lCells);

                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldlen);
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Stloc, lLen);

                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Stloc, lIdx);
                    il.Emit(OpCodes.Br, loopCond);

                    il.MarkLabel(loopBody);
                    il.Emit(OpCodes.Ldloc, lCells);
                    il.Emit(OpCodes.Ldloc, lIdx);
                    il.Emit(OpCodes.Ldelema, simCellType);
                    il.Emit(OpCodes.Stloc, lCelRef);

                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldloc, lIdx);
                    il.Emit(OpCodes.Ldloc, lCelRef);
                    il.Emit(OpCodes.Ldfld, fElemIdx);
                    il.Emit(OpCodes.Stelem_I2);

                    il.Emit(OpCodes.Ldloc, lCelRef);
                    il.Emit(OpCodes.Ldfld, fTemp);
                    il.Emit(OpCodes.Ldc_R4, 273.15f);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Ldc_R4, 10.0f);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Stloc, lTemp);

                    il.Emit(OpCodes.Ldloc, lTemp);
                    il.Emit(OpCodes.Ldc_I4, -2730);
                    il.Emit(OpCodes.Bge_S, clampLowOk);
                    il.Emit(OpCodes.Ldc_I4, -2730);
                    il.Emit(OpCodes.Stloc, lTemp);
                    il.MarkLabel(clampLowOk);

                    il.Emit(OpCodes.Ldloc, lTemp);
                    il.Emit(OpCodes.Ldc_I4, 30000);
                    il.Emit(OpCodes.Ble_S, clampHighOk);
                    il.Emit(OpCodes.Ldc_I4, 30000);
                    il.Emit(OpCodes.Stloc, lTemp);
                    il.MarkLabel(clampHighOk);

                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldloc, lIdx);
                    il.Emit(OpCodes.Ldloc, lTemp);
                    il.Emit(OpCodes.Conv_I2);
                    il.Emit(OpCodes.Stelem_I2);

                    il.Emit(OpCodes.Ldloc, lIdx);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, lIdx);

                    il.MarkLabel(loopCond);
                    il.Emit(OpCodes.Ldloc, lIdx);
                    il.Emit(OpCodes.Ldloc, lLen);
                    il.Emit(OpCodes.Blt, loopBody);

                    il.Emit(OpCodes.Ret);
                    s_extractCells = (Action<Array, ushort[], short[]>)dmExt.CreateDelegate(typeof(Action<Array, ushort[], short[]>));

                    DynamicMethod dmFill = new DynamicMethod(
                        "ONI_FillCellElemIdx",
                        typeof(void),
                        new Type[] { typeof(Array), typeof(ushort) },
                        typeof(Entry).Module,
                        true);
                    ILGenerator ilf = dmFill.GetILGenerator();
                    LocalBuilder lfCells = ilf.DeclareLocal(simCellType.MakeArrayType());
                    LocalBuilder lfIdx = ilf.DeclareLocal(typeof(int));
                    LocalBuilder lfLen = ilf.DeclareLocal(typeof(int));
                    Label fCond = ilf.DefineLabel();
                    Label fBody = ilf.DefineLabel();

                    ilf.Emit(OpCodes.Ldarg_0);
                    ilf.Emit(OpCodes.Castclass, simCellType.MakeArrayType());
                    ilf.Emit(OpCodes.Stloc, lfCells);

                    ilf.Emit(OpCodes.Ldloc, lfCells);
                    ilf.Emit(OpCodes.Ldlen);
                    ilf.Emit(OpCodes.Conv_I4);
                    ilf.Emit(OpCodes.Stloc, lfLen);

                    ilf.Emit(OpCodes.Ldc_I4_0);
                    ilf.Emit(OpCodes.Stloc, lfIdx);
                    ilf.Emit(OpCodes.Br, fCond);

                    ilf.MarkLabel(fBody);
                    ilf.Emit(OpCodes.Ldloc, lfCells);
                    ilf.Emit(OpCodes.Ldloc, lfIdx);
                    ilf.Emit(OpCodes.Ldelema, simCellType);
                    ilf.Emit(OpCodes.Ldarg_1);
                    ilf.Emit(OpCodes.Stfld, fElemIdx);

                    ilf.Emit(OpCodes.Ldloc, lfIdx);
                    ilf.Emit(OpCodes.Ldc_I4_1);
                    ilf.Emit(OpCodes.Add);
                    ilf.Emit(OpCodes.Stloc, lfIdx);

                    ilf.MarkLabel(fCond);
                    ilf.Emit(OpCodes.Ldloc, lfIdx);
                    ilf.Emit(OpCodes.Ldloc, lfLen);
                    ilf.Emit(OpCodes.Blt, fBody);

                    ilf.Emit(OpCodes.Ret);
                    s_fillCellElemIdx = (Action<Array, ushort>)dmFill.CreateDelegate(typeof(Action<Array, ushort>));
                }

                return true;
            }
            catch (Exception e)
            {
                err = "reflect-init:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
        }



        public static string ExecMenuThread(string cmd)
        {
            lock (s_lock)
            {
                try
                {
                    string err;
                    if (!InitReflection(out err)) return "ERR " + err;
                    if (s_fWorldInst != null && s_fWorldInst.GetValue(null) != null)
                        return "ERR in-save";

                    string clean = (cmd ?? "").Trim();
                    if (string.Equals(clean, "GETGAMECFG", StringComparison.OrdinalIgnoreCase))
                    {
                        if (s_tCgs == null) return "ERR no-cgs";
                        object cgs = GetCustomGameSettingsInstance();
                        if (cgs == null) return "ERR no-cgs-inst";
                        string otherCode = s_mCgsGetOtherCode != null ? (s_mCgsGetOtherCode.Invoke(cgs, null) as string ?? "") : "";
                        string storyCode = s_mCgsGetStoryCode != null ? (s_mCgsGetStoryCode.Invoke(cgs, null) as string ?? "") : "";
                        string mixingCode = s_mCgsGetMixCode != null ? (s_mCgsGetMixCode.Invoke(cgs, null) as string ?? "") : "";
                        List<string> stories = new List<string>();
                        if (s_mCgsGetStories != null)
                        {
                            IEnumerable st = s_mCgsGetStories.Invoke(cgs, null) as IEnumerable;
                            if (st != null)
                            {
                                object storiesDb = null;
                                MethodInfo mGetStory = null;
                                try
                                {
                                    Type tDb = s_asmCs.GetType("Db", false);
                                    MethodInfo mDbGet = tDb != null ? tDb.GetMethod("Get", ALL) : null;
                                    object db = mDbGet != null ? mDbGet.Invoke(null, null) : null;
                                    FieldInfo fStories = (db != null && tDb != null) ? tDb.GetField("Stories", ALL) : null;
                                    storiesDb = fStories != null ? fStories.GetValue(db) : null;
                                    if (storiesDb != null)
                                        mGetStory = storiesDb.GetType().GetMethod("Get", ALL, null, new Type[] { typeof(string) }, null);
                                }
                                catch { }

                                foreach (object o in st)
                                {
                                    string id = o as string;
                                    if (string.IsNullOrEmpty(id)) continue;
                                    string resolved = null;
                                    if (storiesDb != null && mGetStory != null)
                                    {
                                        try
                                        {
                                            object stObj = mGetStory.Invoke(storiesDb, new object[] { id });
                                            if (stObj != null)
                                                resolved = stObj.GetType().GetField("worldgenStoryTraitKey", ALL).GetValue(stObj) as string;
                                        }
                                        catch { }
                                    }
                                    if (string.IsNullOrEmpty(resolved))
                                        resolved = NormalizeStoryTraitKey(id);
                                    if (!string.IsNullOrEmpty(resolved)) stories.Add(resolved);
                                }
                            }
                        }
                        return "OK otherCode=" + otherCode + "|storyCode=" + storyCode + "|mixingCode=" + mixingCode + "|stories=" + string.Join(",", stories.ToArray());
                    }
                    if (clean.StartsWith("GETWORKERDATA", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = clean.Length > 13 ? clean.Substring(13).Trim() : "";
                        return ExportWorkerDataCatalog(path);
                    }
                    return "ERR unknown-menu-cmd";
                }
                catch (Exception e)
                {
                    return "ERR exc:" + e.Message;
                }
            }
        }

        static object GetCustomGameSettingsInstance()
        {
            if (s_tCgs == null) return null;
            MethodInfo m = s_tCgs.GetMethod("get_Instance", ALL);
            object inst = m != null ? m.Invoke(null, null) : null;
            if (inst == null && s_fCgsInstance != null) inst = s_fCgsInstance.GetValue(null);
            return inst;
        }



        struct SavedPlacementState
        {
            public string WorldPath;
            public int X, Y, W, H;
            public bool StartWorld;
        }

        sealed class GeyserPoolEntry
        {
            public string PrefabId;
            public string RawId;
            public string LocalizedName;
            public object GeyserTypeObj;
        }

        sealed class PoiOut
        {
            public string PrefabId;
            public string Category;
            public string DisplayName;
            public int X;
            public int Y;
            public byte IsRandomGeyser;

            public byte HasGeyserStats;
            public float AvgYieldGps;
            public float EruptRateGps;
            public float OutputTempC;
            public float EruptSec;
            public float IdleSec;
            public float ActiveCycles;
            public float DormantCycles;
            public byte Grade;
            public float Percentile;
        }

        public static string GeneratePreview(string args)
        {
            if (string.IsNullOrEmpty(args)) return "ERR bad-args";
            string[] parts = args.Split('|');
            if (parts.Length < 6) return "ERR bad-args-count";

            if (parts.Length >= 9 && string.Equals(parts[5].Trim(), "BATCHPOI", StringComparison.OrdinalIgnoreCase))
            {
                string recOut = parts.Length > 9 ? parts[9].Trim() : "";
                return RunBatchPoi(
                    parts[0].Trim(), parts[2].Trim(), parts[3].Trim(), parts[4].Trim(),
                    parts[6].Trim(), parts[7].Trim(), parts[8].Trim(), recOut);
            }

            string clusterPath = parts[0].Trim();
            int seed;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed) || seed <= 0)
                return "ERR bad-seed";
            int worldIndex;
            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out worldIndex) || worldIndex < 0)
                return "ERR bad-world-index";
            string activeDlcCsv = parts[3].Trim();
            string storyArg = parts[4].Trim();
            string outPath = parts[5].Trim();
            string mixingCode = parts.Length > 6 ? parts[6].Trim() : "";
            bool fastPoi = parts.Length > 7 && string.Equals(parts[7].Trim(), "FASTPOI", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(outPath)) return "ERR bad-outpath";

            lock (s_lock)
            {
                CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
                object cgs = null;
                string prevStoryCode = null;
                string prevMixingCode = null;
                bool touchedStoryCode = false;
                bool touchedMixingCode = false;
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                    string initErr;
                    if (!InitReflection(out initErr)) return "ERR " + initErr;

                    bool inSave = (s_fWorldInst != null && s_fWorldInst.GetValue(null) != null);


                    if (!s_isHeadlessWorker && inSave)
                        return "ERR in-save-disabled";

                    Stopwatch sw = Stopwatch.StartNew();

                    if (!IsSettingsCachePopulated())
                        s_mWgLoadSettingsInternal.Invoke(null, new object[] { true, false });

                    bool tcInitted = false;
                    if (s_pTcInitted != null) tcInitted = (bool)s_pTcInitted.GetValue(null, null);
                    else if (s_fTcInitted != null) tcInitted = (bool)s_fTcInitted.GetValue(null);
                    if (!tcInitted)
                        s_mTcInit.Invoke(null, null);

                    if (s_fWgVoidElem.GetValue(null) == null)
                        s_mWgSetupDefaultElems.Invoke(null, null);

                    IList elementsList = s_fElements.GetValue(null) as IList;
                    if (elementsList == null || elementsList.Count == 0)
                        return "ERR no-elements";

                    object clusterLayoutsObj = s_fScClusters.GetValue(null);
                    if (clusterLayoutsObj == null)
                        return "ERR no-clusterlayouts";

                    object layoutObj = s_mScGetClusterData.Invoke(clusterLayoutsObj, new object[] { clusterPath });
                    if (layoutObj == null)
                        layoutObj = FindClusterFallback(clusterLayoutsObj, clusterPath);
                    if (layoutObj == null)
                        return "ERR unknown-cluster:" + clusterPath;


                    cgs = GetCustomGameSettingsInstance();
                    if (cgs != null)
                    {
                        if (storyArg.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase) &&
                            s_mCgsGetStoryCode != null && s_mCgsParseStoryCode != null)
                        {
                            prevStoryCode = s_mCgsGetStoryCode.Invoke(cgs, null) as string;
                            string targetStoryCode = storyArg.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(targetStoryCode) && !string.Equals(targetStoryCode, prevStoryCode, StringComparison.Ordinal))
                            {
                                s_mCgsParseStoryCode.Invoke(cgs, new object[] { targetStoryCode });
                                touchedStoryCode = true;
                            }
                        }
                        if (!string.IsNullOrEmpty(mixingCode) &&
                            s_mCgsGetMixCode != null && s_mCgsParseMixCode != null)
                        {
                            prevMixingCode = s_mCgsGetMixCode.Invoke(cgs, null) as string;
                            if (!string.Equals(mixingCode, prevMixingCode, StringComparison.Ordinal))
                            {
                                s_mCgsParseMixCode.Invoke(cgs, new object[] { mixingCode });
                                touchedMixingCode = true;
                            }
                        }
                    }

                    Type tLayout = layoutObj.GetType();
                    IList placements = tLayout.GetProperty("worldPlacements", ALL).GetValue(layoutObj, null) as IList;
                    if (placements == null || worldIndex >= placements.Count)
                        return "ERR bad-world-idx:" + worldIndex;

                    int startWorldIndex = (int)tLayout.GetProperty("startWorldIndex", ALL).GetValue(layoutObj, null);
                    object worldsObj = s_fScWorlds.GetValue(null);

                    int savedGridW = (int)s_fGridW.GetValue(null);
                    int savedGridH = (int)s_fGridH.GetValue(null);
                    int savedGridC = (int)s_fGridC.GetValue(null);
                    float savedGridWm = s_fGridWm != null ? (float)s_fGridWm.GetValue(null) : 0f;
                    float savedGridHm = s_fGridHm != null ? (float)s_fGridHm.GetValue(null) : 0f;
                    float savedGridCellSize = s_fGridCellSize != null ? (float)s_fGridCellSize.GetValue(null) : 1f;
                    float savedGridHalfCellSize = s_fGridHalfCellSize != null ? (float)s_fGridHalfCellSize.GetValue(null) : 0.5f;

                    SavedPlacementState[] savedPlacements = new SavedPlacementState[placements.Count];
                    Type tWp = placements[0].GetType();
                    FieldInfo fWpX = tWp.GetField("<x>k__BackingField", ALL);
                    FieldInfo fWpY = tWp.GetField("<y>k__BackingField", ALL);
                    FieldInfo fWpW = tWp.GetField("<width>k__BackingField", ALL);
                    FieldInfo fWpH = tWp.GetField("<height>k__BackingField", ALL);
                    FieldInfo fWpStart = tWp.GetField("<startWorld>k__BackingField", ALL);
                    PropertyInfo pWpWorld = tWp.GetProperty("world", ALL);
                    PropertyInfo pWpWorldMixing = tWp.GetProperty("worldMixing", ALL);
                    MethodInfo mWpSetSize = tWp.GetMethod("SetSize", ALL);
                    MethodInfo mWpUndoMix = tWp.GetMethod("UndoWorldMixing", ALL);

                    for (int i = 0; i < placements.Count; i++)
                    {
                        object wp = placements[i];
                        savedPlacements[i] = new SavedPlacementState
                        {
                            WorldPath = pWpWorld.GetValue(wp, null) as string,
                            X = (int)fWpX.GetValue(wp),
                            Y = (int)fWpY.GetValue(wp),
                            W = (int)fWpW.GetValue(wp),
                            H = (int)fWpH.GetValue(wp),
                            StartWorld = (bool)fWpStart.GetValue(wp)
                        };
                    }


                    object mixedSettingsWorld = null;
                    IList listRules = null, listSubworlds = null, listUnknown = null, listSeasons = null;
                    int origRulesCnt = -1, origSubCnt = -1, origUnkCnt = -1, origSeaCnt = -1;
                    bool prevKcrIgnoreAll = false;
                    bool hasKcrIgnoreAll = false;
                    bool prevDebugDisabled = false;
                    bool hasDebugDisabled = false;

                    try
                    {
                        if (s_fKcrIgnoreAll != null && s_fKcrIgnoreAll.IsStatic)
                        {
                            try
                            {
                                prevKcrIgnoreAll = (bool)s_fKcrIgnoreAll.GetValue(null);
                                hasKcrIgnoreAll = true;
                                s_fKcrIgnoreAll.SetValue(null, true);
                            }
                            catch { }
                        }
                        if (s_fDebugLoggingDisabled != null && s_fDebugLoggingDisabled.IsStatic)
                        {
                            try
                            {
                                prevDebugDisabled = (bool)s_fDebugLoggingDisabled.GetValue(null);
                                hasDebugDisabled = true;
                                s_fDebugLoggingDisabled.SetValue(null, true);
                            }
                            catch { }
                        }

                        if (s_mDoWorldMixing != null && cgs != null && s_mCgsGetActiveWorldMix != null)
                        {
                            try
                            {
                                IList activeMix = s_mCgsGetActiveWorldMix.Invoke(cgs, null) as IList;
                                if (activeMix != null && activeMix.Count > 0)
                                {
                                    ParameterInfo[] pms = s_mDoWorldMixing.GetParameters();
                                    object arg0 = layoutObj;
                                    if (pms.Length > 0 && s_tMutatedLayout != null && pms[0].ParameterType == s_tMutatedLayout)
                                        arg0 = Activator.CreateInstance(s_tMutatedLayout, new object[] { layoutObj });
                                    s_mDoWorldMixing.Invoke(null, new object[] { arg0, seed, false, true });
                                }
                            }
                            catch { }
                        }

                        for (int i = 0; i < placements.Count; i++)
                        {
                            object wp = placements[i];
                            string wid = pWpWorld.GetValue(wp, null) as string;
                            object wData = s_mScGetWorldData.Invoke(worldsObj, new object[] { wid });
                            object wSize = wData.GetType().GetProperty("worldsize", ALL).GetValue(wData, null);
                            mWpSetSize.Invoke(wp, new object[] { wSize });
                            fWpStart.SetValue(wp, i == startWorldIndex);
                        }

                        s_mBestFitWorlds.Invoke(null, new object[] { placements, false });

                        object targetWp = placements[worldIndex];
                        int wpX = (int)fWpX.GetValue(targetWp);
                        int wpY = (int)fWpY.GetValue(targetWp);
                        string targetWorldId = pWpWorld.GetValue(targetWp, null) as string;
                        object targetWorld = s_mScGetWorldData.Invoke(worldsObj, new object[] { targetWorldId });
                        Type tWorldDef = targetWorld.GetType();
                        object targetSize = tWorldDef.GetProperty("worldsize", ALL).GetValue(targetWorld, null);
                        int sizeX = (int)s_fV2ix.GetValue(targetSize);
                        int sizeY = (int)s_fV2iy.GetValue(targetSize);
                        int hiddenY = (int)tWorldDef.GetProperty("hiddenY", ALL).GetValue(targetWorld, null);
                        string worldDisplayName = ResolveWorldDisplayName(targetWorld, targetWorldId);

                        int geyserGlobalSeed = (seed > 0) ? unchecked(seed + Math.Max(0, placements.Count - 1)) : seed;
                        int wSeed = (seed > 0) ? unchecked(seed + worldIndex) : seed;
                        List<string> chosenTraits = s_mScGetRandomTraits.Invoke(null, new object[] { wSeed, targetWorld }) as List<string>;
                        if (chosenTraits == null) chosenTraits = new List<string>();
                        List<string> storyTraits = BuildStoryTraitList(storyArg);

                        object wg = s_cWg4.Invoke(new object[] { targetWorldId, chosenTraits, storyTraits, false });
                        s_mWgSetSize.Invoke(wg, new object[] { sizeX, sizeY });
                        object posObj = s_cVector2I.Invoke(new object[] { wpX, wpY });
                        s_mWgSetPos.Invoke(wg, new object[] { posObj });
                        s_mWgSetHiddenY.Invoke(wg, new object[] { hiddenY });
                        bool isStart = (worldIndex == startWorldIndex);
                        if (s_fWgIsStarting != null) s_fWgIsStarting.SetValue(wg, isStart);


                        if (pWpWorldMixing != null && s_pWgSettings != null)
                        {
                            try
                            {
                                object wm = pWpWorldMixing.GetValue(targetWp, null);
                                if (wm != null)
                                {
                                    FieldInfo fApplied = wm.GetType().GetField("mixingWasApplied", ALL);
                                    bool applied = fApplied != null && (bool)fApplied.GetValue(wm);
                                    if (applied)
                                    {
                                        object wgs = s_pWgSettings.GetValue(wg, null);
                                        mixedSettingsWorld = wgs != null ? wgs.GetType().GetProperty("world", ALL).GetValue(wgs, null) : null;
                                        if (mixedSettingsWorld != null)
                                        {
                                            Type tw = mixedSettingsWorld.GetType();
                                            listRules = tw.GetProperty("worldTemplateRules", ALL).GetValue(mixedSettingsWorld, null) as IList;
                                            listSubworlds = tw.GetProperty("subworldFiles", ALL).GetValue(mixedSettingsWorld, null) as IList;
                                            PropertyInfo pUnk = tw.GetProperty("unknownCellsAllowedSubworlds", ALL);
                                            PropertyInfo pSea = tw.GetProperty("seasons", ALL);
                                            listUnknown = pUnk != null ? (pUnk.GetValue(mixedSettingsWorld, null) as IList) : null;
                                            listSeasons = pSea != null ? (pSea.GetValue(mixedSettingsWorld, null) as IList) : null;
                                            if (listRules != null) origRulesCnt = listRules.Count;
                                            if (listSubworlds != null) origSubCnt = listSubworlds.Count;
                                            if (listUnknown != null) origUnkCnt = listUnknown.Count;
                                            if (listSeasons != null) origSeaCnt = listSeasons.Count;

                                            PropertyInfo pAddRules = wm.GetType().GetProperty("additionalWorldTemplateRules", ALL);
                                            PropertyInfo pAddSubs = wm.GetType().GetProperty("additionalSubworldFiles", ALL);
                                            PropertyInfo pAddUnk = wm.GetType().GetProperty("additionalUnknownCellFilters", ALL);
                                            PropertyInfo pAddSea = wm.GetType().GetProperty("additionalSeasons", ALL);
                                            AddRangeSafe(listRules, pAddRules != null ? pAddRules.GetValue(wm, null) as IEnumerable : null);
                                            AddRangeSafe(listSubworlds, pAddSubs != null ? pAddSubs.GetValue(wm, null) as IEnumerable : null);
                                            MethodInfo mAddUnk = tw.GetMethod("AddUnknownCellsAllowedSubworlds", ALL);
                                            MethodInfo mAddSea = tw.GetMethod("AddSeasons", ALL);
                                            if (mAddUnk != null && pAddUnk != null) mAddUnk.Invoke(mixedSettingsWorld, new object[] { pAddUnk.GetValue(wm, null) });
                                            if (mAddSea != null && pAddSea != null) mAddSea.Invoke(mixedSettingsWorld, new object[] { pAddSea.GetValue(wm, null) });
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        s_lastGenError = null;
                        s_mWgInitialise.Invoke(wg, new object[] { s_progressCb, s_errorCb, wSeed, wSeed, wSeed, wSeed, false, false });

                        bool useFastPoi = fastPoi && CanRunFastPoi(wg);
                        if (!inSave && !useFastPoi)
                        {
                            s_mGridReset.Invoke(null, new object[] { sizeX, sizeY });
                        }
                        else
                        {
                            s_fGridW.SetValue(null, sizeX);
                            s_fGridH.SetValue(null, sizeY);
                            s_fGridC.SetValue(null, sizeX * sizeY);
                            if (s_fGridWm != null) s_fGridWm.SetValue(null, (float)sizeX);
                            if (s_fGridHm != null) s_fGridHm.SetValue(null, (float)sizeY);
                            if (s_fGridCellSize != null) s_fGridCellSize.SetValue(null, 1f);
                            if (s_fGridHalfCellSize != null) s_fGridHalfCellSize.SetValue(null, 0.5f);
                        }

                        if (useFastPoi)
                        {
                            string fastErr;
                            int fastBaseX, fastBaseY;
                            List<PoiOut> fastPois = TryRunFastPoi(wg, sizeX, sizeY, wpX, wpY, geyserGlobalSeed, wSeed, activeDlcCsv, out fastBaseX, out fastBaseY, out fastErr);
                            if (fastErr != null)
                                return fastErr;
                            if (fastPois != null)
                            {
                                sw.Stop();
                                string fastTraits = FormatTraitsSummary(chosenTraits);
                                WritePoiOnlyPreview(
                                    outPath, clusterPath, seed, worldIndex, worldDisplayName,
                                    sizeX, sizeY, wpX, wpY, fastBaseX, fastBaseY, (int)sw.ElapsedMilliseconds,
                                    fastTraits, fastPois);
                                return string.Format(CultureInfo.InvariantCulture,
                                    "OK fp w:{0} h:{1} pois:{2} ms:{3}", sizeX, sizeY, fastPois.Count, sw.ElapsedMilliseconds);
                            }
                            if (!inSave) s_mGridReset.Invoke(null, new object[] { sizeX, sizeY });
                        }

                        bool okGen = (bool)s_mWgGenOffline.Invoke(wg, null);
                        if (!okGen)
                            return "ERR gen-offline:" + SanitizeSingleLine(s_lastGenError ?? "unknown");

                        s_mWgFinalizeStart.Invoke(wg, null);

                        object simData = Activator.CreateInstance(s_tSimData);
                        Type tWorldTrait = s_asmFp.GetType("ProcGen.WorldTrait", true);
                        object placedStories = Activator.CreateInstance(typeof(List<>).MakeGenericType(tWorldTrait));
                        MemoryStream dummyStream = new MemoryStream();
                        BinaryWriter dummyWriter = new BinaryWriter(dummyStream);
                        object[] renderArgs = new object[] { false, (uint)seed, dummyWriter, simData, worldIndex, placedStories, isStart };
                        bool okRender = (bool)s_mWgRenderOffline.Invoke(wg, renderArgs);
                        simData = renderArgs[3];
                        if (!okRender || simData == null)
                            return "ERR render-offline:" + SanitizeSingleLine(s_lastGenError ?? "unknown");

                        Array simCells = s_tSimData.GetField("cells", ALL).GetValue(simData) as Array;
                        if (simCells == null || simCells.Length < sizeX * sizeY)
                            return "ERR bad-simcells";

                        int totalCells = sizeX * sizeY;
                        ushort[] outElem = new ushort[totalCells];
                        short[] outTemp = new short[totalCells];
                        s_extractCells(simCells, outElem, outTemp);

                        object wgData = s_pWgData.GetValue(wg, null);
                        byte[] zoneGrid = RasterizeBiomes(wg, wgData, sizeX, sizeY, wpX, wpY);

                        int baseStartX, baseStartY;
                        List<PoiOut> pois = ExtractPois(wgData, geyserGlobalSeed, wpX, wpY, sizeX, sizeY, activeDlcCsv, out baseStartX, out baseStartY);

                        sw.Stop();
                        string traitsSummary = FormatTraitsSummary(chosenTraits);

                        WriteBinaryPreview(
                            outPath, clusterPath, seed, worldIndex, worldDisplayName,
                            sizeX, sizeY, wpX, wpY, baseStartX, baseStartY, (int)sw.ElapsedMilliseconds,
                            traitsSummary, elementsList, outElem, outTemp, zoneGrid, pois);

                        return string.Format(CultureInfo.InvariantCulture,
                            "OK w:{0} h:{1} pois:{2} ms:{3}", sizeX, sizeY, pois.Count, sw.ElapsedMilliseconds);
                    }
                    finally
                    {
                        if (hasDebugDisabled && s_fDebugLoggingDisabled != null)
                        {
                            try { s_fDebugLoggingDisabled.SetValue(null, prevDebugDisabled); } catch { }
                        }
                        if (hasKcrIgnoreAll && s_fKcrIgnoreAll != null)
                        {
                            try { s_fKcrIgnoreAll.SetValue(null, prevKcrIgnoreAll); } catch { }
                        }

                        TruncateList(listRules, origRulesCnt);
                        TruncateList(listSubworlds, origSubCnt);
                        TruncateList(listUnknown, origUnkCnt);
                        TruncateList(listSeasons, origSeaCnt);

                        for (int i = 0; i < placements.Count; i++)
                        {
                            object wp = placements[i];
                            if (mWpUndoMix != null) { try { mWpUndoMix.Invoke(wp, null); } catch { } }
                            if (!string.IsNullOrEmpty(savedPlacements[i].WorldPath))
                                pWpWorld.SetValue(wp, savedPlacements[i].WorldPath, null);
                            fWpX.SetValue(wp, savedPlacements[i].X);
                            fWpY.SetValue(wp, savedPlacements[i].Y);
                            fWpW.SetValue(wp, savedPlacements[i].W);
                            fWpH.SetValue(wp, savedPlacements[i].H);
                            fWpStart.SetValue(wp, savedPlacements[i].StartWorld);
                        }
                        s_fGridW.SetValue(null, savedGridW);
                        s_fGridH.SetValue(null, savedGridH);
                        s_fGridC.SetValue(null, savedGridC);
                        if (s_fGridWm != null) s_fGridWm.SetValue(null, savedGridWm);
                        if (s_fGridHm != null) s_fGridHm.SetValue(null, savedGridHm);
                        if (s_fGridCellSize != null) s_fGridCellSize.SetValue(null, savedGridCellSize);
                        if (s_fGridHalfCellSize != null) s_fGridHalfCellSize.SetValue(null, savedGridHalfCellSize);
                    }
                }
                catch (Exception e)
                {
                    Exception inner = e;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    string st = inner.StackTrace ?? e.StackTrace ?? "";
                    if (st.Length > 700) st = st.Substring(0, 700);
                    return "ERR exc:" + inner.GetType().Name + ":" + inner.Message + " | " + st.Replace('\r', ' ').Replace('\n', ' ');
                }
                finally
                {
                    if (cgs != null)
                    {
                        if (touchedStoryCode && s_mCgsParseStoryCode != null)
                        {
                            try { s_mCgsParseStoryCode.Invoke(cgs, new object[] { prevStoryCode ?? "" }); } catch { }
                        }
                        if (touchedMixingCode && s_mCgsParseMixCode != null)
                        {
                            try { s_mCgsParseMixCode.Invoke(cgs, new object[] { prevMixingCode ?? "" }); } catch { }
                        }
                    }
                    Thread.CurrentThread.CurrentCulture = prevCulture;
                }
            }
        }

        static bool CanRunFastPoi(object wg)
        {
            if (wg == null ||
                s_mWgGenLayout == null || s_mWgPlaceTemplateSpawners == null ||
                s_fWgMyRandom == null || s_fWgPoiBounds == null || s_fWgClaimedPoiCells == null ||
                s_mTsDetermineTemplates == null || s_fTspPos == null || s_fTspContainer == null)
                return false;
            try
            {
                object wgs = s_pWgSettings != null ? s_pWgSettings.GetValue(wg, null) : null;
                object worldObj = wgs != null ? wgs.GetType().GetProperty("world", ALL).GetValue(wgs, null) : null;
                if (worldObj != null)
                {
                    PropertyInfo pMod = worldObj.GetType().GetProperty("modifyLayoutTags", ALL);
                    if (pMod != null)
                    {
                        IList rules = pMod.GetValue(worldObj, null) as IList;
                        if (rules != null && rules.Count > 0) return false;
                    }
                }
            }
            catch { return false; }
            return true;
        }

        static void PrepareFastSimDataForBorder(int total)
        {
            if (s_cachedSimData == null || s_cachedSimDataCells != total)
            {
                object sd = Activator.CreateInstance(s_tSimData);
                s_mSimDataInit.Invoke(sd, new object[] { total });
                s_cachedSimData = sd;
                s_cachedSimDataCells = total;
            }
            ushort vacIdx = 0;
            if (s_fWgVacuumElem != null && s_fElemIdx != null)
            {
                object vacObj = s_fWgVacuumElem.GetValue(null);
                if (vacObj != null)
                    vacIdx = Convert.ToUInt16(s_fElemIdx.GetValue(vacObj), CultureInfo.InvariantCulture);
            }
            Array simCells = s_fSimDataCells != null ? (s_fSimDataCells.GetValue(s_cachedSimData) as Array) : null;
            if (simCells != null && s_fillCellElemIdx != null)
                s_fillCellElemIdx(simCells, vacIdx);
        }

        static List<PoiOut> TryRunFastPoi(
            object wg, int sizeX, int sizeY, int wpX, int wpY, int clusterSeed, int wSeed, string activeDlcCsv,
            out int baseStartX, out int baseStartY, out string err)
        {
            baseStartX = sizeX / 2;
            baseStartY = sizeY / 2;
            err = null;

            bool okLayout = (bool)s_mWgGenLayout.Invoke(wg, new object[] { s_progressCb });
            if (!okLayout)
            {
                err = "ERR gen-offline:" + SanitizeSingleLine(s_lastGenError ?? "layout-failed");
                return null;
            }

            s_mWgFinalizeStart.Invoke(wg, null);

            object wgData = s_pWgData.GetValue(wg, null);
            object wgSettings = s_pWgSettings.GetValue(wg, null);
            object poiBounds = Activator.CreateInstance(s_fWgPoiBounds.FieldType);
            s_fWgPoiBounds.SetValue(wg, poiBounds);

            bool drawBorder = false;
            try
            {
                MethodInfo mGetBool = wgSettings != null ? wgSettings.GetType().GetMethod("GetBoolSetting", ALL, null, new Type[] { typeof(string) }, null) : null;
                if (mGetBool != null)
                    drawBorder = (bool)mGetBool.Invoke(wgSettings, new object[] { "DrawWorldBorder" });
            }
            catch { }

            if (drawBorder && s_mWgDrawWorldBorder != null && s_cSeededRandomInt != null && s_mSimDataInit != null)
            {
                PrepareFastSimDataForBorder(sizeX * sizeY);
                object borderRng = s_cSeededRandomInt.Invoke(new object[] { 0 });
                HashSet<int> borderCells = new HashSet<int>();
                object chunkWorld = wgData.GetType().GetField("world", ALL).GetValue(wgData);
                object[] bArgs = new object[] { s_cachedSimData, chunkWorld, borderRng, borderCells, poiBounds, s_progressCb };
                s_mWgDrawWorldBorder.Invoke(wg, bArgs);
                poiBounds = bArgs[4];
                s_fWgPoiBounds.SetValue(wg, poiBounds);
            }

            object spawnData = wgData.GetType().GetField("gameSpawnData", ALL).GetValue(wgData);
            object worldLayout = wgData.GetType().GetField("worldLayout", ALL).GetValue(wgData);
            if (spawnData != null && worldLayout != null)
            {
                MethodInfo mGetStart = worldLayout.GetType().GetMethod("GetStartLocation", ALL);
                if (mGetStart != null)
                {
                    object startPos = mGetStart.Invoke(worldLayout, null);
                    FieldInfo fBsp = spawnData.GetType().GetField("baseStartPos", ALL);
                    if (fBsp != null && startPos != null) fBsp.SetValue(spawnData, startPos);
                }
            }

            IList terrainCells = wgData.GetType().GetField("terrainCells", ALL).GetValue(wgData) as IList;
            object myRandom = s_fWgMyRandom.GetValue(wg);
            Type tWorldTrait = s_asmFp.GetType("ProcGen.WorldTrait", true);
            object placedStories = Activator.CreateInstance(typeof(List<>).MakeGenericType(tWorldTrait));
            object[] tsArgs = new object[] { wgSettings, terrainCells, myRandom, poiBounds, false, placedStories, s_progressCb };

            IList spawners = null;
            try
            {
                spawners = s_mTsDetermineTemplates.Invoke(null, tsArgs) as IList;
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                string msg = null;
                try
                {
                    FieldInfo fUm = inner.GetType().GetField("userMessage", ALL);
                    if (fUm != null) msg = fUm.GetValue(inner) as string;
                }
                catch { }
                if (string.IsNullOrEmpty(msg)) msg = inner.Message;
                err = "ERR render-offline:" + SanitizeSingleLine(msg ?? "template-spawning-failed");
                return null;
            }

            poiBounds = tsArgs[3];
            s_fWgPoiBounds.SetValue(wg, poiBounds);

            if (spawners == null)
            {
                err = "ERR render-offline:" + SanitizeSingleLine(s_lastGenError ?? "template-spawning-null");
                return null;
            }

            object claimedPoi = s_fWgClaimedPoiCells.GetValue(wg);
            for (int i = 0; i < spawners.Count; i++)
            {
                object sp = spawners[i];
                object spPos = s_fTspPos.GetValue(sp);
                object spCont = s_fTspContainer.GetValue(sp);
                object[] pArgs = new object[] { spPos, spCont, claimedPoi };
                s_mWgPlaceTemplateSpawners.Invoke(wg, pArgs);
                claimedPoi = pArgs[2];
            }
            s_fWgClaimedPoiCells.SetValue(wg, claimedPoi);

            return ExtractPois(wgData, clusterSeed, wpX, wpY, sizeX, sizeY, activeDlcCsv, out baseStartX, out baseStartY);
        }

        sealed class BatchRule
        {
            public string PrefabId;
            public bool IsWildcard;
            public string WildCat;
            public bool CompareAtMost;
            public int MinCount;
            public int MaxDist;
            public float MinYieldGps;
            public byte MinGrade;
        }

        sealed class BatchPoi
        {
            public string PrefabId;
            public string Category;
            public int X;
            public int Y;
            public byte IsRandomGeyser;
            public GeyserPoolEntry PickedPoolEntry;
            public bool StatsComputed;
            public float AvgYieldGps;
            public byte Grade;
            public float Percentile;
        }

        static List<BatchRule> ParseBatchRules(string spec)
        {
            List<BatchRule> rules = new List<BatchRule>();
            if (string.IsNullOrEmpty(spec)) return rules;
            string[] items = spec.Split(';');
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i].Trim();
                if (item.Length == 0) continue;
                string[] seg = item.Split('~');
                if (seg.Length < 6) continue;
                string pid = seg[0].Trim();
                if (pid.Length == 0) continue;
                bool atMost = seg[1].Trim() == "1";
                int minCount = 1, maxDist = 0;
                float minYield = 0f;
                byte minGrade = 0;
                int.TryParse(seg[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minCount);
                int.TryParse(seg[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out maxDist);
                float.TryParse(seg[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out minYield);
                byte.TryParse(seg[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minGrade);
                if (minGrade > 0) minYield = 0f;
                bool wild = pid.StartsWith("@", StringComparison.Ordinal);
                rules.Add(new BatchRule
                {
                    PrefabId = pid,
                    IsWildcard = wild,
                    WildCat = wild ? pid.Substring(1) : null,
                    CompareAtMost = atMost,
                    MinCount = minCount,
                    MaxDist = maxDist,
                    MinYieldGps = minYield,
                    MinGrade = minGrade
                });
            }
            return rules;
        }

        static string RunBatchPoi(
            string clusterPath,
            string worldIndexStr,
            string activeDlcCsv,
            string storyArg,
            string mixingCode,
            string rulesSpec,
            string seedsCsv,
            string recordOutPath)
        {
            int worldIndex;
            if (!int.TryParse(worldIndexStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out worldIndex) || worldIndex < 0)
                return "ERR bad-world-index";
            List<BatchRule> rules = ParseBatchRules(rulesSpec);
            if (rules.Count == 0 || string.IsNullOrEmpty(seedsCsv))
                return "ERR batch-fallback";

            string[] seedStrs = seedsCsv.Split(',');
            List<int> candSeeds = new List<int>(seedStrs.Length);
            for (int i = 0; i < seedStrs.Length; i++)
            {
                int s;
                if (int.TryParse(seedStrs[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out s) && s > 0)
                    candSeeds.Add(s);
            }
            if (candSeeds.Count == 0) return "ERR batch-fallback";

            lock (s_lock)
            {
                CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
                object cgs = null;
                string prevStoryCode = null;
                string prevMixingCode = null;
                bool touchedStoryCode = false;
                bool touchedMixingCode = false;
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                    string initErr;
                    if (!InitReflection(out initErr)) return "ERR " + initErr;
                    if (!s_isHeadlessWorker && s_fWorldInst != null && s_fWorldInst.GetValue(null) != null)
                        return "ERR in-save-disabled";
                    if (s_fWorldInst != null && s_fWorldInst.GetValue(null) != null)
                        return "ERR batch-fallback";

                    if (s_mWgGenLayout == null || s_fWgMyRandom == null || s_fWgPoiBounds == null ||
                        s_mTsDetermineTemplates == null || s_fTspPos == null || s_fTspContainer == null ||
                        s_tTemplateContainer == null || s_pPrefabId == null || s_pPrefabX == null || s_pPrefabY == null)
                        return "ERR batch-fallback";

                    if (!IsSettingsCachePopulated())
                        s_mWgLoadSettingsInternal.Invoke(null, new object[] { true, false });

                    bool tcInitted = false;
                    if (s_pTcInitted != null) tcInitted = (bool)s_pTcInitted.GetValue(null, null);
                    else if (s_fTcInitted != null) tcInitted = (bool)s_fTcInitted.GetValue(null);
                    if (!tcInitted)
                        s_mTcInit.Invoke(null, null);

                    if (s_fWgVoidElem.GetValue(null) == null)
                        s_mWgSetupDefaultElems.Invoke(null, null);

                    object clusterLayoutsObj = s_fScClusters.GetValue(null);
                    if (clusterLayoutsObj == null) return "ERR no-clusterlayouts";

                    object layoutObj = s_mScGetClusterData.Invoke(clusterLayoutsObj, new object[] { clusterPath });
                    if (layoutObj == null) layoutObj = FindClusterFallback(clusterLayoutsObj, clusterPath);
                    if (layoutObj == null) return "ERR unknown-cluster:" + clusterPath;

                    cgs = GetCustomGameSettingsInstance();
                    if (cgs != null)
                    {
                        if (storyArg.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase) &&
                            s_mCgsGetStoryCode != null && s_mCgsParseStoryCode != null)
                        {
                            prevStoryCode = s_mCgsGetStoryCode.Invoke(cgs, null) as string;
                            string targetStoryCode = storyArg.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(targetStoryCode) && !string.Equals(targetStoryCode, prevStoryCode, StringComparison.Ordinal))
                            {
                                s_mCgsParseStoryCode.Invoke(cgs, new object[] { targetStoryCode });
                                touchedStoryCode = true;
                            }
                        }
                        if (!string.IsNullOrEmpty(mixingCode) &&
                            s_mCgsGetMixCode != null && s_mCgsParseMixCode != null)
                        {
                            prevMixingCode = s_mCgsGetMixCode.Invoke(cgs, null) as string;
                            if (!string.Equals(mixingCode, prevMixingCode, StringComparison.Ordinal))
                            {
                                s_mCgsParseMixCode.Invoke(cgs, new object[] { mixingCode });
                                touchedMixingCode = true;
                            }
                        }
                        if (s_mCgsGetActiveWorldMix != null)
                        {
                            try
                            {
                                IList activeMix = s_mCgsGetActiveWorldMix.Invoke(cgs, null) as IList;
                                if (activeMix != null && activeMix.Count > 0)
                                    return "ERR batch-fallback";
                            }
                            catch { }
                        }
                    }

                    Type tLayout = layoutObj.GetType();
                    IList placements = tLayout.GetProperty("worldPlacements", ALL).GetValue(layoutObj, null) as IList;
                    if (placements == null || worldIndex >= placements.Count)
                        return "ERR bad-world-idx:" + worldIndex;

                    int startWorldIndex = (int)tLayout.GetProperty("startWorldIndex", ALL).GetValue(layoutObj, null);
                    object worldsObj = s_fScWorlds.GetValue(null);

                    int savedGridW = (int)s_fGridW.GetValue(null);
                    int savedGridH = (int)s_fGridH.GetValue(null);
                    int savedGridC = (int)s_fGridC.GetValue(null);
                    float savedGridWm = s_fGridWm != null ? (float)s_fGridWm.GetValue(null) : 0f;
                    float savedGridHm = s_fGridHm != null ? (float)s_fGridHm.GetValue(null) : 0f;
                    float savedGridCellSize = s_fGridCellSize != null ? (float)s_fGridCellSize.GetValue(null) : 1f;
                    float savedGridHalfCellSize = s_fGridHalfCellSize != null ? (float)s_fGridHalfCellSize.GetValue(null) : 0.5f;

                    SavedPlacementState[] savedPlacements = new SavedPlacementState[placements.Count];
                    Type tWp = placements[0].GetType();
                    FieldInfo fWpX = tWp.GetField("<x>k__BackingField", ALL);
                    FieldInfo fWpY = tWp.GetField("<y>k__BackingField", ALL);
                    FieldInfo fWpW = tWp.GetField("<width>k__BackingField", ALL);
                    FieldInfo fWpH = tWp.GetField("<height>k__BackingField", ALL);
                    FieldInfo fWpStart = tWp.GetField("<startWorld>k__BackingField", ALL);
                    PropertyInfo pWpWorld = tWp.GetProperty("world", ALL);
                    MethodInfo mWpSetSize = tWp.GetMethod("SetSize", ALL);

                    for (int i = 0; i < placements.Count; i++)
                    {
                        object wp = placements[i];
                        savedPlacements[i] = new SavedPlacementState
                        {
                            WorldPath = pWpWorld.GetValue(wp, null) as string,
                            X = (int)fWpX.GetValue(wp),
                            Y = (int)fWpY.GetValue(wp),
                            W = (int)fWpW.GetValue(wp),
                            H = (int)fWpH.GetValue(wp),
                            StartWorld = (bool)fWpStart.GetValue(wp)
                        };
                        string wid = savedPlacements[i].WorldPath;
                        object wData = s_mScGetWorldData.Invoke(worldsObj, new object[] { wid });
                        object wSize = wData.GetType().GetProperty("worldsize", ALL).GetValue(wData, null);
                        mWpSetSize.Invoke(wp, new object[] { wSize });
                        fWpStart.SetValue(wp, i == startWorldIndex);
                    }

                    s_mBestFitWorlds.Invoke(null, new object[] { placements, false });

                    object targetWp = placements[worldIndex];
                    int wpX = (int)fWpX.GetValue(targetWp);
                    int wpY = (int)fWpY.GetValue(targetWp);
                    string targetWorldId = pWpWorld.GetValue(targetWp, null) as string;
                    object targetWorld = s_mScGetWorldData.Invoke(worldsObj, new object[] { targetWorldId });
                    try
                    {
                        PropertyInfo pMod = targetWorld.GetType().GetProperty("modifyLayoutTags", ALL);
                        if (pMod != null)
                        {
                            IList modRules = pMod.GetValue(targetWorld, null) as IList;
                            if (modRules != null && modRules.Count > 0)
                                return "ERR batch-fallback";
                        }
                    }
                    catch { }

                    Type tWorldDef = targetWorld.GetType();
                    object targetSize = tWorldDef.GetProperty("worldsize", ALL).GetValue(targetWorld, null);
                    int sizeX = (int)s_fV2ix.GetValue(targetSize);
                    int sizeY = (int)s_fV2iy.GetValue(targetSize);
                    int hiddenY = (int)tWorldDef.GetProperty("hiddenY", ALL).GetValue(targetWorld, null);
                    bool isStart = (worldIndex == startWorldIndex);
                    object posObj = s_cVector2I.Invoke(new object[] { wpX, wpY });
                    List<string> storyTraits = BuildStoryTraitList(storyArg);

                    CachedGeyserPool pool = GetOrBuildGeyserPool(activeDlcCsv);
                    List<GeyserPoolEntry> geyserPool = pool.GenericPool;
                    Dictionary<string, GeyserPoolEntry> allByPrefab = pool.AllByPrefab;
                    Dictionary<string, GeyserPoolEntry> allByRaw = pool.AllByRaw;

                    bool prevKcrIgnoreAll = false, hasKcrIgnoreAll = false;
                    bool prevDebugDisabled = false, hasDebugDisabled = false;
                    int checkedCount = 0;
                    int skippedCount = 0;

                    try
                    {
                        s_fGridW.SetValue(null, sizeX);
                        s_fGridH.SetValue(null, sizeY);
                        s_fGridC.SetValue(null, sizeX * sizeY);
                        if (s_fGridWm != null) s_fGridWm.SetValue(null, (float)sizeX);
                        if (s_fGridHm != null) s_fGridHm.SetValue(null, (float)sizeY);
                        if (s_fGridCellSize != null) s_fGridCellSize.SetValue(null, 1f);
                        if (s_fGridHalfCellSize != null) s_fGridHalfCellSize.SetValue(null, 0.5f);

                        if (s_fKcrIgnoreAll != null && s_fKcrIgnoreAll.IsStatic)
                        {
                            try { prevKcrIgnoreAll = (bool)s_fKcrIgnoreAll.GetValue(null); hasKcrIgnoreAll = true; s_fKcrIgnoreAll.SetValue(null, true); } catch { }
                        }
                        if (s_fDebugLoggingDisabled != null && s_fDebugLoggingDisabled.IsStatic)
                        {
                            try { prevDebugDisabled = (bool)s_fDebugLoggingDisabled.GetValue(null); hasDebugDisabled = true; s_fDebugLoggingDisabled.SetValue(null, true); } catch { }
                        }

                        Type tWorldTrait = s_asmFp.GetType("ProcGen.WorldTrait", true);
                        Type tPlacedStories = typeof(List<>).MakeGenericType(tWorldTrait);
                        MethodInfo mGetBoolSetting = null;
                        FieldInfo fDataWorld = null, fDataLayout = null, fDataTerrainCells = null;
                        MethodInfo mWlGetStart = null;
                        List<BatchPoi> tmpPois = new List<BatchPoi>(32);
                        HashSet<string> dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        bool doRecord = !string.IsNullOrEmpty(recordOutPath);
                        StringBuilder recSb = doRecord ? new StringBuilder(2048) : null;

                        for (int ci = 0; ci < candSeeds.Count; ci++)
                        {
                            int candSeed = candSeeds[ci];
                            int geyserGlobalSeed = unchecked(candSeed + Math.Max(0, placements.Count - 1));
                            int wSeed = unchecked(candSeed + worldIndex);

                            List<string> chosenTraits = s_mScGetRandomTraits.Invoke(null, new object[] { wSeed, targetWorld }) as List<string>;
                            if (chosenTraits == null) chosenTraits = new List<string>();

                            object wg = s_cWg4.Invoke(new object[] { targetWorldId, chosenTraits, storyTraits, false });
                            s_mWgSetSize.Invoke(wg, new object[] { sizeX, sizeY });
                            s_mWgSetPos.Invoke(wg, new object[] { posObj });
                            s_mWgSetHiddenY.Invoke(wg, new object[] { hiddenY });
                            if (s_fWgIsStarting != null) s_fWgIsStarting.SetValue(wg, isStart);

                            s_lastGenError = null;
                            s_mWgInitialise.Invoke(wg, new object[] { s_progressCb, s_errorCb, wSeed, wSeed, wSeed, wSeed, false, false });

                            bool okLayout = (bool)s_mWgGenLayout.Invoke(wg, new object[] { s_progressCb });
                            if (!okLayout) { skippedCount++; continue; }

                            s_mWgFinalizeStart.Invoke(wg, null);

                            object wgData = s_pWgData.GetValue(wg, null);
                            object wgSettings = s_pWgSettings.GetValue(wg, null);
                            object poiBounds = Activator.CreateInstance(s_fWgPoiBounds.FieldType);
                            s_fWgPoiBounds.SetValue(wg, poiBounds);

                            bool drawBorder = false;
                            if (wgSettings != null)
                            {
                                if (mGetBoolSetting == null)
                                    mGetBoolSetting = wgSettings.GetType().GetMethod("GetBoolSetting", ALL, null, new Type[] { typeof(string) }, null);
                                if (mGetBoolSetting != null)
                                    try { drawBorder = (bool)mGetBoolSetting.Invoke(wgSettings, new object[] { "DrawWorldBorder" }); } catch { }
                            }

                            if (drawBorder && s_mWgDrawWorldBorder != null && s_cSeededRandomInt != null && s_mSimDataInit != null)
                            {
                                PrepareFastSimDataForBorder(sizeX * sizeY);
                                if (fDataWorld == null) fDataWorld = wgData.GetType().GetField("world", ALL);
                                object chunkWorld = fDataWorld.GetValue(wgData);
                                object borderRng = s_cSeededRandomInt.Invoke(new object[] { 0 });
                                HashSet<int> borderCells = new HashSet<int>();
                                object[] bArgs = new object[] { s_cachedSimData, chunkWorld, borderRng, borderCells, poiBounds, s_progressCb };
                                s_mWgDrawWorldBorder.Invoke(wg, bArgs);
                                poiBounds = bArgs[4];
                                s_fWgPoiBounds.SetValue(wg, poiBounds);
                            }

                            int baseStartX = sizeX / 2, baseStartY = sizeY / 2;
                            if (fDataLayout == null) fDataLayout = wgData.GetType().GetField("worldLayout", ALL);
                            object worldLayout = fDataLayout != null ? fDataLayout.GetValue(wgData) : null;
                            if (worldLayout != null)
                            {
                                if (mWlGetStart == null) mWlGetStart = worldLayout.GetType().GetMethod("GetStartLocation", ALL);
                                if (mWlGetStart != null)
                                {
                                    object startPos = mWlGetStart.Invoke(worldLayout, null);
                                    if (startPos != null)
                                    {
                                        baseStartX = (int)s_fV2ix.GetValue(startPos);
                                        baseStartY = (int)s_fV2iy.GetValue(startPos);
                                    }
                                }
                            }

                            if (fDataTerrainCells == null) fDataTerrainCells = wgData.GetType().GetField("terrainCells", ALL);
                            IList terrainCells = fDataTerrainCells.GetValue(wgData) as IList;
                            object myRandom = s_fWgMyRandom.GetValue(wg);
                            object placedStories = Activator.CreateInstance(tPlacedStories);
                            object[] tsArgs = new object[] { wgSettings, terrainCells, myRandom, poiBounds, false, placedStories, s_progressCb };

                            IList spawners = null;
                            try
                            {
                                spawners = s_mTsDetermineTemplates.Invoke(null, tsArgs) as IList;
                            }
                            catch
                            {
                                skippedCount++;
                                continue;
                            }
                            if (spawners == null) { skippedCount++; continue; }

                            checkedCount++;


                            tmpPois.Clear();
                            dedup.Clear();
                            for (int si = 0; si < spawners.Count; si++)
                            {
                                object sp = spawners[si];
                                object spPos = s_fTspPos.GetValue(sp);
                                object spCont = s_fTspContainer.GetValue(sp);
                                if (spPos == null || spCont == null) continue;
                                int posX = (int)s_fV2ix.GetValue(spPos);
                                int posY = (int)s_fV2iy.GetValue(spPos);

                                for (int li = 0; li < 3; li++)
                                {
                                    PropertyInfo pList = li == 0 ? s_pTcBuildings : (li == 1 ? s_pTcOtherEntities : s_pTcPickupables);
                                    IList pfs = pList != null ? (pList.GetValue(spCont, null) as IList) : null;
                                    if (pfs == null) continue;
                                    for (int pi = 0; pi < pfs.Count; pi++)
                                    {
                                        object pf = pfs[pi];
                                        string id = s_pPrefabId.GetValue(pf, null) as string;
                                        if (string.IsNullOrEmpty(id)) continue;
                                        int absX = posX + (int)s_pPrefabX.GetValue(pf, null);
                                        int absY = posY + (int)s_pPrefabY.GetValue(pf, null);

                                        byte isRand = 0;
                                        GeyserPoolEntry pickedPoolEntry = null;
                                        if (string.Equals(id, "GeyserGeneric", StringComparison.Ordinal))
                                        {
                                            if (geyserPool.Count > 0)
                                            {
                                                int gSeed = unchecked(geyserGlobalSeed + (wpX + absX) + (wpY + absY));
                                                object pk = s_cKRandomInt.Invoke(new object[] { gSeed });
                                                int pick = (int)s_mKRandomNext2.Invoke(pk, new object[] { 0, geyserPool.Count });
                                                if (pick >= 0 && pick < geyserPool.Count)
                                                {
                                                    pickedPoolEntry = geyserPool[pick];
                                                    id = pickedPoolEntry.PrefabId;
                                                }
                                            }
                                            isRand = 1;
                                        }

                                        string cat = ClassifyPoi(id);
                                        if (cat == null) continue;
                                        if (string.Equals(id, "Headquarters", StringComparison.Ordinal))
                                        {
                                            baseStartX = absX;
                                            baseStartY = absY;
                                        }
                                        string dkey = id + "@" + absX + "," + absY;
                                        if (!dedup.Add(dkey)) continue;

                                        tmpPois.Add(new BatchPoi
                                        {
                                            PrefabId = id,
                                            Category = cat,
                                            X = absX,
                                            Y = absY,
                                            IsRandomGeyser = isRand,
                                            PickedPoolEntry = pickedPoolEntry
                                        });
                                    }
                                }
                            }

                            bool isMatch = MatchesAllBatchRules(tmpPois, rules, baseStartX, baseStartY, geyserGlobalSeed, wpX, wpY, allByPrefab, allByRaw);

                            if (doRecord)
                            {
                                recSb.Append(candSeed.ToString(CultureInfo.InvariantCulture)).Append('\t')
                                     .Append(worldIndex.ToString(CultureInfo.InvariantCulture)).Append('\t')
                                     .Append(targetWorldId ?? "").Append('\t')
                                     .Append(chosenTraits.Count > 0 ? string.Join(",", chosenTraits.ToArray()) : "").Append('\t');
                                bool firstPoi = true;
                                for (int pi = 0; pi < tmpPois.Count; pi++)
                                {
                                    BatchPoi bp = tmpPois[pi];
                                    if (bp.Category == "start") continue;
                                    EnsureBatchPoiStats(bp, geyserGlobalSeed, wpX, wpY, allByPrefab, allByRaw);
                                    int dx = bp.X - baseStartX;
                                    int dy = bp.Y - baseStartY;
                                    int dist = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
                                    if (!firstPoi) recSb.Append(';');
                                    firstPoi = false;
                                    recSb.Append(bp.PrefabId ?? "").Append('~')
                                         .Append(bp.Category ?? "").Append('~')
                                         .Append(bp.X.ToString(CultureInfo.InvariantCulture)).Append('~')
                                         .Append(bp.Y.ToString(CultureInfo.InvariantCulture)).Append('~')
                                         .Append(dist.ToString(CultureInfo.InvariantCulture)).Append('~')
                                         .Append(bp.AvgYieldGps.ToString("0.##", CultureInfo.InvariantCulture)).Append('~')
                                         .Append(bp.Grade.ToString(CultureInfo.InvariantCulture)).Append('~')
                                         .Append(bp.Percentile.ToString("0.#", CultureInfo.InvariantCulture));
                                }
                                recSb.Append('\n');
                            }

                            if (isMatch)
                            {
                                FlushBatchRecordFile(recordOutPath, recSb);
                                return string.Format(CultureInfo.InvariantCulture,
                                    "OK bmatch:{0}|{1}|{2}", candSeed, checkedCount, skippedCount);
                            }
                        }

                        FlushBatchRecordFile(recordOutPath, recSb);
                        return string.Format(CultureInfo.InvariantCulture,
                            "OK bmiss:0|{0}|{1}", checkedCount, skippedCount);
                    }
                    finally
                    {
                        if (hasDebugDisabled && s_fDebugLoggingDisabled != null)
                            try { s_fDebugLoggingDisabled.SetValue(null, prevDebugDisabled); } catch { }
                        if (hasKcrIgnoreAll && s_fKcrIgnoreAll != null)
                            try { s_fKcrIgnoreAll.SetValue(null, prevKcrIgnoreAll); } catch { }

                        for (int i = 0; i < placements.Count; i++)
                        {
                            object wp = placements[i];
                            if (!string.IsNullOrEmpty(savedPlacements[i].WorldPath))
                                pWpWorld.SetValue(wp, savedPlacements[i].WorldPath, null);
                            fWpX.SetValue(wp, savedPlacements[i].X);
                            fWpY.SetValue(wp, savedPlacements[i].Y);
                            fWpW.SetValue(wp, savedPlacements[i].W);
                            fWpH.SetValue(wp, savedPlacements[i].H);
                            fWpStart.SetValue(wp, savedPlacements[i].StartWorld);
                        }
                        s_fGridW.SetValue(null, savedGridW);
                        s_fGridH.SetValue(null, savedGridH);
                        s_fGridC.SetValue(null, savedGridC);
                        if (s_fGridWm != null) s_fGridWm.SetValue(null, savedGridWm);
                        if (s_fGridHm != null) s_fGridHm.SetValue(null, savedGridHm);
                        if (s_fGridCellSize != null) s_fGridCellSize.SetValue(null, savedGridCellSize);
                        if (s_fGridHalfCellSize != null) s_fGridHalfCellSize.SetValue(null, savedGridHalfCellSize);
                    }
                }
                catch
                {
                    return "ERR batch-fallback";
                }
                finally
                {
                    if (cgs != null)
                    {
                        if (touchedStoryCode && s_mCgsParseStoryCode != null)
                            try { s_mCgsParseStoryCode.Invoke(cgs, new object[] { prevStoryCode ?? "" }); } catch { }
                        if (touchedMixingCode && s_mCgsParseMixCode != null)
                            try { s_mCgsParseMixCode.Invoke(cgs, new object[] { prevMixingCode ?? "" }); } catch { }
                    }
                    Thread.CurrentThread.CurrentCulture = prevCulture;
                }
            }
        }

        static bool MatchesAllBatchRules(
            List<BatchPoi> pois,
            List<BatchRule> rules,
            int baseStartX,
            int baseStartY,
            int candSeed,
            int wpX,
            int wpY,
            Dictionary<string, GeyserPoolEntry> allByPrefab,
            Dictionary<string, GeyserPoolEntry> allByRaw)
        {
            for (int ri = 0; ri < rules.Count; ri++)
            {
                BatchRule r = rules[ri];
                int cnt = 0;
                for (int pi = 0; pi < pois.Count; pi++)
                {
                    BatchPoi p = pois[pi];
                    if (r.IsWildcard)
                    {
                        if (!string.Equals(p.Category, r.WildCat, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    else
                    {
                        if (!string.Equals(p.PrefabId, r.PrefabId, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (r.MaxDist > 0)
                    {
                        int dx = p.X - baseStartX;
                        int dy = p.Y - baseStartY;
                        int dist = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
                        if (dist > r.MaxDist) continue;
                    }
                    if (r.MinYieldGps > 0f || r.MinGrade > 0)
                    {
                        EnsureBatchPoiStats(p, candSeed, wpX, wpY, allByPrefab, allByRaw);
                        if (p.Grade == 0) continue;
                        if (r.MinYieldGps > 0f && p.AvgYieldGps < r.MinYieldGps) continue;
                        if (r.MinGrade > 0 && p.Grade > r.MinGrade) continue;
                    }
                    cnt++;
                }
                if (r.CompareAtMost ? (cnt > r.MinCount) : (cnt < r.MinCount))
                    return false;
            }
            return true;
        }

        static void EnsureBatchPoiStats(
            BatchPoi p,
            int candSeed,
            int wpX,
            int wpY,
            Dictionary<string, GeyserPoolEntry> allByPrefab,
            Dictionary<string, GeyserPoolEntry> allByRaw)
        {
            if (p.StatsComputed) return;
            p.StatsComputed = true;
            if (p.Category != "water_geyser" && p.Category != "gas_geyser" &&
                p.Category != "metal_volcano" && p.Category != "volcano")
                return;

            object gtHash = null, gtObj = null;
            float pMin = 0f, pMax = 1f;
            if (p.IsRandomGeyser == 1 && p.PickedPoolEntry != null && p.PickedPoolEntry.GeyserTypeObj != null)
            {
                gtObj = p.PickedPoolEntry.GeyserTypeObj;
                gtHash = s_fGtIdHash.GetValue(gtObj);
            }
            else if (!TryResolveFixedGeyserPreset(p.PrefabId, allByPrefab, allByRaw, out gtHash, out gtObj, out pMin, out pMax))
            {
                return;
            }

            int gSeed2 = unchecked(candSeed + (wpX + p.X) + (wpY + p.Y));
            object krandStats = s_cKRandomInt.Invoke(new object[] { gSeed2 });
            PoiOut tmp = new PoiOut { X = p.X, Y = p.Y };
            PopulateGeyserStats(tmp, krandStats, gtHash, gtObj, pMin, pMax);
            if (tmp.HasGeyserStats == 1)
            {
                p.AvgYieldGps = tmp.AvgYieldGps;
                p.Grade = tmp.Grade;
                p.Percentile = tmp.Percentile;
            }
        }

        static void FlushBatchRecordFile(string path, StringBuilder sb)
        {
            if (string.IsNullOrEmpty(path) || sb == null || sb.Length == 0) return;
            try
            {
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static string SanitizeSingleLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string clean = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (clean.Contains("  "))
                clean = clean.Replace("  ", " ");
            if (clean.Length > 240)
                clean = clean.Substring(0, 240) + "...";
            return clean;
        }

        static void AddRangeSafe(IList dst, IEnumerable src)
        {
            if (dst == null || src == null) return;
            foreach (object item in src) dst.Add(item);
        }

        static void TruncateList(IList list, int origCount)
        {
            if (list == null || origCount < 0) return;
            try
            {
                while (list.Count > origCount)
                    list.RemoveAt(list.Count - 1);
            }
            catch { }
        }

        static object FindClusterFallback(object clusterLayoutsObj, string target)
        {
            IDictionary dict = clusterLayoutsObj.GetType().GetField("clusterCache", ALL).GetValue(clusterLayoutsObj) as IDictionary;
            if (dict == null) return null;
            foreach (DictionaryEntry de in dict)
            {
                string key = de.Key as string;
                if (string.Equals(key, target, StringComparison.OrdinalIgnoreCase))
                    return de.Value;
            }
            return null;
        }

        static string NormalizeStoryTraitKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string clean = s.Trim();
            if (clean.Length == 0) return "";
            if (clean.IndexOf('/') >= 0) return clean;
            if (string.Equals(clean, "CreatureManipulator", StringComparison.OrdinalIgnoreCase))
                return "storytraits/CritterManipulator";
            return "storytraits/" + clean;
        }

        static List<string> BuildStoryTraitList(string storyArg)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(storyArg) && !storyArg.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = storyArg.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = NormalizeStoryTraitKey(parts[i]);
                    if (s.Length > 0) list.Add(s);
                }
                return list;
            }
            try
            {
                object cgs = GetCustomGameSettingsInstance();
                Type tDb = s_asmCs.GetType("Db", false);
                if (cgs != null && tDb != null)
                {
                    MethodInfo mDbGet = tDb.GetMethod("Get", ALL);
                    object db = mDbGet != null ? mDbGet.Invoke(null, null) : null;
                    if (db != null && s_mCgsGetStories != null)
                    {
                        IEnumerable stories = s_mCgsGetStories.Invoke(cgs, null) as IEnumerable;
                        object storiesDb = tDb.GetField("Stories", ALL).GetValue(db);
                        MethodInfo mGetStory = storiesDb != null ? storiesDb.GetType().GetMethod("Get", ALL, null, new Type[] { typeof(string) }, null) : null;
                        if (stories != null && mGetStory != null)
                        {
                            foreach (object sid in stories)
                            {
                                object st = mGetStory.Invoke(storiesDb, new object[] { sid as string });
                                if (st != null)
                                {
                                    string key = st.GetType().GetField("worldgenStoryTraitKey", ALL).GetValue(st) as string;
                                    if (!string.IsNullOrEmpty(key)) list.Add(key);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        static string ResolveWorldDisplayName(object worldObj, string worldId)
        {
            try
            {
                string nameKey = worldObj.GetType().GetProperty("name", ALL).GetValue(worldObj, null) as string;
                string loc = TryGetString(nameKey);
                if (!string.IsNullOrEmpty(loc)) return CleanRichText(loc);
            }
            catch { }
            int slash = worldId != null ? worldId.LastIndexOf('/') : -1;
            return slash >= 0 ? worldId.Substring(slash + 1) : (worldId ?? "");
        }

        static string FormatTraitsSummary(List<string> traitIds)
        {
            if (traitIds == null || traitIds.Count == 0) return "";
            List<string> names = new List<string>(traitIds.Count);
            MethodInfo mGetTrait = s_tSettingsCache.GetMethod("GetCachedWorldTrait", ALL, null, new Type[] { typeof(string), typeof(bool) }, null);
            for (int i = 0; i < traitIds.Count; i++)
            {
                string id = traitIds[i];
                string disp = id;
                try
                {
                    if (mGetTrait != null)
                    {
                        object wt = mGetTrait.Invoke(null, new object[] { id, false });
                        if (wt != null)
                        {
                            string nk = wt.GetType().GetProperty("name", ALL).GetValue(wt, null) as string;
                            string tr = TryGetString(nk);
                            if (!string.IsNullOrEmpty(tr)) disp = CleanRichText(tr);
                        }
                    }
                }
                catch { }
                names.Add(disp);
            }
            return string.Join("、", names.ToArray());
        }

        static byte[] RasterizeBiomes(object wg, object wgData, int w, int h, int wpX, int wpY)
        {
            byte[] grid = new byte[w * h];
            for (int i = 0; i < grid.Length; i++) grid[i] = 255;
            if (wgData == null) return grid;

            if (s_fWgOverworldCells == null)
                s_fWgOverworldCells = wgData.GetType().GetField("overworldCells", ALL);
            IList ovCells = s_fWgOverworldCells != null ? (s_fWgOverworldCells.GetValue(wgData) as IList) : null;
            if (ovCells == null || ovCells.Count == 0) return grid;

            object wgSettings = s_pWgSettings != null ? s_pWgSettings.GetValue(wg, null) : null;
            if (!s_rasterInit)
            {
                Type tTc = ovCells[0].GetType();
                s_pTcPoly = tTc.GetProperty("poly", ALL);
                s_pTcNode = tTc.GetProperty("node", ALL);
                if (wgSettings != null)
                {
                    Type tws = wgSettings.GetType();
                    s_mWgsHasSubworld = tws.GetMethod("HasSubworld", ALL, null, new Type[] { typeof(string) }, null);
                    s_mWgsGetSubWorld = tws.GetMethod("GetSubWorld", ALL, null, new Type[] { typeof(string) }, null);
                }
                Type tPoly = s_asmFp.GetType("Delaunay.Geo.Polygon", true);
                s_fPolyVerts = tPoly.GetField("vertices", ALL);
                s_pPolyBounds = tPoly.GetProperty("bounds", ALL);
                Type tRect = s_asmCore.GetType("UnityEngine.Rect", true);
                s_pRectXMax = tRect.GetProperty("xMax", ALL);
                s_pRectYMax = tRect.GetProperty("yMax", ALL);
                Type tVec2 = s_asmCore.GetType("UnityEngine.Vector2", true);
                s_fVec2X = tVec2.GetField("x", ALL);
                s_fVec2Y = tVec2.GetField("y", ALL);
                s_rasterInit = true;
            }

            float maxPolyX = 0f, maxPolyY = 0f;
            for (int i = 0; i < ovCells.Count; i++)
            {
                object poly = s_pTcPoly != null ? s_pTcPoly.GetValue(ovCells[i], null) : null;
                if (poly == null) continue;
                object rect = s_pPolyBounds.GetValue(poly, null);
                float xmax = (float)s_pRectXMax.GetValue(rect, null);
                float ymax = (float)s_pRectYMax.GetValue(rect, null);
                if (xmax > maxPolyX) maxPolyX = xmax;
                if (ymax > maxPolyY) maxPolyY = ymax;
            }
            float offX = (wpX > 0 && maxPolyX > w + 4f) ? (float)wpX : 0f;
            float offY = (wpY > 0 && maxPolyY > h + 4f) ? (float)wpY : 0f;

            float[] vx = new float[64];
            float[] vy = new float[64];
            float[] xs = new float[64];

            for (int c = 0; c < ovCells.Count; c++)
            {
                object ov = ovCells[c];
                byte zone = 255;
                if (wgSettings != null && s_mWgsGetSubWorld != null && s_pTcNode != null)
                {
                    object nodeObj = s_pTcNode.GetValue(ov, null);
                    if (nodeObj != null)
                    {
                        if (s_pNodeType == null) s_pNodeType = nodeObj.GetType().GetProperty("type", ALL);
                        string subName = s_pNodeType != null ? (s_pNodeType.GetValue(nodeObj, null) as string) : null;
                        if (!string.IsNullOrEmpty(subName) &&
                            (s_mWgsHasSubworld == null || (bool)s_mWgsHasSubworld.Invoke(wgSettings, new object[] { subName })))
                        {
                            object subWorld = s_mWgsGetSubWorld.Invoke(wgSettings, new object[] { subName });
                            if (subWorld != null)
                            {
                                if (s_pSubZoneType == null) s_pSubZoneType = subWorld.GetType().GetProperty("zoneType", ALL);
                                if (s_pSubZoneType != null) zone = Convert.ToByte(s_pSubZoneType.GetValue(subWorld, null));
                            }
                        }
                    }
                }
                object poly = s_pTcPoly != null ? s_pTcPoly.GetValue(ov, null) : null;
                if (poly == null) continue;

                IList verts = s_fPolyVerts.GetValue(poly) as IList;
                if (verts == null || verts.Count < 3) continue;

                int vn = verts.Count;
                if (vn > vx.Length)
                {
                    vx = new float[vn];
                    vy = new float[vn];
                    xs = new float[vn];
                }
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int v = 0; v < vn; v++)
                {
                    object pt = verts[v];
                    float px = (float)s_fVec2X.GetValue(pt) - offX;
                    float py = (float)s_fVec2Y.GetValue(pt) - offY;
                    vx[v] = px;
                    vy[v] = py;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }

                int y0 = Math.Max(0, (int)Math.Floor(minY));
                int y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY));

                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    int nx = 0;
                    for (int i = 0, j = vn - 1; i < vn; j = i++)
                    {
                        float yi = vy[i], yj = vy[j];
                        if ((yi > py) != (yj > py))
                            xs[nx++] = (vx[j] - vx[i]) * (py - yi) / (yj - yi) + vx[i];
                    }
                    if (nx < 2) continue;
                    for (int a = 1; a < nx; a++)
                    {
                        float key = xs[a];
                        int b = a - 1;
                        while (b >= 0 && xs[b] > key)
                        {
                            xs[b + 1] = xs[b];
                            b--;
                        }
                        xs[b + 1] = key;
                    }
                    int rowOff = y * w;
                    for (int m = 0; m + 1 < nx; m += 2)
                    {
                        int xStart = Math.Max(0, (int)Math.Ceiling(xs[m] - 0.5f));
                        int xEnd = Math.Min(w - 1, (int)Math.Floor(xs[m + 1] - 0.5f));
                        for (int x = xStart; x <= xEnd; x++)
                            grid[rowOff + x] = zone;
                    }
                }
            }
            return grid;
        }

        static CachedGeyserPool GetOrBuildGeyserPool(string activeDlcCsv)
        {
            string key = activeDlcCsv ?? "";
            CachedGeyserPool cached;
            if (s_geyserPoolCache.TryGetValue(key, out cached))
                return cached;

            HashSet<string> activeDlcs = ParseDlcCsv(key);
            List<GeyserPoolEntry> gp;
            Dictionary<string, GeyserPoolEntry> byPrefab;
            Dictionary<string, GeyserPoolEntry> byRaw;
            BuildGeyserPools(activeDlcs, out gp, out byPrefab, out byRaw);

            cached = new CachedGeyserPool
            {
                GenericPool = gp,
                AllByPrefab = byPrefab,
                AllByRaw = byRaw
            };
            s_geyserPoolCache[key] = cached;
            return cached;
        }

        static HashSet<string> ParseDlcCsv(string csv)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(csv)) return set;
            set.Add("");
            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length > 0 && !string.Equals(s, "VANILLA", StringComparison.OrdinalIgnoreCase))
                    set.Add(s);
            }
            return set;
        }

        static void BuildGeyserPools(
            HashSet<string> activeDlcs,
            out List<GeyserPoolEntry> genericPool,
            out Dictionary<string, GeyserPoolEntry> allByPrefab,
            out Dictionary<string, GeyserPoolEntry> allByRaw)
        {
            genericPool = new List<GeyserPoolEntry>();
            allByPrefab = new Dictionary<string, GeyserPoolEntry>(StringComparer.OrdinalIgnoreCase);
            allByRaw = new Dictionary<string, GeyserPoolEntry>(StringComparer.OrdinalIgnoreCase);

            object gcfg = Activator.CreateInstance(s_tGeyserGenericConfig);
            IList configs = s_mGeyserGenConfigs.Invoke(gcfg, null) as IList;
            if (configs == null) return;

            for (int i = 0; i < configs.Count; i++)
            {
                object p = configs[i];
                Type tp = p.GetType();
                bool isGeneric = (bool)tp.GetField("isGenericGeyser", ALL).GetValue(p);
                object gt = tp.GetField("geyserType", ALL).GetValue(p);
                Type tgt = gt.GetType();

                string prefabId = tp.GetField("id", ALL).GetValue(p) as string;
                string rawId = tgt.GetField("id", ALL).GetValue(gt) as string;
                object skObj = tp.GetField("nameStringKey", ALL).GetValue(p);
                string nameKey = skObj != null ? (skObj.GetType().GetField("String", ALL).GetValue(skObj) as string) : null;
                string locName = TryGetString(nameKey);

                GeyserPoolEntry entry = new GeyserPoolEntry
                {
                    PrefabId = prefabId ?? "",
                    RawId = rawId ?? "",
                    LocalizedName = !string.IsNullOrEmpty(locName) ? CleanRichText(locName) : null,
                    GeyserTypeObj = gt
                };
                if (!string.IsNullOrEmpty(entry.PrefabId)) allByPrefab[entry.PrefabId] = entry;
                if (!string.IsNullOrEmpty(entry.RawId)) allByRaw[entry.RawId] = entry;

                if (!isGeneric) continue;

                MethodInfo mReq = tgt.GetMethod("GetRequiredDlcIds", ALL);
                MethodInfo mForb = tgt.GetMethod("GetForbiddenDlcIds", ALL);
                string[] reqDlcs = mReq != null ? (mReq.Invoke(gt, null) as string[]) : null;
                string[] forbDlcs = mForb != null ? (mForb.Invoke(gt, null) as string[]) : null;

                if (activeDlcs != null && activeDlcs.Count > 0)
                {
                    bool ok = true;
                    if (reqDlcs != null && reqDlcs.Length > 0)
                    {
                        for (int k = 0; k < reqDlcs.Length; k++)
                        {
                            if (!string.IsNullOrEmpty(reqDlcs[k]) && !activeDlcs.Contains(reqDlcs[k])) { ok = false; break; }
                        }
                    }
                    if (ok && forbDlcs != null && forbDlcs.Length > 0)
                    {
                        for (int k = 0; k < forbDlcs.Length; k++)
                        {
                            if (!string.IsNullOrEmpty(forbDlcs[k]) && activeDlcs.Contains(forbDlcs[k])) { ok = false; break; }
                        }
                    }
                    if (!ok) continue;
                }

                genericPool.Add(entry);
            }
        }

        static object FindGeyserTypeById(string rawTypeId)
        {
            if (string.IsNullOrEmpty(rawTypeId) || s_fGcTypesList == null || s_fGtId == null) return null;
            try
            {
                IList list = s_fGcTypesList.GetValue(null) as IList;
                if (list == null) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    object gt = list[i];
                    string id = s_fGtId.GetValue(gt) as string;
                    if (string.Equals(id, rawTypeId, StringComparison.OrdinalIgnoreCase))
                        return gt;
                }
            }
            catch { }
            return null;
        }

        static bool TryResolveFixedGeyserPreset(
            string prefabId,
            Dictionary<string, GeyserPoolEntry> allByPrefab,
            Dictionary<string, GeyserPoolEntry> allByRaw,
            out object typeIdHash,
            out object geyserTypeObj,
            out float presetMin,
            out float presetMax)
        {
            typeIdHash = null;
            geyserTypeObj = null;
            presetMin = 0f;
            presetMax = 1f;


            if (s_mAssetsGetPrefab != null && s_cTagStr != null && s_tGeyserCfg != null)
            {
                try
                {
                    object tag = s_cTagStr.Invoke(new object[] { prefabId });
                    object go = s_mAssetsGetPrefab.Invoke(null, new object[] { tag });
                    if (go != null)
                    {
                        MethodInfo mGetComp = go.GetType().GetMethod("GetComponent", ALL, null, new Type[] { typeof(Type) }, null);
                        object gc = mGetComp != null ? mGetComp.Invoke(go, new object[] { s_tGeyserCfg }) : null;
                        if (gc != null)
                        {
                            object hs = s_fGcPresetType.GetValue(gc);
                            if (hs != null)
                            {
                                typeIdHash = hs;
                                presetMin = (float)s_fGcPresetMin.GetValue(gc);
                                presetMax = (float)s_fGcPresetMax.GetValue(gc);
                                if (s_mGcFindType != null)
                                {
                                    try { geyserTypeObj = s_mGcFindType.Invoke(null, new object[] { hs }); } catch { }
                                }
                                return true;
                            }
                        }
                    }
                }
                catch { }
            }


            string guessRaw = null;
            if (prefabId.StartsWith("GeyserGeneric_", StringComparison.Ordinal)) guessRaw = prefabId.Substring("GeyserGeneric_".Length);
            else if (string.Equals(prefabId, "Geyser", StringComparison.Ordinal)) { guessRaw = "steam"; presetMin = 0.5f; presetMax = 0.75f; }
            else if (string.Equals(prefabId, "MethaneGeyser", StringComparison.Ordinal)) { guessRaw = "methane"; presetMin = 0.35f; presetMax = 0.65f; }
            else if (string.Equals(prefabId, "ChlorineGeyser", StringComparison.Ordinal)) { guessRaw = "chlorine_gas"; presetMin = 0.35f; presetMax = 0.65f; }
            else if (string.Equals(prefabId, "SmallReefGeyser", StringComparison.Ordinal)) { guessRaw = "salt_water"; presetMin = 0f; presetMax = 1f; }
            else if (string.Equals(prefabId, "NiobiumGeyser", StringComparison.Ordinal)) { guessRaw = "molten_niobium"; presetMin = 0f; presetMax = 1f; }

            GeyserPoolEntry e;
            if (allByPrefab.TryGetValue(prefabId, out e) && e.GeyserTypeObj != null)
            {
                geyserTypeObj = e.GeyserTypeObj;
                typeIdHash = s_fGtIdHash.GetValue(geyserTypeObj);
                return true;
            }

            if (!string.IsNullOrEmpty(guessRaw))
            {
                if (allByRaw.TryGetValue(guessRaw, out e) && e.GeyserTypeObj != null)
                {
                    geyserTypeObj = e.GeyserTypeObj;
                    typeIdHash = s_fGtIdHash.GetValue(geyserTypeObj);
                    return true;
                }
                geyserTypeObj = FindGeyserTypeById(guessRaw);
                if (geyserTypeObj != null)
                {
                    typeIdHash = s_fGtIdHash.GetValue(geyserTypeObj);
                    return true;
                }
            }

            return false;
        }

        static float RollGeyser(object krand, float min, float max)
        {
            double u = (double)s_mKRandomNextDbl.Invoke(krand, null);
            return min + (float)(u * (double)(max - min));
        }

        static void PopulateGeyserStats(PoiOut poi, object krand, object typeIdHash, object geyserTypeObj, float pMin, float pMax)
        {
            if (s_tGeyserInstCfg == null || s_mKRandomNextDbl == null || typeIdHash == null) return;
            try
            {
                if (krand == null)
                    krand = s_cKRandomInt.Invoke(new object[] { poi.X + poi.Y });







                float rRate = RollGeyser(krand, pMin, pMax);
                float rIterLen = RollGeyser(krand, 0f, 1f);
                float rIterPct = RollGeyser(krand, pMin, pMax);
                float rYearLen = RollGeyser(krand, 0f, 1f);
                float rYearPct = RollGeyser(krand, pMin, pMax);

                object cfg = Activator.CreateInstance(s_tGeyserInstCfg);
                s_fGicTypeId.SetValue(cfg, typeIdHash);
                s_fGicRateRoll.SetValue(cfg, rRate);
                s_fGicIterLenRoll.SetValue(cfg, rIterLen);
                s_fGicIterPctRoll.SetValue(cfg, rIterPct);
                s_fGicYearLenRoll.SetValue(cfg, rYearLen);
                s_fGicYearPctRoll.SetValue(cfg, rYearPct);

                s_mGicInit.Invoke(cfg, new object[] { false });

                float avgKgS = (float)s_mGicGetAvg.Invoke(cfg, null);
                float emitKgS = (float)s_mGicGetEmit.Invoke(cfg, null);
                float tempK = (float)s_mGicGetTemp.Invoke(cfg, null);
                float onSec = (float)s_mGicGetOnDur.Invoke(cfg, null);
                float iterSec = (float)s_mGicGetIterLen.Invoke(cfg, null);
                float yOnSec = (float)s_mGicGetYearOnDur.Invoke(cfg, null);
                float yLenSec = (float)s_mGicGetYearLen.Invoke(cfg, null);

                poi.HasGeyserStats = 1;
                poi.AvgYieldGps = avgKgS * 1000f;
                poi.EruptRateGps = emitKgS * 1000f;
                poi.OutputTempC = tempK - 273.15f;
                poi.EruptSec = onSec;
                poi.IdleSec = Math.Max(0f, iterSec - onSec);
                poi.ActiveCycles = yOnSec / 600f;
                poi.DormantCycles = Math.Max(0f, (yLenSec - yOnSec) / 600f);

                object gt = geyserTypeObj;
                if (gt == null && s_mGicGetType != null)
                {
                    try { gt = s_mGicGetType.Invoke(cfg, null); } catch { }
                }

                float pct = ComputeAnalyticalPercentile(gt, avgKgS, pMin, pMax);
                poi.Percentile = pct;
                if (pct >= 85f) poi.Grade = 1;
                else if (pct >= 65f) poi.Grade = 2;
                else if (pct >= 35f) poi.Grade = 3;
                else poi.Grade = 4;
            }
            catch { }
        }

        static float ComputeAnalyticalPercentile(object gt, float avgKgS, float pMin, float pMax)
        {
            if (gt == null || s_fGtMinRate == null) return 50f;
            try
            {
                float minRate = (float)s_fGtMinRate.GetValue(gt);
                float maxRate = (float)s_fGtMaxRate.GetValue(gt);
                float minYp = (float)s_fGtMinYearPct.GetValue(gt);
                float maxYp = (float)s_fGtMaxYearPct.GetValue(gt);

                float loR = minRate + (maxRate - minRate) * pMin;
                float hiR = minRate + (maxRate - minRate) * pMax;
                float loYp = minYp + (maxYp - minYp) * pMin;
                float hiYp = minYp + (maxYp - minYp) * pMax;

                if (Math.Abs(hiR - loR) < 1e-4f && Math.Abs(hiYp - loYp) < 1e-4f)
                    return 50f;

                int below = 0;
                int n = s_quantileSteps.Length;
                for (int i = 0; i < n; i++)
                {
                    float rDiv600 = (loR + (hiR - loR) * s_quantileSteps[i]) / 600f;
                    for (int j = 0; j < n; j++)
                    {
                        float yp = loYp + (hiYp - loYp) * s_quantileSteps[j];
                        if (rDiv600 * yp <= avgKgS + 1e-6f) below++;
                    }
                }
                return (below * 100f) / (n * n);
            }
            catch
            {
                return 50f;
            }
        }

        static List<PoiOut> ExtractPois(
            object wgData, int seed, int wpX, int wpY, int w, int h,
            string activeDlcCsv, out int baseStartX, out int baseStartY)
        {
            baseStartX = w / 2;
            baseStartY = h / 2;

            List<PoiOut> res = new List<PoiOut>();
            if (wgData == null) return res;

            object spawnData = wgData.GetType().GetField("gameSpawnData", ALL).GetValue(wgData);
            if (spawnData == null) return res;

            Type tSd = spawnData.GetType();
            object startPosObj = tSd.GetField("baseStartPos", ALL).GetValue(spawnData);
            if (startPosObj != null)
            {
                baseStartX = (int)s_fV2ix.GetValue(startPosObj);
                baseStartY = (int)s_fV2iy.GetValue(startPosObj);
            }

            CachedGeyserPool pool = GetOrBuildGeyserPool(activeDlcCsv);
            List<GeyserPoolEntry> geyserPool = pool.GenericPool;
            Dictionary<string, GeyserPoolEntry> allByPrefab = pool.AllByPrefab;
            Dictionary<string, GeyserPoolEntry> allByRaw = pool.AllByRaw;

            Dictionary<string, string> geyserNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < geyserPool.Count; i++)
                if (!string.IsNullOrEmpty(geyserPool[i].LocalizedName))
                    geyserNameMap[geyserPool[i].PrefabId] = geyserPool[i].LocalizedName;
            foreach (KeyValuePair<string, GeyserPoolEntry> kv in allByPrefab)
                if (!string.IsNullOrEmpty(kv.Value.LocalizedName))
                    geyserNameMap[kv.Key] = kv.Value.LocalizedName;

            Type tPrefab = s_asmCs.GetType("TemplateClasses.Prefab", true);
            PropertyInfo pId = tPrefab.GetProperty("id", ALL);
            PropertyInfo pLx = tPrefab.GetProperty("location_x", ALL);
            PropertyInfo pLy = tPrefab.GetProperty("location_y", ALL);

            HashSet<string> dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasHq = false;

            string[] lists = { "buildings", "otherEntities", "pickupables" };
            for (int li = 0; li < lists.Length; li++)
            {
                IList items = tSd.GetField(lists[li], ALL).GetValue(spawnData) as IList;
                if (items == null) continue;
                for (int i = 0; i < items.Count; i++)
                {
                    object pf = items[i];
                    string id = pId.GetValue(pf, null) as string;
                    if (string.IsNullOrEmpty(id)) continue;
                    int lx = (int)pLx.GetValue(pf, null);
                    int ly = (int)pLy.GetValue(pf, null);

                    byte isRandom = 0;
                    object pickedKrand = null;
                    GeyserPoolEntry pickedPoolEntry = null;
                    if (string.Equals(id, "GeyserGeneric", StringComparison.Ordinal))
                    {
                        if (geyserPool.Count > 0)
                        {
                            int gSeed = unchecked(seed + (wpX + lx) + (wpY + ly));
                            pickedKrand = s_cKRandomInt.Invoke(new object[] { gSeed });
                            int pick = (int)s_mKRandomNext2.Invoke(pickedKrand, new object[] { 0, geyserPool.Count });
                            if (pick >= 0 && pick < geyserPool.Count)
                            {
                                pickedPoolEntry = geyserPool[pick];
                                id = pickedPoolEntry.PrefabId;
                            }
                        }
                        isRandom = 1;
                    }

                    string cat = ClassifyPoi(id);
                    if (cat == null) continue;

                    if (string.Equals(id, "Headquarters", StringComparison.Ordinal))
                    {
                        hasHq = true;
                        baseStartX = lx;
                        baseStartY = ly;
                    }

                    string key = id + "@" + lx + "," + ly;
                    if (!dedup.Add(key)) continue;

                    string disp = ResolvePoiDisplayName(id, geyserNameMap);
                    PoiOut poi = new PoiOut
                    {
                        PrefabId = id,
                        Category = cat,
                        DisplayName = disp,
                        X = lx,
                        Y = ly,
                        IsRandomGeyser = isRandom
                    };

                    if (cat == "water_geyser" || cat == "gas_geyser" || cat == "metal_volcano" || cat == "volcano")
                    {
                        if (isRandom == 1 && pickedPoolEntry != null && pickedPoolEntry.GeyserTypeObj != null)
                        {



                            int gSeed2 = unchecked(seed + (wpX + lx) + (wpY + ly));
                            object krandStats = s_cKRandomInt.Invoke(new object[] { gSeed2 });
                            object gtHash = s_fGtIdHash.GetValue(pickedPoolEntry.GeyserTypeObj);
                            PopulateGeyserStats(poi, krandStats, gtHash, pickedPoolEntry.GeyserTypeObj, 0f, 1f);
                        }
                        else
                        {
                            object gtHash, gtObj;
                            float pMin, pMax;
                            if (TryResolveFixedGeyserPreset(id, allByPrefab, allByRaw, out gtHash, out gtObj, out pMin, out pMax))
                            {
                                int gSeed2 = unchecked(seed + (wpX + lx) + (wpY + ly));
                                object krandStats = s_cKRandomInt.Invoke(new object[] { gSeed2 });
                                PopulateGeyserStats(poi, krandStats, gtHash, gtObj, pMin, pMax);
                            }
                        }
                    }

                    res.Add(poi);
                }
            }

            if (!hasHq && baseStartX > 0 && baseStartY > 0 && baseStartX < w && baseStartY < h)
            {
                string disp = ResolvePoiDisplayName("Headquarters", geyserNameMap);
                res.Insert(0, new PoiOut
                {
                    PrefabId = "Headquarters",
                    Category = "start",
                    DisplayName = disp,
                    X = baseStartX,
                    Y = baseStartY,
                    IsRandomGeyser = 0
                });
            }

            return res;
        }

        static string ClassifyPoi(string id)
        {
            if (string.Equals(id, "Headquarters", StringComparison.Ordinal))
                return "start";

            if (string.Equals(id, "OilWell", StringComparison.Ordinal))
                return "oil";

            if (id.StartsWith("GeyserGeneric_", StringComparison.Ordinal))
            {
                if (id.IndexOf("molten_", StringComparison.Ordinal) >= 0) return "metal_volcano";
                if (id.IndexOf("volcano", StringComparison.Ordinal) >= 0) return "volcano";
                if (id.IndexOf("methane", StringComparison.Ordinal) >= 0 ||
                    id.IndexOf("hydrogen", StringComparison.Ordinal) >= 0 ||
                    id.IndexOf("chlorine", StringComparison.Ordinal) >= 0 ||
                    id.IndexOf("hot_co2", StringComparison.Ordinal) >= 0 ||
                    id.IndexOf("po2", StringComparison.Ordinal) >= 0 ||
                    id.IndexOf("liquid_co2", StringComparison.Ordinal) >= 0)
                    return "gas_geyser";
                return "water_geyser";
            }

            if (string.Equals(id, "NiobiumGeyser", StringComparison.Ordinal)) return "metal_volcano";
            if (string.Equals(id, "MethaneGeyser", StringComparison.Ordinal) ||
                string.Equals(id, "ChlorineGeyser", StringComparison.Ordinal))
                return "gas_geyser";
            if (string.Equals(id, "SmallReefGeyser", StringComparison.Ordinal) ||
                string.Equals(id, "Geyser", StringComparison.Ordinal))
                return "water_geyser";

            if (string.Equals(id, "MegaBrainTank", StringComparison.Ordinal) ||
                string.Equals(id, "LonelyMinionHouse", StringComparison.Ordinal) ||
                string.Equals(id, "LonelyMailBox", StringComparison.Ordinal) ||
                string.Equals(id, "FossilDig", StringComparison.Ordinal) ||
                string.Equals(id, "MorbRoverMaker", StringComparison.Ordinal) ||
                string.Equals(id, "HijackedHeadquarters", StringComparison.Ordinal) ||
                string.Equals(id, "GravitasCreatureManipulator", StringComparison.Ordinal))
                return "story";

            if (string.Equals(id, "MassiveHeatSink", StringComparison.Ordinal) ||
                string.Equals(id, "GeneShuffler", StringComparison.Ordinal) ||
                string.Equals(id, "WarpPortal", StringComparison.Ordinal) ||
                string.Equals(id, "WarpReceiver", StringComparison.Ordinal) ||
                string.Equals(id, "WarpConduitSender", StringComparison.Ordinal) ||
                string.Equals(id, "WarpConduitReceiver", StringComparison.Ordinal) ||
                string.Equals(id, "CryoTank", StringComparison.Ordinal) ||
                string.Equals(id, "TemporalTearOpener", StringComparison.Ordinal) ||
                string.Equals(id, "TemporalTearAnalyzer", StringComparison.Ordinal) ||
                string.Equals(id, "SapTree", StringComparison.Ordinal) ||
                string.Equals(id, "GeothermalControllerEntity", StringComparison.Ordinal) ||
                string.Equals(id, "GeothermalVentEntity", StringComparison.Ordinal) ||
                string.Equals(id, "HeatCompressor", StringComparison.Ordinal) ||
                string.Equals(id, "POICeresTechUnlock", StringComparison.Ordinal) ||
                string.Equals(id, "POIDlc4TechUnlock", StringComparison.Ordinal) ||
                string.Equals(id, "POIDlc5TechUnlock", StringComparison.Ordinal) ||
                string.Equals(id, "PropSurfaceSatellite1", StringComparison.Ordinal) ||
                string.Equals(id, "PropSurfaceSatellite2", StringComparison.Ordinal) ||
                string.Equals(id, "PropSurfaceSatellite3", StringComparison.Ordinal) ||
                string.Equals(id, "MissileLauncher", StringComparison.Ordinal) ||
                string.Equals(id, "GravitasPedestal", StringComparison.Ordinal))
                return "poi";

            return null;
        }

        static string ResolvePoiDisplayName(string id, Dictionary<string, string> geyserNameMap)
        {
            string loc;
            if (geyserNameMap.TryGetValue(id, out loc) && !string.IsNullOrEmpty(loc))
                return loc;

            if (id.StartsWith("GeyserGeneric_", StringComparison.Ordinal))
            {
                string sub = id.Substring("GeyserGeneric_".Length).ToUpperInvariant();
                loc = TryGetString("STRINGS.CREATURES.SPECIES.GEYSER." + sub + ".NAME");
                if (!string.IsNullOrEmpty(loc)) return CleanRichText(loc);
            }

            string upper = id.ToUpperInvariant();
            string[] candidates =
            {
                "STRINGS.BUILDINGS.PREFABS." + upper + ".NAME",
                "STRINGS.CREATURES.SPECIES." + upper + ".NAME",
                "STRINGS.CREATURES.SPECIES.GEYSER." + upper + ".NAME"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                loc = TryGetString(candidates[i]);
                if (!string.IsNullOrEmpty(loc)) return CleanRichText(loc);
            }
            return id;
        }

        static bool IsSettingsCachePopulated()
        {
            try
            {
                object worldsObj = s_fScWorlds.GetValue(null);
                object clustersObj = s_fScClusters.GetValue(null);
                if (worldsObj == null || clustersObj == null) return false;
                IDictionary wDict = worldsObj.GetType().GetField("worldCache", ALL).GetValue(worldsObj) as IDictionary;
                IDictionary cDict = clustersObj.GetType().GetField("clusterCache", ALL).GetValue(clustersObj) as IDictionary;
                return wDict != null && wDict.Count > 0 && cDict != null && cDict.Count > 0;
            }
            catch { return false; }
        }

        static string TryGetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (s_headlessPo != null)
            {
                string val;
                if (s_headlessPo.TryGetValue(key, out val) && !string.IsNullOrEmpty(val))
                    return val;
            }
            if (s_mStringsTryGet == null || s_fStringEntryVal == null) return null;
            try
            {
                object[] args = new object[] { key, null };
                bool ok = (bool)s_mStringsTryGet.Invoke(null, args);
                if (ok && args[1] != null) return s_fStringEntryVal.GetValue(args[1]) as string;
            }
            catch { }
            return null;
        }

        static string CleanRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('<') >= 0) s = Regex.Replace(s, "<[^>]+>", "");
            return s.Trim();
        }

        static void WriteBinaryPreview(
            string outPath,
            string clusterPath,
            int seed,
            int worldIndex,
            string worldName,
            int width,
            int height,
            int wpX,
            int wpY,
            int baseStartX,
            int baseStartY,
            int elapsedMs,
            string traitsSummary,
            IList elementsList,
            ushort[] outElem,
            short[] outTemp,
            byte[] zoneGrid,
            List<PoiOut> pois)
        {
            Type tElem = elementsList[0].GetType();
            FieldInfo fIdx = tElem.GetField("idx", ALL);
            FieldInfo fState = tElem.GetField("state", ALL);
            FieldInfo fName = tElem.GetField("<name>k__BackingField", ALL);
            FieldInfo fSubst = tElem.GetField("substance", ALL);

            int maxIdx = elementsList.Count - 1;
            for (int i = 0; i < elementsList.Count; i++)
            {
                int idx = Convert.ToInt32(fIdx.GetValue(elementsList[i]));
                if (idx > maxIdx && idx < 4096) maxIdx = idx;
            }

            byte[] elemR = new byte[maxIdx + 1];
            byte[] elemG = new byte[maxIdx + 1];
            byte[] elemB = new byte[maxIdx + 1];
            byte[] elemS = new byte[maxIdx + 1];
            string[] elemN = new string[maxIdx + 1];

            FieldInfo fUiCol = null, fCr = null, fCg = null, fCb = null;
            for (int i = 0; i < elementsList.Count; i++)
            {
                object el = elementsList[i];
                int idx = Convert.ToInt32(fIdx.GetValue(el));
                if (idx < 0 || idx > maxIdx) continue;
                int st = Convert.ToInt32(fState.GetValue(el)) & 3;
                string nm = CleanRichText(fName.GetValue(el) as string);
                elemS[idx] = (byte)st;
                elemN[idx] = nm;

                object sub = fSubst.GetValue(el);
                if (sub != null)
                {
                    if (fUiCol == null)
                    {
                        fUiCol = sub.GetType().GetField("uiColour", ALL);
                        if (fUiCol == null) fUiCol = sub.GetType().GetField("colour", ALL);
                    }
                    if (fUiCol != null)
                    {
                        object c32 = fUiCol.GetValue(sub);
                        if (c32 != null)
                        {
                            if (fCr == null)
                            {
                                Type tc32 = c32.GetType();
                                fCr = tc32.GetField("r", ALL);
                                fCg = tc32.GetField("g", ALL);
                                fCb = tc32.GetField("b", ALL);
                            }
                            elemR[idx] = (byte)fCr.GetValue(c32);
                            elemG[idx] = (byte)fCg.GetValue(c32);
                            elemB[idx] = (byte)fCb.GetValue(c32);
                        }
                    }
                }
            }

            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmpPath = outPath + ".tmp";

            using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write((byte)'O'); bw.Write((byte)'N'); bw.Write((byte)'I'); bw.Write((byte)'P');
                bw.Write(2);
                bw.Write(clusterPath ?? "");
                bw.Write(seed);
                bw.Write(worldIndex);
                bw.Write(worldName ?? "");
                bw.Write(width);
                bw.Write(height);
                bw.Write(wpX);
                bw.Write(wpY);
                bw.Write(baseStartX);
                bw.Write(baseStartY);
                bw.Write(elapsedMs);
                bw.Write(traitsSummary ?? "");

                int paletteCount = maxIdx + 1;
                bw.Write(paletteCount);
                for (int i = 0; i < paletteCount; i++)
                {
                    bw.Write(elemR[i]);
                    bw.Write(elemG[i]);
                    bw.Write(elemB[i]);
                    bw.Write(elemS[i]);
                    bw.Write(elemN[i] ?? "");
                }

                int total = width * height;
                for (int i = 0; i < total; i++) bw.Write(outElem[i]);
                for (int i = 0; i < total; i++) bw.Write(outTemp[i]);
                bw.Write(zoneGrid, 0, total);

                WritePoiList(bw, pois);
            }

            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmpPath, outPath);
        }

        static void WritePoiOnlyPreview(
            string outPath,
            string clusterPath,
            int seed,
            int worldIndex,
            string worldName,
            int width,
            int height,
            int wpX,
            int wpY,
            int baseStartX,
            int baseStartY,
            int elapsedMs,
            string traitsSummary,
            List<PoiOut> pois)
        {
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmpPath = outPath + ".tmp";

            using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write((byte)'O'); bw.Write((byte)'N'); bw.Write((byte)'I'); bw.Write((byte)'P');
                bw.Write(3);
                bw.Write(clusterPath ?? "");
                bw.Write(seed);
                bw.Write(worldIndex);
                bw.Write(worldName ?? "");
                bw.Write(width);
                bw.Write(height);
                bw.Write(wpX);
                bw.Write(wpY);
                bw.Write(baseStartX);
                bw.Write(baseStartY);
                bw.Write(elapsedMs);
                bw.Write(traitsSummary ?? "");
                WritePoiList(bw, pois);
            }

            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmpPath, outPath);
        }

        static void WritePoiList(BinaryWriter bw, List<PoiOut> pois)
        {
            bw.Write(pois.Count);
            for (int i = 0; i < pois.Count; i++)
            {
                PoiOut p = pois[i];
                bw.Write(p.PrefabId ?? "");
                bw.Write(p.Category ?? "");
                bw.Write(p.DisplayName ?? "");
                bw.Write(p.X);
                bw.Write(p.Y);
                bw.Write(p.IsRandomGeyser);
                bw.Write(p.HasGeyserStats);
                bw.Write(p.AvgYieldGps);
                bw.Write(p.EruptRateGps);
                bw.Write(p.OutputTempC);
                bw.Write(p.EruptSec);
                bw.Write(p.IdleSec);
                bw.Write(p.ActiveCycles);
                bw.Write(p.DormantCycles);
                bw.Write(p.Grade);
                bw.Write(p.Percentile);
            }
        }



        static bool InitInSaveReflection()
        {
            if (s_inSaveResolved) return true;
            string dummy;
            if (!InitAssemblies(out dummy)) return false;
            try
            {
                s_tComponents = s_asmCs.GetType("Components", true);
                s_fCompLiveMinions = s_tComponents.GetField("LiveMinionIdentities", ALL);
                s_fCompGeysers = s_tComponents.GetField("Geysers", ALL);

                s_tStudyable = s_asmCs.GetType("Studyable", false);
                if (s_tStudyable != null)
                {
                    s_fStudyStudied = s_tStudyable.GetField("studied", ALL);
                    s_fStudyMarked = s_tStudyable.GetField("markedForStudy", ALL);
                    s_mStudyCancel = s_tStudyable.GetMethod("CancelChore", ALL, null, Type.EmptyTypes, null);
                    s_mStudyRefresh = s_tStudyable.GetMethod("Refresh", ALL, null, Type.EmptyTypes, null);
                    s_mStudyTrigger = s_tStudyable.GetMethod("Trigger", ALL, null, new Type[] { typeof(int), typeof(object) }, null);
                }

                Type tGeyser = s_asmCs.GetType("Geyser", false);
                if (tGeyser != null) s_fGeyserStudyable = tGeyser.GetField("studyable", ALL);


                string rDummy;
                InitReflection(out rDummy);

                s_tGeyser = tGeyser;
                if (s_tGeyser != null)
                {
                    s_fGeyserConfig = s_tGeyser.GetField("configuration", ALL);
                    s_mGeyserUpdateMod = s_tGeyser.GetMethod("UpdateModifier", ALL, null, Type.EmptyTypes, null);
                    s_mGeyserRefreshMeter = s_tGeyser.GetMethod("RefreshStudiedMeter", ALL, null, Type.EmptyTypes, null);
                    s_mGeyserGenName = s_tGeyser.GetMethod("GenerateName", ALL, null, Type.EmptyTypes, null);
                    s_geyserMemoizerFields = new List<FieldInfo>();
                    FieldInfo[] gfs = s_tGeyser.GetFields(ALL | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < gfs.Length; i++)
                    {
                        if (gfs[i].Name.EndsWith("Memoizer", StringComparison.Ordinal))
                            s_geyserMemoizerFields.Add(gfs[i]);
                    }
                }
                s_tKPrefabId = s_asmCs.GetType("KPrefabID", false) ?? s_asmFp.GetType("KPrefabID", false);
                if (s_tKPrefabId != null) s_fPrefabTag = s_tKPrefabId.GetField("PrefabTag", ALL);
                if (s_tTag != null) s_pTagName = s_tTag.GetProperty("Name", ALL);
                s_tKSelectable = s_asmCs.GetType("KSelectable", false);
                if (s_tKSelectable != null) s_mKSelectGetName = s_tKSelectable.GetMethod("GetName", ALL, null, Type.EmptyTypes, null);
                s_tClusterMgr = s_asmCs.GetType("ClusterManager", false);
                if (s_tClusterMgr != null)
                {
                    s_fClusterMgrInst = s_tClusterMgr.GetField("Instance", ALL);
                    s_mClusterGetWorld = s_tClusterMgr.GetMethod("GetWorld", ALL, null, new Type[] { typeof(int) }, null);
                    s_mClusterSetActiveWorld = s_tClusterMgr.GetMethod("SetActiveWorld", ALL, null, new Type[] { typeof(int) }, null);
                }
                s_tClusterGridEntity = s_asmCs.GetType("ClusterGridEntity", false);
                if (s_tClusterGridEntity != null)
                {
                    s_pCgeName = s_tClusterGridEntity.GetProperty("Name", ALL);
                }
                Type tWc = s_asmCs.GetType("WorldContainer", false);
                if (tWc != null)
                {
                    s_fWorldName = tWc.GetField("worldName", ALL);
                    s_fWorldOverrideName = tWc.GetField("overrideName", ALL);
                    s_mWorldGetOffset = tWc.GetMethod("get_WorldOffset", ALL, null, Type.EmptyTypes, null);
                }
                s_tCamCtrl = s_asmCs.GetType("CameraController", false);
                if (s_tCamCtrl != null)
                {
                    s_fCamCtrlInst = s_tCamCtrl.GetField("<Instance>k__BackingField", ALL);
                }
                s_tVec3 = s_asmCore.GetType("UnityEngine.Vector3", true);
                Type tUtil = s_asmFp.GetType("Util", false);
                Type tGo = s_asmCore.GetType("UnityEngine.GameObject", false);
                Type tTr = s_asmCore.GetType("UnityEngine.Transform", false);
                Type tCompBase = s_asmCore.GetType("UnityEngine.Component", false);
                Type tMono = s_asmFp.GetType("KMonoBehaviour", false);
                if (tMono != null)
                {
                    s_mKMonoTrigger = tMono.GetMethod("Trigger", ALL, null, new Type[] { typeof(int), typeof(object) }, null);
                }
                Type tClusterUtil = s_asmCs.GetType("ClusterUtil", false);
                if (tClusterUtil != null && tMono != null)
                {
                    s_mGetMyWorldId = tClusterUtil.GetMethod("GetMyWorldId", ALL, null, new Type[] { tMono }, null);
                }
                if (tGo != null)
                {
                    s_mGoGetComp = tGo.GetMethod("GetComponent", ALL, null, new Type[] { typeof(Type) }, null);
                    s_mGoSetActive = tGo.GetMethod("SetActive", ALL, null, new Type[] { typeof(bool) }, null);
                    s_pGoTransform = tGo.GetProperty("transform", ALL);
                }
                if (tCompBase != null) s_pCompTransform = tCompBase.GetProperty("transform", ALL);
                if (tTr != null) s_mTrGetPos = tTr.GetMethod("get_position", ALL, null, Type.EmptyTypes, null);
                s_cVec3F3 = s_tVec3 != null ? s_tVec3.GetConstructor(ALL, null, new Type[] { typeof(float), typeof(float), typeof(float) }, null) : null;
                if (s_tCamCtrl != null && s_tVec3 != null)
                {
                    s_mCamSetTargetPos = s_tCamCtrl.GetMethod("SetTargetPos", ALL, null,
                        new Type[] { s_tVec3, typeof(float), typeof(bool) }, null);
                }
                if (tUtil != null && tGo != null && s_tVec3 != null)
                {
                    s_mUtilKInst2 = tUtil.GetMethod("KInstantiate", ALL, null, new Type[] { tGo, s_tVec3 }, null);
                    s_mUtilKDest1 = tUtil.GetMethod("KDestroyGameObject", ALL, null, new Type[] { tGo }, null);
                }

                s_tModifiers = s_asmCs.GetType("Klei.AI.Modifiers", false);
                if (s_tModifiers != null)
                {
                    s_fModAmounts = s_tModifiers.GetField("amounts", ALL);
                    s_fModAttrs = s_tModifiers.GetField("attributes", ALL);
                }

                s_tAmounts = s_asmCs.GetType("Klei.AI.Amounts", false);
                Type tAmtInst = s_asmCs.GetType("Klei.AI.AmountInstance", false);
                if (s_tAmounts != null && tAmtInst != null)
                {
                    s_mAmountsGet = s_tAmounts.GetMethod("Get", ALL, null, new Type[] { typeof(string) }, null);
                    s_mAmtGetMax = tAmtInst.GetMethod("GetMax", ALL);
                    s_mAmtSetVal = tAmtInst.GetMethod("SetValue", ALL, null, new Type[] { typeof(float) }, null);
                }

                Type tAttrs = s_asmCs.GetType("Klei.AI.Attributes", false);
                Type tAttrInst = s_asmCs.GetType("Klei.AI.AttributeInstance", false);
                s_tAttrMod = s_asmCs.GetType("Klei.AI.AttributeModifier", false);
                if (tAttrs != null && tAttrInst != null && s_tAttrMod != null)
                {
                    s_mAttrsGet = tAttrs.GetMethod("Get", ALL, null, new Type[] { typeof(string) }, null);
                    s_fAttrInstMods = tAttrInst.GetField("Modifiers", ALL);
                    s_mAttrInstAdd = tAttrInst.GetMethod("Add", ALL, null, new Type[] { s_tAttrMod }, null);
                    s_mAttrInstRemove = tAttrInst.GetMethod("Remove", ALL, null, new Type[] { s_tAttrMod }, null);
                    s_fAttrModDesc = s_tAttrMod.GetField("Description", ALL);
                    s_cAttrMod6 = s_tAttrMod.GetConstructor(ALL, null, new Type[] { typeof(string), typeof(float), typeof(string), typeof(bool), typeof(bool), typeof(bool) }, null);
                }

                s_tAttrLevels = s_asmCs.GetType("Klei.AI.AttributeLevels", false);
                Type tAttrLvl = s_asmCs.GetType("Klei.AI.AttributeLevel", false);
                if (s_tAttrLevels != null && tAttrLvl != null)
                {
                    s_mAttrLevelGet = s_tAttrLevels.GetMethod("GetAttributeLevel", ALL, null, new Type[] { typeof(string) }, null);
                    s_mAttrLevelSet = s_tAttrLevels.GetMethod("SetLevel", ALL, null, new Type[] { typeof(string), typeof(int) }, null);
                    s_fAttrLevelVal = tAttrLvl.GetField("level", ALL);
                }

                s_tTraits = s_asmCs.GetType("Klei.AI.Traits", false);
                Type tTrait = s_asmCs.GetType("Klei.AI.Trait", false);
                if (s_tTraits != null && tTrait != null)
                {
                    s_fTraitList = s_tTraits.GetField("TraitList", ALL);
                    s_mTraitsRemove = s_tTraits.GetMethod("Remove", ALL, null, new Type[] { tTrait }, null);
                    s_mTraitsAdd = s_tTraits.GetMethod("Add", ALL, null, new Type[] { tTrait }, null);
                    s_mTraitsHasStr = s_tTraits.GetMethod("HasTrait", ALL, null, new Type[] { typeof(string) }, null);
                    s_mTraitGetName = tTrait.GetMethod("GetName", ALL);
                    s_mTraitGetTooltip = tTrait.GetMethod("GetTooltip", ALL, null, Type.EmptyTypes, null);
                    s_fTraitPositive = tTrait.GetField("PositiveTrait", ALL);
                    for (Type bt = tTrait; bt != null && s_fTraitDesc == null; bt = bt.BaseType)
                        s_fTraitDesc = bt.GetField("description", ALL | BindingFlags.DeclaredOnly);
                }

                s_tDb = s_asmCs.GetType("Db", false);
                if (s_tDb != null)
                {
                    s_mDbGet = s_tDb.GetMethod("Get", ALL, null, Type.EmptyTypes, null);
                    s_fDbTraits = s_tDb.GetField("traits", ALL);
                }
                s_tDupStats = s_asmCs.GetType("TUNING.DUPLICANTSTATS", false);

                Type tComp = s_asmCore.GetType("UnityEngine.Component", true);
                s_mGetComponent = tComp.GetMethod("GetComponent", ALL, null, new Type[] { typeof(Type) }, null);
                s_tUnityObj = s_asmCore.GetType("UnityEngine.Object", true);
                s_mUnityFindObjects = s_tUnityObj.GetMethod("FindObjectsOfType", ALL, null, new Type[] { typeof(Type) }, null);

                s_tVec3 = s_asmCore.GetType("UnityEngine.Vector3", true);

                s_tGrid = s_asmCs.GetType("Grid", true);
                s_fGridW = s_tGrid.GetField("WidthInCells", ALL);
                s_fGridC = s_tGrid.GetField("CellCount", ALL);
                s_fGridElemIdx = s_tGrid.GetField("ElementIdx", ALL);
                s_fGridMass = s_tGrid.GetField("Mass", ALL);
                s_fGridTemp = s_tGrid.GetField("Temperature", ALL);
                s_fGridDisIdx = s_tGrid.GetField("DiseaseIdx", ALL);
                s_fGridDisCnt = s_tGrid.GetField("DiseaseCount", ALL);
                Type[] oneInt = new Type[] { typeof(int) };
                if (s_fGridElemIdx != null) s_mGridGetElemIdx = s_fGridElemIdx.FieldType.GetMethod("get_Item", ALL, null, oneInt, null);
                if (s_fGridMass != null) s_mGridGetMass = s_fGridMass.FieldType.GetMethod("get_Item", ALL, null, oneInt, null);
                if (s_fGridTemp != null) s_mGridGetTemp = s_fGridTemp.FieldType.GetMethod("get_Item", ALL, null, oneInt, null);
                if (s_fGridDisIdx != null) s_mGridGetDisIdx = s_fGridDisIdx.FieldType.GetMethod("get_Item", ALL, null, oneInt, null);
                if (s_fGridDisCnt != null) s_mGridGetDisCnt = s_fGridDisCnt.FieldType.GetMethod("get_Item", ALL, null, oneInt, null);
                s_mGridIsValidCell = s_tGrid.GetMethod("IsValidCell", ALL, null, oneInt, null);
                s_mGridPosToCell = s_tGrid.GetMethod("PosToCell", ALL, null, new Type[] { s_tVec3 }, null);

                Type tApp = s_asmCore.GetType("UnityEngine.Application", false);
                if (tApp != null) s_pAppIsFocused = tApp.GetProperty("isFocused", ALL);
                s_tSelectTool = s_asmCs.GetType("SelectTool", false);
                if (s_tSelectTool != null)
                {
                    s_fSelectToolInst = s_tSelectTool.GetField("Instance", ALL);
                    s_fSelectSelected = s_tSelectTool.GetField("selected", ALL);
                    s_fSelectCell = s_tSelectTool.GetField("selectedCell", ALL);
                }
                s_tCellSelObj = s_asmCs.GetType("CellSelectionObject", false);
                if (s_tCellSelObj != null)
                {
                    s_fCsoSelectedCell = s_tCellSelObj.GetField("selectedCell", ALL);
                    s_fCsoMouseCell = s_tCellSelObj.GetField("mouseCell", ALL);
                }

                s_tElementLoader = s_asmCs.GetType("ElementLoader", true);
                s_fElements = s_tElementLoader.GetField("elements", ALL);

                s_tPlayerCtrl = s_asmCs.GetType("PlayerController", false);
                if (s_tPlayerCtrl != null)
                {
                    s_fPcInst = s_tPlayerCtrl.GetField("<Instance>k__BackingField", ALL);
                    s_mPcGetCursor = s_tPlayerCtrl.GetMethod("GetCursorPos", ALL, null, Type.EmptyTypes, null);
                }

                s_tPopFX = s_asmCs.GetType("PopFXManager", false);
                if (s_tPopFX != null)
                {
                    s_fPopInst = s_tPopFX.GetField("Instance", ALL);
                    s_fPopReady = s_tPopFX.GetField("ready", ALL);
                    s_fPopSpritePlus = s_tPopFX.GetField("sprite_Plus", ALL);
                    Type tSprite = s_asmCore.GetType("UnityEngine.Sprite", false);
                    Type tTransform = s_asmCore.GetType("UnityEngine.Transform", false);
                    if (tSprite != null && tTransform != null)
                    {
                        s_mPopSpawnFx = s_tPopFX.GetMethod("SpawnFX", ALL, null,
                            new Type[] { tSprite, typeof(string), tTransform, s_tVec3, typeof(float), typeof(bool), typeof(bool) }, null);
                    }
                }

                s_tSimMessages = s_asmCs.GetType("SimMessages", false);
                s_tSimHashes = s_asmCs.GetType("SimHashes", false);
                if (s_tSimMessages != null)
                {
                    MethodInfo[] ms = s_tSimMessages.GetMethods(ALL);
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (ms[i].Name == "ReplaceElement" && ms[i].GetParameters().Length == 8)
                        {
                            s_mSimReplaceElem = ms[i];
                            break;
                        }
                    }
                }
                if (s_tSimHashes != null)
                {
                    try { s_simVacuumEnum = Enum.Parse(s_tSimHashes, "Vacuum", true); } catch { }
                }

                s_tSaveLoader = s_asmCs.GetType("SaveLoader", false);
                if (s_tSaveLoader != null)
                {
                    s_pSaveLoaderInst = s_tSaveLoader.GetProperty("Instance", ALL);
                    s_mSaveGetPath = s_tSaveLoader.GetMethod("GetActiveSaveFilePath", ALL, null, Type.EmptyTypes, null);
                }

                s_inSaveResolved = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static IList GetLiveMinions()
        {
            if (s_fCompLiveMinions == null) return null;
            object compList = s_fCompLiveMinions.GetValue(null);
            if (compList == null) return null;
            PropertyInfo pItems = compList.GetType().GetProperty("Items", ALL);
            return pItems != null ? (pItems.GetValue(compList, null) as IList) : null;
        }

        static void SetAmountMaxOrZero(object amounts, string id, bool toMax)
        {
            if (amounts == null || s_mAmountsGet == null || s_mAmtSetVal == null) return;
            object inst = s_mAmountsGet.Invoke(amounts, new object[] { id });
            if (inst == null) return;
            float val = 0f;
            if (toMax && s_mAmtGetMax != null) val = (float)s_mAmtGetMax.Invoke(inst, null);
            s_mAmtSetVal.Invoke(inst, new object[] { val });
        }

        public static void TickMainThread(int mask)
        {
            if (mask == 0) return;
            try
            {
                if (!InitInSaveReflection()) return;
                IList minions = GetLiveMinions();
                if (minions == null) return;

                for (int i = 0; i < minions.Count; i++)
                {
                    object mi = minions[i];
                    if (mi == null) continue;
                    object modComp = s_mGetComponent.Invoke(mi, new object[] { s_tModifiers });
                    if (modComp == null) continue;
                    object amounts = s_fModAmounts.GetValue(modComp);
                    if (amounts == null) continue;

                    if ((mask & 1) != 0)
                    {
                        SetAmountMaxOrZero(amounts, "Calories", true);
                        SetAmountMaxOrZero(amounts, "BionicInternalBattery", true);
                        SetAmountMaxOrZero(amounts, "BionicOil", true);
                    }
                    if ((mask & 2) != 0)
                    {
                        SetAmountMaxOrZero(amounts, "Stamina", true);
                    }
                    if ((mask & 4) != 0)
                    {
                        SetAmountMaxOrZero(amounts, "Bladder", false);
                        SetAmountMaxOrZero(amounts, "BionicGunk", false);
                    }
                    if ((mask & 8) != 0)
                    {
                        SetAmountMaxOrZero(amounts, "Breath", true);
                        SetAmountMaxOrZero(amounts, "BionicOxygenTank", true);
                    }
                    if ((mask & 16) != 0)
                    {
                        SetAmountMaxOrZero(amounts, "ImmuneLevel", true);
                        SetAmountMaxOrZero(amounts, "HitPoints", true);
                        SetAmountMaxOrZero(amounts, "RadiationBalance", false);
                    }
                }
            }
            catch { }
        }

        public static string ExecMainThread(string cmd)
        {
            try
            {
                if (!InitInSaveReflection()) return "ERR init-insave";
                string raw = (cmd ?? "").Trim();
                if (raw.Length == 0) return "ERR empty-cmd";

                int sp = raw.IndexOf(' ');
                string verb = sp > 0 ? raw.Substring(0, sp) : raw;
                string arg = sp > 0 ? raw.Substring(sp + 1).Trim() : "";

                if (string.Equals(verb, "STUDYALL", StringComparison.OrdinalIgnoreCase))
                    return CmdStudyAll();
                if (string.Equals(verb, "GETWORKERDATA", StringComparison.OrdinalIgnoreCase))
                    return ExportWorkerDataCatalog(arg);
                if (string.Equals(verb, "GEYSERLIST", StringComparison.OrdinalIgnoreCase))
                    return CmdGeyserList();
                if (string.Equals(verb, "GEYSERCATALOG", StringComparison.OrdinalIgnoreCase))
                    return CmdGeyserCatalog();
                if (string.Equals(verb, "GEYSERSET", StringComparison.OrdinalIgnoreCase))
                    return CmdGeyserSet(arg);
                if (string.Equals(verb, "GEYSERTYPE", StringComparison.OrdinalIgnoreCase))
                    return CmdGeyserType(arg);
                if (string.Equals(verb, "GEYSERFOCUS", StringComparison.OrdinalIgnoreCase))
                    return CmdGeyserFocus(arg);
                if (string.Equals(verb, "WORKSPEED", StringComparison.OrdinalIgnoreCase))
                    return CmdWorkSpeed(arg);
                if (string.Equals(verb, "SURVIVAL_NOW", StringComparison.OrdinalIgnoreCase))
                {
                    TickMainThread(31);
                    return "OK survival_now";
                }
                if (string.Equals(verb, "DUPELIST", StringComparison.OrdinalIgnoreCase))
                    return CmdDupeList();
                if (string.Equals(verb, "TRAITCATALOG", StringComparison.OrdinalIgnoreCase))
                    return CmdTraitCatalog();
                if (string.Equals(verb, "SETATTR", StringComparison.OrdinalIgnoreCase))
                    return CmdSetAttr(arg);
                if (string.Equals(verb, "ADDTRAIT", StringComparison.OrdinalIgnoreCase))
                    return CmdAddTrait(arg);
                if (string.Equals(verb, "REMOVETRAIT", StringComparison.OrdinalIgnoreCase))
                    return CmdRemoveTrait(arg);
                if (string.Equals(verb, "CELLTEMP", StringComparison.OrdinalIgnoreCase))
                    return CmdCellTemp(arg);
                if (string.Equals(verb, "CELLVAC", StringComparison.OrdinalIgnoreCase))
                    return CmdCellVac(arg);
                if (string.Equals(verb, "POPFX", StringComparison.OrdinalIgnoreCase))
                    return CmdPopFx(arg);
                if (string.Equals(verb, "SAVEPATH", StringComparison.OrdinalIgnoreCase))
                    return CmdSavePath();
                if (string.Equals(verb, "CLEANUP", StringComparison.OrdinalIgnoreCase))
                    return CmdWorkSpeed("0");

                return "ERR unknown-cmd:" + verb;
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                return "ERR exc:" + inner.Message;
            }
        }

        static string CmdStudyAll()
        {
            if (s_fStudyStudied == null) return "ERR no-studied-field";
            HashSet<object> list = new HashSet<object>();
            if (s_fCompGeysers != null && s_fGeyserStudyable != null)
            {
                try
                {
                    object gComp = s_fCompGeysers.GetValue(null);
                    if (gComp != null)
                    {
                        Type gt = gComp.GetType();
                        PropertyInfo piItems = gt.GetProperty("Items", ALL);
                        if (piItems != null)
                        {
                            IList geysers = piItems.GetValue(gComp, null) as IList;
                            if (geysers != null)
                            {
                                for (int i = 0; i < geysers.Count; i++)
                                {
                                    object g = geysers[i];
                                    if (g == null) continue;
                                    object st = s_fGeyserStudyable.GetValue(g);
                                    if (st != null) list.Add(st);
                                }
                            }
                        }
                        else
                        {
                            MethodInfo mWorldIds = gt.GetMethod("GetWorldsIds", ALL, null, Type.EmptyTypes, null)
                                ?? gt.GetMethod("GetWorldIds", ALL, null, Type.EmptyTypes, null);
                            MethodInfo mGetItems = gt.GetMethod("GetItems", ALL, null, new Type[] { typeof(int) }, null);
                            if (mWorldIds != null && mGetItems != null)
                            {
                                IEnumerable wids = mWorldIds.Invoke(gComp, null) as IEnumerable;
                                if (wids != null)
                                {
                                    foreach (object widObj in wids)
                                    {
                                        IList geysers = mGetItems.Invoke(gComp, new object[] { widObj }) as IList;
                                        if (geysers == null) continue;
                                        for (int i = 0; i < geysers.Count; i++)
                                        {
                                            object g = geysers[i];
                                            if (g == null) continue;
                                            object st = s_fGeyserStudyable.GetValue(g);
                                            if (st != null) list.Add(st);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            if (s_mUnityFindObjects != null && s_tStudyable != null)
            {
                try
                {
                    Array found = s_mUnityFindObjects.Invoke(null, new object[] { s_tStudyable }) as Array;
                    if (found != null)
                    {
                        for (int i = 0; i < found.Length; i++)
                        {
                            object st = found.GetValue(i);
                            if (st != null) list.Add(st);
                        }
                    }
                }
                catch { }
            }

            int total = 0, studied = 0;
            foreach (object st in list)
            {
                if (st == null) continue;
                total++;
                try
                {
                    object sv = s_fStudyStudied.GetValue(st);
                    if (sv is bool && (bool)sv) continue;
                    s_fStudyStudied.SetValue(st, true);
                    if (s_fStudyMarked != null) s_fStudyMarked.SetValue(st, false);
                    if (s_mStudyCancel != null) { try { s_mStudyCancel.Invoke(st, null); } catch { } }
                    if (s_mStudyRefresh != null) { try { s_mStudyRefresh.Invoke(st, null); } catch { } }
                    if (s_mStudyTrigger != null) { try { s_mStudyTrigger.Invoke(st, new object[] { -1436775550, null }); } catch { } }
                    studied++;
                }
                catch { }
            }
            return "OK studied:" + studied + "/" + total;
        }

        static string CmdWorkSpeed(string arg)
        {
            float bonus = 0f;
            float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out bonus);
            if (bonus < 0f) bonus = 0f;

            IList minions = GetLiveMinions();
            if (minions == null) return "ERR no-minions";
            int n = 0;
            for (int i = 0; i < minions.Count; i++)
            {
                object mi = minions[i];
                if (mi == null) continue;
                object modComp = s_mGetComponent.Invoke(mi, new object[] { s_tModifiers });
                if (modComp == null) continue;
                object attrs = s_fModAttrs.GetValue(modComp);
                if (attrs == null) continue;
                for (int a = 0; a < s_workAttrIds.Length; a++)
                {
                    string aid = s_workAttrIds[a];
                    object ai = s_mAttrsGet.Invoke(attrs, new object[] { aid });
                    if (ai == null) continue;
                    IList mods = s_fAttrInstMods.GetValue(ai) as IList;
                    if (mods != null)
                    {
                        for (int m = mods.Count - 1; m >= 0; m--)
                        {
                            object mod = mods[m];
                            string desc = s_fAttrModDesc.GetValue(mod) as string;
                            if (string.Equals(desc, "TrainerWork", StringComparison.Ordinal))
                                s_mAttrInstRemove.Invoke(ai, new object[] { mod });
                        }
                    }
                    if (bonus > 0f)
                    {
                        object newMod = s_cAttrMod6.Invoke(new object[] { aid, bonus, "TrainerWork", false, false, true });
                        s_mAttrInstAdd.Invoke(ai, new object[] { newMod });
                    }
                }
                n++;
            }
            return "OK workspeed:" + bonus.ToString("0", CultureInfo.InvariantCulture) + " n:" + n;
        }

        static string GetDupeNameAndModel(object mi, out string model)
        {
            model = "";
            string name = "";
            Type tMi = mi.GetType();
            try
            {
                MethodInfo mGp = tMi.GetMethod("GetProperName", ALL, null, Type.EmptyTypes, null);
                if (mGp != null) name = mGp.Invoke(mi, null) as string;
            }
            catch { }
            if (string.IsNullOrEmpty(name))
            {
                FieldInfo nf = tMi.GetField("name", ALL);
                if (nf != null) name = nf.GetValue(mi) as string;
            }
            try
            {
                FieldInfo mdf = tMi.GetField("model", ALL);
                object tag = mdf != null ? mdf.GetValue(mi) : null;
                if (tag != null)
                {
                    PropertyInfo pn = tag.GetType().GetProperty("Name", ALL);
                    model = pn != null ? (pn.GetValue(tag, null) as string) : tag.ToString();
                }
            }
            catch { }
            return (name ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        static int GetUnityInstanceId(object obj)
        {
            MethodInfo m = obj.GetType().GetMethod("GetInstanceID", ALL, null, Type.EmptyTypes, null);
            return m != null ? (int)m.Invoke(obj, null) : 0;
        }

        static object FindMinionByInstId(int instId)
        {
            IList minions = GetLiveMinions();
            if (minions == null) return null;
            for (int i = 0; i < minions.Count; i++)
            {
                object mi = minions[i];
                if (mi != null && GetUnityInstanceId(mi) == instId) return mi;
            }
            return null;
        }

        static string CmdDupeList()
        {
            IList minions = GetLiveMinions();
            if (minions == null) return "ERR no-minions";
            StringBuilder sb = new StringBuilder();
            sb.Append("OK dupes:").Append(minions.Count);
            for (int i = 0; i < minions.Count; i++)
            {
                object mi = minions[i];
                if (mi == null) continue;
                int instId = GetUnityInstanceId(mi);
                string model;
                string name = GetDupeNameAndModel(mi, out model);

                StringBuilder attrSb = new StringBuilder();
                object attrLvls = s_tAttrLevels != null ? s_mGetComponent.Invoke(mi, new object[] { s_tAttrLevels }) : null;
                if (attrLvls != null && s_mAttrLevelGet != null && s_fAttrLevelVal != null)
                {
                    for (int a = 0; a < s_dupeAttrIds.Length; a++)
                    {
                        string aid = s_dupeAttrIds[a];
                        object al = s_mAttrLevelGet.Invoke(attrLvls, new object[] { aid });
                        int lvl = al != null ? (int)s_fAttrLevelVal.GetValue(al) : 0;
                        if (attrSb.Length > 0) attrSb.Append(',');
                        attrSb.Append(aid).Append(':').Append(lvl);
                    }
                }

                StringBuilder trSb = new StringBuilder();
                object traitsComp = s_tTraits != null ? s_mGetComponent.Invoke(mi, new object[] { s_tTraits }) : null;
                if (traitsComp != null && s_fTraitList != null)
                {
                    IList tlist = s_fTraitList.GetValue(traitsComp) as IList;
                    if (tlist != null)
                    {
                        for (int t = 0; t < tlist.Count; t++)
                        {
                            object tr = tlist[t];
                            if (tr == null) continue;
                            string tid = ReadTraitId(tr);
                            string tname = s_mTraitGetName != null ? (s_mTraitGetName.Invoke(tr, null) as string ?? tid) : tid;
                            bool pos = s_fTraitPositive != null && (bool)s_fTraitPositive.GetValue(tr);
                            tname = CleanRichText(tname).Replace('|', '/').Replace(';', '，').Replace(':', '：');
                            string tdesc = ReadTraitDesc(tr, tid);
                            if (trSb.Length > 0) trSb.Append(';');
                            trSb.Append(tid).Append(':').Append(tname).Append(':').Append(pos ? "1" : "0").Append(':').Append(tdesc);
                        }
                    }
                }

                sb.Append('\n')
                  .Append(instId).Append('|')
                  .Append(name).Append('|')
                  .Append(model ?? "").Append('|')
                  .Append(attrSb.ToString()).Append('|')
                  .Append(trSb.ToString());
            }
            return sb.ToString();
        }

        static string ReadTraitId(object tr)
        {
            if (tr == null) return "";
            for (Type t = tr.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("Id", ALL | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(tr) as string ?? "";
                PropertyInfo p = t.GetProperty("Id", ALL | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(tr, null) as string ?? "";
            }
            return "";
        }

        static string ReadTraitDesc(object tr, string tid)
        {
            string raw = null;
            if (tr != null && s_mTraitGetTooltip != null)
            {
                try { raw = s_mTraitGetTooltip.Invoke(tr, null) as string; } catch { }
            }
            if (string.IsNullOrEmpty(raw) && tr != null && s_fTraitDesc != null)
            {
                try { raw = s_fTraitDesc.GetValue(tr) as string; } catch { }
            }
            if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(tid) && s_mStringsTryGet != null)
            {
                try
                {
                    object[] args = new object[] { "STRINGS.DUPLICANTS.TRAITS." + tid.ToUpperInvariant() + ".DESC", null };
                    if ((bool)s_mStringsTryGet.Invoke(null, args) && args[1] != null && s_fStringEntryVal != null)
                        raw = s_fStringEntryVal.GetValue(args[1]) as string;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(raw)) return "";
            string clean = CleanRichText(raw);
            return clean.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "；")
                .Replace('|', '/').Replace(';', '，').Replace(':', '：').Trim();
        }

        static object GetDbTraitsSet()
        {
            if (s_mDbGet == null || s_fDbTraits == null) return null;
            object db = s_mDbGet.Invoke(null, null);
            return db != null ? s_fDbTraits.GetValue(db) : null;
        }

        static object ResolveTraitById(object dbTraits, string traitId)
        {
            if (dbTraits == null || string.IsNullOrEmpty(traitId)) return null;
            if (s_mResTryGet == null)
                s_mResTryGet = dbTraits.GetType().GetMethod("TryGet", ALL, null, new Type[] { typeof(string) }, null);
            if (s_mResTryGet != null)
            {
                try
                {
                    object found = s_mResTryGet.Invoke(dbTraits, new object[] { traitId });
                    if (found != null) return found;
                }
                catch { }
            }
            FieldInfo fr = dbTraits.GetType().GetField("resources", ALL);
            IList res = fr != null ? (fr.GetValue(dbTraits) as IList) : null;
            if (res != null)
            {
                for (int i = 0; i < res.Count; i++)
                {
                    object tr = res[i];
                    if (tr != null && string.Equals(ReadTraitId(tr), traitId, StringComparison.OrdinalIgnoreCase))
                        return tr;
                }
            }
            return null;
        }

        static string CmdTraitCatalog()
        {
            object dbTraits = GetDbTraitsSet();
            if (dbTraits == null) return "ERR no-db-traits";

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> rows = new List<string>();

            if (s_tDupStats != null)
            {
                string[,] cats = new string[,]
                {
                    { "GOODTRAITS", "正面特质" },
                    { "BADTRAITS", "负面特质" },
                    { "GENESHUFFLERTRAITS", "基因改造" },
                    { "BIONICUPGRADETRAITS", "仿生升级" },
                    { "BIONICBUGTRAITS", "仿生缺陷" },
                    { "JOYTRAITS", "愉悦反应" },
                    { "STRESSTRAITS", "压力反应" },
                    { "NEEDTRAITS", "生理需求" },
                    { "SPECIALTRAITS", "特殊特质" }
                };
                int n = cats.GetLength(0);
                for (int c = 0; c < n; c++)
                {
                    FieldInfo cf = s_tDupStats.GetField(cats[c, 0], ALL);
                    IEnumerable items = cf != null ? (cf.GetValue(null) as IEnumerable) : null;
                    if (items == null) continue;
                    foreach (object item in items)
                    {
                        if (item == null) continue;
                        string tid = item as string;
                        if (string.IsNullOrEmpty(tid))
                        {
                            FieldInfo fid = item.GetType().GetField("id", ALL) ?? item.GetType().GetField("Id", ALL);
                            if (fid != null) tid = fid.GetValue(item) as string;
                        }
                        if (string.IsNullOrEmpty(tid) || !seen.Add(tid)) continue;
                        object tr = ResolveTraitById(dbTraits, tid);
                        if (tr == null) continue;
                        string tname = s_mTraitGetName != null ? (s_mTraitGetName.Invoke(tr, null) as string ?? tid) : tid;
                        tname = CleanRichText(tname).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
                        bool pos = s_fTraitPositive != null && (bool)s_fTraitPositive.GetValue(tr);
                        string tdesc = ReadTraitDesc(tr, tid);
                        rows.Add(tid + "|" + tname + "|" + (pos ? "1" : "0") + "|" + cats[c, 1] + "|" + tdesc);
                    }
                }
            }

            if (rows.Count == 0)
            {
                FieldInfo fr = dbTraits.GetType().GetField("resources", ALL);
                IList res = fr != null ? (fr.GetValue(dbTraits) as IList) : null;
                if (res != null)
                {
                    for (int i = 0; i < res.Count; i++)
                    {
                        object tr = res[i];
                        if (tr == null) continue;
                        string tid = ReadTraitId(tr);
                        if (string.IsNullOrEmpty(tid) || !seen.Add(tid)) continue;
                        if (tid.IndexOf("BaseTrait", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            tid.IndexOf("Critter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            tid.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            string.Equals(tid, "None", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string tname = s_mTraitGetName != null ? (s_mTraitGetName.Invoke(tr, null) as string ?? tid) : tid;
                        tname = CleanRichText(tname).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
                        if (string.IsNullOrEmpty(tname)) continue;
                        bool pos = s_fTraitPositive != null && (bool)s_fTraitPositive.GetValue(tr);
                        string tdesc = ReadTraitDesc(tr, tid);
                        rows.Add(tid + "|" + tname + "|" + (pos ? "1" : "0") + "|特质|" + tdesc);
                    }
                }
            }

            return "OK traits:" + rows.Count + (rows.Count > 0 ? ("\n" + string.Join("\n", rows.ToArray())) : "");
        }

        static string CmdAddTrait(string arg)
        {
            int sp = arg.IndexOf(' ');
            if (sp <= 0) return "ERR args";
            int instId;
            if (!int.TryParse(arg.Substring(0, sp), out instId)) return "ERR bad-id";
            string traitId = arg.Substring(sp + 1).Trim();
            if (string.IsNullOrEmpty(traitId)) return "ERR args";

            object mi = FindMinionByInstId(instId);
            if (mi == null) return "ERR no-dupe";
            object traitsComp = s_mGetComponent.Invoke(mi, new object[] { s_tTraits });
            if (traitsComp == null || s_mTraitsAdd == null) return "ERR no-traits";

            object dbTraits = GetDbTraitsSet();
            object traitObj = ResolveTraitById(dbTraits, traitId);
            if (traitObj == null) return "ERR unknown-trait";

            bool already = false;
            if (s_mTraitsHasStr != null)
            {
                try { already = (bool)s_mTraitsHasStr.Invoke(traitsComp, new object[] { ReadTraitId(traitObj) }); } catch { }
            }
            if (!already && s_fTraitList != null)
            {
                IList tlist = s_fTraitList.GetValue(traitsComp) as IList;
                if (tlist != null)
                {
                    for (int i = 0; i < tlist.Count; i++)
                    {
                        if (string.Equals(ReadTraitId(tlist[i]), traitId, StringComparison.OrdinalIgnoreCase))
                        {
                            already = true;
                            break;
                        }
                    }
                }
            }
            if (already) return "ERR already-has";

            s_mTraitsAdd.Invoke(traitsComp, new object[] { traitObj });
            return "OK added:" + traitId;
        }

        static string CmdSetAttr(string arg)
        {
            string[] ps = arg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ps.Length < 3) return "ERR args";
            int instId, level;
            if (!int.TryParse(ps[0], out instId) || !int.TryParse(ps[2], out level)) return "ERR bad-num";
            if (level < 0) level = 0;
            if (level > 99) level = 99;
            object mi = FindMinionByInstId(instId);
            if (mi == null) return "ERR no-dupe";
            object attrLvls = s_mGetComponent.Invoke(mi, new object[] { s_tAttrLevels });
            if (attrLvls == null || s_mAttrLevelSet == null) return "ERR no-attrlevels";
            s_mAttrLevelSet.Invoke(attrLvls, new object[] { ps[1], level });
            return "OK setattr:" + ps[1] + "=" + level;
        }

        static string CmdRemoveTrait(string arg)
        {
            int sp = arg.IndexOf(' ');
            if (sp <= 0) return "ERR args";
            int instId;
            if (!int.TryParse(arg.Substring(0, sp), out instId)) return "ERR bad-id";
            string traitId = arg.Substring(sp + 1).Trim();
            object mi = FindMinionByInstId(instId);
            if (mi == null) return "ERR no-dupe";
            object traitsComp = s_mGetComponent.Invoke(mi, new object[] { s_tTraits });
            if (traitsComp == null || s_fTraitList == null || s_mTraitsRemove == null) return "ERR no-traits";
            IList tlist = s_fTraitList.GetValue(traitsComp) as IList;
            if (tlist == null) return "ERR empty";
            int removed = 0;
            for (int i = tlist.Count - 1; i >= 0; i--)
            {
                object tr = tlist[i];
                if (tr == null) continue;
                string tid = ReadTraitId(tr);
                if (string.Equals(tid, traitId, StringComparison.OrdinalIgnoreCase))
                {
                    s_mTraitsRemove.Invoke(traitsComp, new object[] { tr });
                    removed++;
                }
            }
            return removed > 0 ? ("OK removed:" + removed) : "ERR not-found";
        }

        static bool IsValidGameCell(int c)
        {
            if (c < 0 || s_mGridIsValidCell == null) return false;
            return (bool)s_mGridIsValidCell.Invoke(null, new object[] { c });
        }

        static bool TryGetCursorCell(out int cell)
        {
            cell = -1;
            bool appFocused = true;
            if (s_pAppIsFocused != null)
            {
                try { appFocused = (bool)s_pAppIsFocused.GetValue(null, null); } catch { }
            }


            if (appFocused && s_fPcInst != null && s_mPcGetCursor != null && s_mGridPosToCell != null)
            {
                object pc = s_fPcInst.GetValue(null);
                if (pc != null)
                {
                    object pos = s_mPcGetCursor.Invoke(pc, null);
                    int c = (int)s_mGridPosToCell.Invoke(null, new object[] { pos });
                    if (IsValidGameCell(c)) { cell = c; s_lastTargetCell = c; return true; }
                }
            }


            if (s_fSelectToolInst != null)
            {
                try
                {
                    object st = s_fSelectToolInst.GetValue(null);
                    if (st != null)
                    {
                        object sel = s_fSelectSelected != null ? s_fSelectSelected.GetValue(st) : null;
                        if (sel != null)
                        {
                            if (s_tCellSelObj != null && s_mGetComponent != null && s_fCsoSelectedCell != null)
                            {
                                object cso = s_mGetComponent.Invoke(sel, new object[] { s_tCellSelObj });
                                if (cso != null)
                                {
                                    int sc = (int)s_fCsoSelectedCell.GetValue(cso);
                                    if (IsValidGameCell(sc)) { cell = sc; s_lastTargetCell = sc; return true; }
                                }
                            }
                            if (s_fSelectCell != null)
                            {
                                int sc2 = (int)s_fSelectCell.GetValue(st);
                                if (IsValidGameCell(sc2)) { cell = sc2; s_lastTargetCell = sc2; return true; }
                            }
                            if (s_pCompTransform != null && s_mTrGetPos != null && s_mGridPosToCell != null)
                            {
                                object tr = s_pCompTransform.GetValue(sel, null);
                                object spos = tr != null ? s_mTrGetPos.Invoke(tr, null) : null;
                                if (spos != null)
                                {
                                    int sc3 = (int)s_mGridPosToCell.Invoke(null, new object[] { spos });
                                    if (IsValidGameCell(sc3)) { cell = sc3; s_lastTargetCell = sc3; return true; }
                                }
                            }
                        }
                    }
                }
                catch { }
            }


            if (s_tCellSelObj != null && s_fCsoMouseCell != null && s_mUnityFindObjects != null)
            {
                try
                {
                    Array csos = s_mUnityFindObjects.Invoke(null, new object[] { s_tCellSelObj }) as Array;
                    if (csos != null)
                    {
                        for (int i = 0; i < csos.Length; i++)
                        {
                            object cso = csos.GetValue(i);
                            if (cso == null) continue;
                            int mc = (int)s_fCsoMouseCell.GetValue(cso);
                            if (IsValidGameCell(mc)) { cell = mc; s_lastTargetCell = mc; return true; }
                        }
                    }
                }
                catch { }
            }


            if (s_fPcInst != null && s_mPcGetCursor != null && s_mGridPosToCell != null)
            {
                object pc = s_fPcInst.GetValue(null);
                if (pc != null)
                {
                    object pos = s_mPcGetCursor.Invoke(pc, null);
                    int c = (int)s_mGridPosToCell.Invoke(null, new object[] { pos });
                    if (IsValidGameCell(c)) { cell = c; s_lastTargetCell = c; return true; }
                }
            }
            if (IsValidGameCell(s_lastTargetCell)) { cell = s_lastTargetCell; return true; }
            return false;
        }

        static string CmdCellTemp(string arg)
        {
            float deltaC;
            if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out deltaC))
                return "ERR bad-delta";
            int cell;
            if (!TryGetCursorCell(out cell)) return "ERR bad-cell";
            if (s_mSimReplaceElem == null) return "ERR no-sim";
            if (s_fGridElemIdx == null || s_mGridGetElemIdx == null ||
                s_fGridMass == null || s_mGridGetMass == null ||
                s_fGridTemp == null || s_mGridGetTemp == null || s_fElements == null)
                return "ERR grid";

            object idxerElem = s_fGridElemIdx.GetValue(null);
            object idxerMass = s_fGridMass.GetValue(null);
            object idxerTemp = s_fGridTemp.GetValue(null);
            IList elems = s_fElements.GetValue(null) as IList;
            if (idxerElem == null || idxerMass == null || idxerTemp == null || elems == null) return "ERR grid";

            object boxedCell = cell;
            object[] cellArg = new object[] { boxedCell };
            ushort idx = (ushort)s_mGridGetElemIdx.Invoke(idxerElem, cellArg);
            if (idx >= elems.Count) return "ERR elem-idx";
            object elem = elems[idx];
            Type te = elem.GetType();
            int st = Convert.ToInt32(te.GetField("state", ALL).GetValue(elem)) & 3;
            if (st == 3) return "ERR solid";
            float mass = (float)s_mGridGetMass.Invoke(idxerMass, cellArg);
            if (st == 0 || mass <= 0f) return "ERR vacuum";

            object elemIdEnum = te.GetField("id", ALL).GetValue(elem);
            FieldInfo fElName = te.GetField("<name>k__BackingField", ALL);
            string elemName = CleanRichText(fElName != null ? (fElName.GetValue(elem) as string) : "");
            if (string.IsNullOrEmpty(elemName)) elemName = elemIdEnum != null ? elemIdEnum.ToString() : "物质";

            float curK = (float)s_mGridGetTemp.Invoke(idxerTemp, cellArg);
            float newK = curK + deltaC;
            if (newK < 1.5f) newK = 1.5f;
            if (newK > 9999f) newK = 9999f;
            byte di = 255;
            int dc = 0;
            if (s_fGridDisIdx != null && s_mGridGetDisIdx != null)
            {
                object idxerDi = s_fGridDisIdx.GetValue(null);
                if (idxerDi != null) di = (byte)s_mGridGetDisIdx.Invoke(idxerDi, cellArg);
            }
            if (s_fGridDisCnt != null && s_mGridGetDisCnt != null)
            {
                object idxerDc = s_fGridDisCnt.GetValue(null);
                if (idxerDc != null) dc = (int)s_mGridGetDisCnt.Invoke(idxerDc, cellArg);
            }

            s_mSimReplaceElem.Invoke(null, new object[] { cell, elemIdEnum, null, mass, newK, di, dc, -1 });
            int gw = s_fGridW != null ? (int)s_fGridW.GetValue(null) : 0;
            string coord = gw > 0 ? ("(" + (cell % gw) + "," + (cell / gw) + ") ") : "";
            return string.Format(CultureInfo.InvariantCulture, "OK {0} {1}{2:0.#}℃ → {3:0.#}℃",
                elemName, coord, curK - 273.15f, newK - 273.15f);
        }

        static string CmdCellVac(string arg)
        {
            int radius = 0;
            int.TryParse(arg, out radius);
            if (radius < 0) radius = 0;
            if (radius > 3) radius = 3;
            int center;
            if (!TryGetCursorCell(out center)) return "ERR bad-cell";
            if (s_mSimReplaceElem == null || s_simVacuumEnum == null) return "ERR no-sim";
            if (s_fGridElemIdx == null || s_mGridGetElemIdx == null || s_fElements == null) return "ERR grid";

            int gridW = (int)s_fGridW.GetValue(null);
            int cellCount = (int)s_fGridC.GetValue(null);
            object idxerElem = s_fGridElemIdx.GetValue(null);
            IList elems = s_fElements.GetValue(null) as IList;
            if (gridW <= 0 || idxerElem == null || elems == null) return "ERR grid";

            int cx = center % gridW;
            int cy = center / gridW;
            int cleared = 0;
            int skippedSolid = 0;
            FieldInfo fState = elems[0].GetType().GetField("state", ALL);

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    if (gx < 0 || gx >= gridW || gy < 0) continue;
                    int c = gy * gridW + gx;
                    if (c < 0 || c >= cellCount) continue;
                    ushort idx = (ushort)s_mGridGetElemIdx.Invoke(idxerElem, new object[] { c });
                    if (idx >= elems.Count) continue;
                    int st = Convert.ToInt32(fState.GetValue(elems[idx])) & 3;
                    if (st == 3) { skippedSolid++; continue; }
                    s_mSimReplaceElem.Invoke(null, new object[] { c, s_simVacuumEnum, null, 0f, 0f, (byte)255, 0, -1 });
                    cleared++;
                }
            }
            if (cleared == 0 && skippedSolid > 0) return "ERR solid";
            return string.Format(CultureInfo.InvariantCulture, "OK 已抽空 ({0},{1}) 共 {2} 格", cx, cy, cleared);
        }

        static string CmdPopFx(string text)
        {
            if (string.IsNullOrEmpty(text) || s_tPopFX == null || s_mPopSpawnFx == null) return "ERR no-popfx";
            object inst = s_fPopInst != null ? s_fPopInst.GetValue(null) : null;
            if (inst == null) return "ERR no-popfx-inst";
            if (s_fPopReady != null && !(bool)s_fPopReady.GetValue(inst)) return "ERR not-ready";
            object sprite = s_fPopSpritePlus != null ? s_fPopSpritePlus.GetValue(inst) : null;

            object offset = Activator.CreateInstance(s_tVec3);
            bool usedLastCell = false;
            if (s_lastTargetCell >= 0 && s_cVec3F3 != null && s_fGridW != null)
            {
                int gw = (int)s_fGridW.GetValue(null);
                if (gw > 0)
                {
                    offset = s_cVec3F3.Invoke(new object[] { (s_lastTargetCell % gw) + 0.5f, (s_lastTargetCell / gw) + 0.5f, 0f });
                    usedLastCell = true;
                }
            }
            if (!usedLastCell && s_fPcInst != null && s_mPcGetCursor != null)
            {
                object pc = s_fPcInst.GetValue(null);
                if (pc != null) offset = s_mPcGetCursor.Invoke(pc, null);
            }
            s_mPopSpawnFx.Invoke(inst, new object[] { sprite, text, null, offset, 2.0f, false, true });
            return "OK popfx";
        }

        static string CmdSavePath()
        {
            if (s_mSaveGetPath == null) return "ERR no-saveloader";
            object target = null;
            if (!s_mSaveGetPath.IsStatic)
            {
                if (s_pSaveLoaderInst == null) return "ERR no-inst-prop";
                target = s_pSaveLoaderInst.GetValue(null, null);
                if (target == null) return "ERR no-inst";
            }
            string p = s_mSaveGetPath.Invoke(target, null) as string;
            if (string.IsNullOrEmpty(p)) return "ERR empty";
            return "OK " + p;
        }



        static string ExportWorkerDataCatalog(string outPath)
        {
            if (string.IsNullOrEmpty(outPath)) return "ERR bad-outpath";
            try
            {
                string dummy;
                if (!InitReflection(out dummy)) return "ERR " + dummy;
                StringBuilder sb = new StringBuilder(16384);

                IList elems = s_fElements != null ? (s_fElements.GetValue(null) as IList) : null;
                if (elems != null && elems.Count > 0)
                {
                    Type te = elems[0].GetType();
                    FieldInfo fId = te.GetField("id", ALL);
                    FieldInfo fName = te.GetField("<name>k__BackingField", ALL);
                    FieldInfo fSubst = te.GetField("substance", ALL);
                    FieldInfo fUiCol = null, fCol = null, fR = null, fG = null, fB = null;
                    for (int i = 0; i < elems.Count; i++)
                    {
                        object el = elems[i];
                        if (el == null) continue;
                        object idObj = fId != null ? fId.GetValue(el) : null;
                        int idHash = idObj != null ? Convert.ToInt32(idObj) : 0;
                        string idStr = idObj != null ? idObj.ToString() : "";
                        string nm = CleanRichText(fName != null ? (fName.GetValue(el) as string) : "")
                            .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                        byte rr = 120, gg = 120, bb = 120;
                        object sub = fSubst != null ? fSubst.GetValue(el) : null;
                        if (sub != null)
                        {
                            if (fUiCol == null)
                            {
                                fUiCol = sub.GetType().GetField("uiColour", ALL);
                                fCol = sub.GetType().GetField("colour", ALL);
                            }
                            object c32 = fUiCol != null ? fUiCol.GetValue(sub) : null;
                            if (c32 == null && fCol != null) c32 = fCol.GetValue(sub);
                            if (c32 != null)
                            {
                                if (fR == null)
                                {
                                    Type tc = c32.GetType();
                                    fR = tc.GetField("r", ALL);
                                    fG = tc.GetField("g", ALL);
                                    fB = tc.GetField("b", ALL);
                                }
                                rr = (byte)fR.GetValue(c32);
                                gg = (byte)fG.GetValue(c32);
                                bb = (byte)fB.GetValue(c32);
                            }
                        }
                        sb.Append("E\t").Append(idHash).Append('\t').Append(idStr).Append('\t')
                          .Append(rr).Append('\t').Append(gg).Append('\t').Append(bb).Append('\t').Append(nm).Append('\n');
                    }
                }

                object gcfg = Activator.CreateInstance(s_tGeyserGenericConfig);
                IList configs = s_mGeyserGenConfigs != null ? (s_mGeyserGenConfigs.Invoke(gcfg, null) as IList) : null;
                if (configs != null)
                {
                    for (int i = 0; i < configs.Count; i++)
                    {
                        object p = configs[i];
                        Type tp = p.GetType();
                        bool isGeneric = (bool)tp.GetField("isGenericGeyser", ALL).GetValue(p);
                        object gt = tp.GetField("geyserType", ALL).GetValue(p);
                        if (gt == null) continue;
                        Type tgt = gt.GetType();

                        string prefabId = tp.GetField("id", ALL).GetValue(p) as string ?? "";
                        string rawId = tgt.GetField("id", ALL).GetValue(gt) as string ?? "";
                        int elemHash = Convert.ToInt32(tgt.GetField("element", ALL).GetValue(gt));
                        int shapeVal = Convert.ToInt32(tgt.GetField("shape", ALL).GetValue(gt));
                        float temp = (float)tgt.GetField("temperature", ALL).GetValue(gt);
                        float minR = (float)tgt.GetField("minRatePerCycle", ALL).GetValue(gt);
                        float maxR = (float)tgt.GetField("maxRatePerCycle", ALL).GetValue(gt);
                        float maxP = (float)tgt.GetField("maxPressure", ALL).GetValue(gt);
                        float minIL = (float)tgt.GetField("minIterationLength", ALL).GetValue(gt);
                        float maxIL = (float)tgt.GetField("maxIterationLength", ALL).GetValue(gt);
                        float minIP = (float)tgt.GetField("minIterationPercent", ALL).GetValue(gt);
                        float maxIP = (float)tgt.GetField("maxIterationPercent", ALL).GetValue(gt);
                        float minYL = (float)tgt.GetField("minYearLength", ALL).GetValue(gt);
                        float maxYL = (float)tgt.GetField("maxYearLength", ALL).GetValue(gt);
                        float minYP = (float)tgt.GetField("minYearPercent", ALL).GetValue(gt);
                        float maxYP = (float)tgt.GetField("maxYearPercent", ALL).GetValue(gt);
                        float gTemp = (float)tgt.GetField("geyserTemperature", ALL).GetValue(gt);
                        MethodInfo mReq = tgt.GetMethod("GetRequiredDlcIds", ALL);
                        MethodInfo mForb = tgt.GetMethod("GetForbiddenDlcIds", ALL);
                        string[] reqDlc = mReq != null ? (mReq.Invoke(gt, null) as string[]) : null;
                        string[] forbDlc = mForb != null ? (mForb.Invoke(gt, null) as string[]) : null;
                        object skObj = tp.GetField("nameStringKey", ALL).GetValue(p);
                        string nameKey = skObj != null ? (skObj.GetType().GetField("String", ALL).GetValue(skObj) as string) : null;
                        string locName = CleanRichText(TryGetString(nameKey) ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

                        sb.Append("G\t").Append(prefabId).Append('\t').Append(rawId).Append('\t')
                          .Append(elemHash).Append('\t').Append(shapeVal).Append('\t')
                          .Append(temp.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(minR.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxR.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxP.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(minIL.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxIL.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(minIP.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxIP.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(minYL.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxYL.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(minYP.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(maxYP.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(gTemp.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(reqDlc != null ? string.Join(",", reqDlc) : "").Append('\t')
                          .Append(forbDlc != null ? string.Join(",", forbDlc) : "").Append('\t')
                          .Append(isGeneric ? "1" : "0").Append('\t')
                          .Append(locName).Append('\n');
                    }
                }

                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string tmp = outPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(outPath)) File.Delete(outPath);
                File.Move(tmp, outPath);
                return "OK catalog-written";
            }
            catch (Exception ex)
            {
                return "ERR exc:" + SanitizeSingleLine(ex.Message);
            }
        }



        static List<object> CollectAllLiveGeysers()
        {
            List<object> list = new List<object>();
            HashSet<int> seenIds = new HashSet<int>();
            if (s_fCompGeysers != null)
            {
                try
                {
                    object gComp = s_fCompGeysers.GetValue(null);
                    if (gComp != null)
                    {
                        Type gt = gComp.GetType();
                        PropertyInfo piItems = gt.GetProperty("Items", ALL);
                        if (piItems != null)
                        {
                            IList items = piItems.GetValue(gComp, null) as IList;
                            if (items != null)
                            {
                                for (int i = 0; i < items.Count; i++)
                                {
                                    object g = items[i];
                                    if (g != null && seenIds.Add(GetUnityInstanceId(g))) list.Add(g);
                                }
                            }
                        }
                        else
                        {
                            MethodInfo mWorldIds = gt.GetMethod("GetWorldsIds", ALL, null, Type.EmptyTypes, null)
                                ?? gt.GetMethod("GetWorldIds", ALL, null, Type.EmptyTypes, null);
                            MethodInfo mGetItems = gt.GetMethod("GetItems", ALL, null, new Type[] { typeof(int) }, null);
                            if (mWorldIds != null && mGetItems != null)
                            {
                                IEnumerable wids = mWorldIds.Invoke(gComp, null) as IEnumerable;
                                if (wids != null)
                                {
                                    foreach (object widObj in wids)
                                    {
                                        IList items = mGetItems.Invoke(gComp, new object[] { widObj }) as IList;
                                        if (items == null) continue;
                                        for (int i = 0; i < items.Count; i++)
                                        {
                                            object g = items[i];
                                            if (g != null && seenIds.Add(GetUnityInstanceId(g))) list.Add(g);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            if (s_mUnityFindObjects != null && s_tGeyser != null)
            {
                try
                {
                    Array found = s_mUnityFindObjects.Invoke(null, new object[] { s_tGeyser }) as Array;
                    if (found != null)
                    {
                        for (int i = 0; i < found.Length; i++)
                        {
                            object g = found.GetValue(i);
                            if (g != null && seenIds.Add(GetUnityInstanceId(g))) list.Add(g);
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        static object FindGeyserByInstId(int instId)
        {
            List<object> all = CollectAllLiveGeysers();
            for (int i = 0; i < all.Count; i++)
            {
                if (GetUnityInstanceId(all[i]) == instId) return all[i];
            }
            return null;
        }

        static float ClampRollSafe(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        static void ClearGeyserMemoizers(object geyser)
        {
            if (geyser == null || s_geyserMemoizerFields == null) return;
            for (int i = 0; i < s_geyserMemoizerFields.Count; i++)
            {
                try
                {
                    object mem = s_geyserMemoizerFields[i].GetValue(geyser);
                    if (mem != null)
                    {
                        MethodInfo mClear = mem.GetType().GetMethod("Clear", ALL, null, Type.EmptyTypes, null);
                        if (mClear != null) mClear.Invoke(mem, null);
                    }
                }
                catch { }
            }
            if (s_mKMonoTrigger != null)
            {
                try { s_mKMonoTrigger.Invoke(geyser, new object[] { 1980521255, null }); } catch { }
            }
        }

        static string GetWorldDisplayNameSafe(int worldId, out int offX, out int offY)
        {
            offX = 0;
            offY = 0;
            string name = "小行星 #" + worldId;
            try
            {
                object cm = s_fClusterMgrInst != null ? s_fClusterMgrInst.GetValue(null) : null;
                if (cm != null && s_mClusterGetWorld != null)
                {
                    object wc = s_mClusterGetWorld.Invoke(cm, new object[] { worldId });
                    if (wc != null)
                    {
                        string resolved = null;
                        if (s_tClusterGridEntity != null && s_pCgeName != null && s_mGetComponent != null)
                        {
                            try
                            {
                                object cge = s_mGetComponent.Invoke(wc, new object[] { s_tClusterGridEntity });
                                if (cge != null)
                                    resolved = CleanRichText(s_pCgeName.GetValue(cge, null) as string);
                            }
                            catch { }
                        }
                        if (string.IsNullOrEmpty(resolved) && s_fWorldOverrideName != null)
                        {
                            string ov = s_fWorldOverrideName.GetValue(wc) as string;
                            if (!string.IsNullOrEmpty(ov))
                                resolved = CleanRichText(TryGetString(ov) ?? ov);
                        }
                        if (string.IsNullOrEmpty(resolved) && s_fWorldName != null)
                        {
                            string wn = s_fWorldName.GetValue(wc) as string;
                            if (!string.IsNullOrEmpty(wn))
                                resolved = CleanRichText(TryGetString(wn) ?? wn);
                        }
                        if (!string.IsNullOrEmpty(resolved)) name = resolved;

                        if (s_mWorldGetOffset != null)
                        {
                            object wo = s_mWorldGetOffset.Invoke(wc, null);
                            if (wo != null && s_fV2ix != null && s_fV2iy != null)
                            {
                                offX = (int)s_fV2ix.GetValue(wo);
                                offY = (int)s_fV2iy.GetValue(wo);
                            }
                        }
                    }
                }
            }
            catch { }
            return name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        static void MarkStudyableStudied(object studyable)
        {
            if (studyable == null || s_fStudyStudied == null) return;
            try
            {
                s_fStudyStudied.SetValue(studyable, true);
                if (s_fStudyMarked != null) s_fStudyMarked.SetValue(studyable, false);
                if (s_mStudyCancel != null) { try { s_mStudyCancel.Invoke(studyable, null); } catch { } }
                if (s_mStudyRefresh != null) { try { s_mStudyRefresh.Invoke(studyable, null); } catch { } }
                if (s_mStudyTrigger != null) { try { s_mStudyTrigger.Invoke(studyable, new object[] { -1436775550, null }); } catch { } }
            }
            catch { }
        }

        static string CmdGeyserList()
        {
            if (s_tGeyser == null || s_fGeyserConfig == null) return "ERR no-geyser-type";
            List<object> list = CollectAllLiveGeysers();
            CachedGeyserPool pool = GetOrBuildGeyserPool("");
            StringBuilder sb = new StringBuilder();
            int validCount = 0;
            StringBuilder rows = new StringBuilder();

            for (int i = 0; i < list.Count; i++)
            {
                object g = list[i];
                try
                {
                    int instId = GetUnityInstanceId(g);
                    object cfg = s_fGeyserConfig.GetValue(g);
                    if (cfg == null) continue;
                    object gt = s_mGicGetType != null ? s_mGicGetType.Invoke(cfg, null) : null;
                    if (gt == null) continue;

                    string rawId = s_fGtId != null ? (s_fGtId.GetValue(gt) as string ?? "") : "";
                    string prefabId = "";
                    try
                    {
                        object kpid = s_tKPrefabId != null ? s_mGetComponent.Invoke(g, new object[] { s_tKPrefabId }) : null;
                        object tag = (kpid != null && s_fPrefabTag != null) ? s_fPrefabTag.GetValue(kpid) : null;
                        prefabId = (tag != null && s_pTagName != null) ? (s_pTagName.GetValue(tag, null) as string ?? "") : "";
                    }
                    catch { }
                    if (string.IsNullOrEmpty(prefabId)) prefabId = "GeyserGeneric_" + rawId;

                    string cat = ClassifyPoi(prefabId) ?? ClassifyPoi("GeyserGeneric_" + rawId) ?? "water_geyser";
                    string disp = "";
                    try
                    {
                        object ks = s_tKSelectable != null ? s_mGetComponent.Invoke(g, new object[] { s_tKSelectable }) : null;
                        if (ks != null && s_mKSelectGetName != null)
                            disp = CleanRichText(s_mKSelectGetName.Invoke(ks, null) as string);
                    }
                    catch { }
                    if (string.IsNullOrEmpty(disp) && pool != null)
                    {
                        GeyserPoolEntry pe;
                        if (pool.AllByPrefab.TryGetValue(prefabId, out pe) && !string.IsNullOrEmpty(pe.LocalizedName))
                            disp = pe.LocalizedName;
                        else if (pool.AllByRaw.TryGetValue(rawId, out pe) && !string.IsNullOrEmpty(pe.LocalizedName))
                            disp = pe.LocalizedName;
                    }
                    if (string.IsNullOrEmpty(disp)) disp = prefabId;
                    disp = disp.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

                    int worldId = 0;
                    if (s_mGetMyWorldId != null)
                        try { worldId = (int)s_mGetMyWorldId.Invoke(null, new object[] { g }); } catch { }
                    int offX, offY;
                    string worldName = GetWorldDisplayNameSafe(worldId, out offX, out offY);

                    int lx = 0, ly = 0;
                    try
                    {
                        object tr = s_pCompTransform != null ? s_pCompTransform.GetValue(g, null) : null;
                        object pos = (tr != null && s_mTrGetPos != null) ? s_mTrGetPos.Invoke(tr, null) : null;
                        if (pos != null && s_mGridPosToCell != null && s_fGridW != null)
                        {
                            int cell = (int)s_mGridPosToCell.Invoke(null, new object[] { pos });
                            int gw = (int)s_fGridW.GetValue(null);
                            if (cell >= 0 && gw > 0)
                            {
                                lx = (cell % gw) - offX;
                                ly = (cell / gw) - offY;
                            }
                        }
                    }
                    catch { }

                    bool studied = false;
                    object stComp = s_fGeyserStudyable != null ? s_fGeyserStudyable.GetValue(g) : null;
                    if (stComp != null && s_fStudyStudied != null)
                    {
                        object sv = s_fStudyStudied.GetValue(stComp);
                        if (sv is bool) studied = (bool)sv;
                    }

                    float avgKgS = (float)s_mGicGetAvg.Invoke(cfg, null);
                    float emitKgS = (float)s_mGicGetEmit.Invoke(cfg, null);
                    float tempK = (float)s_mGicGetTemp.Invoke(cfg, null);
                    float onSec = (float)s_mGicGetOnDur.Invoke(cfg, null);
                    float iterSec = (float)s_mGicGetIterLen.Invoke(cfg, null);
                    float yOnSec = (float)s_mGicGetYearOnDur.Invoke(cfg, null);
                    float yLenSec = (float)s_mGicGetYearLen.Invoke(cfg, null);

                    float pMin = 0f, pMax = 1f;
                    try
                    {
                        object gc = s_tGeyserCfg != null ? s_mGetComponent.Invoke(g, new object[] { s_tGeyserCfg }) : null;
                        if (gc != null && s_fGcPresetMin != null && s_fGcPresetMax != null)
                        {
                            pMin = (float)s_fGcPresetMin.GetValue(gc);
                            pMax = (float)s_fGcPresetMax.GetValue(gc);
                        }
                    }
                    catch { }

                    float pct = ComputeAnalyticalPercentile(gt, avgKgS, pMin, pMax);
                    byte grade = pct >= 85f ? (byte)1 : (pct >= 65f ? (byte)2 : (pct >= 35f ? (byte)3 : (byte)4));

                    float rRate = (float)s_fGicRateRoll.GetValue(cfg);
                    float rIterLen = (float)s_fGicIterLenRoll.GetValue(cfg);
                    float rIterPct = (float)s_fGicIterPctRoll.GetValue(cfg);
                    float rYearLen = (float)s_fGicYearLenRoll.GetValue(cfg);
                    float rYearPct = (float)s_fGicYearPctRoll.GetValue(cfg);

                    float minRate = s_fGtMinRate != null ? (float)s_fGtMinRate.GetValue(gt) : 0f;
                    float maxRate = s_fGtMaxRate != null ? (float)s_fGtMaxRate.GetValue(gt) : 0f;
                    float minYp = s_fGtMinYearPct != null ? (float)s_fGtMinYearPct.GetValue(gt) : 0f;
                    float maxYp = s_fGtMaxYearPct != null ? (float)s_fGtMaxYearPct.GetValue(gt) : 1f;
                    float loR = minRate + (maxRate - minRate) * pMin;
                    float hiR = minRate + (maxRate - minRate) * pMax;
                    float loYp = minYp + (maxYp - minYp) * pMin;
                    float hiYp = minYp + (maxYp - minYp) * pMax;
                    float minAvgGps = (loR / 600f) * loYp * 1000f;
                    float maxAvgGps = (hiR / 600f) * hiYp * 1000f;
                    float minIl = s_fGtMinIterLen != null ? (float)s_fGtMinIterLen.GetValue(gt) : 60f;
                    float maxIl = s_fGtMaxIterLen != null ? (float)s_fGtMaxIterLen.GetValue(gt) : 1140f;
                    float minIp = s_fGtMinIterPct != null ? (float)s_fGtMinIterPct.GetValue(gt) : 0.1f;
                    float maxIp = s_fGtMaxIterPct != null ? (float)s_fGtMaxIterPct.GetValue(gt) : 0.9f;
                    float minYl = s_fGtMinYearLen != null ? (float)s_fGtMinYearLen.GetValue(gt) : 15000f;
                    float maxYl = s_fGtMaxYearLen != null ? (float)s_fGtMaxYearLen.GetValue(gt) : 135000f;

                    rows.Append('\n')
                        .Append(instId).Append('\t')
                        .Append(worldId).Append('\t')
                        .Append(worldName).Append('\t')
                        .Append(lx).Append('\t')
                        .Append(ly).Append('\t')
                        .Append(prefabId).Append('\t')
                        .Append(rawId).Append('\t')
                        .Append(cat).Append('\t')
                        .Append(disp).Append('\t')
                        .Append(studied ? 1 : 0).Append('\t')
                        .Append((avgKgS * 1000f).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                        .Append((emitKgS * 1000f).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                        .Append((tempK - 273.15f).ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(onSec.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Math.Max(0f, iterSec - onSec).ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                        .Append((yOnSec / 600f).ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Math.Max(0f, (yLenSec - yOnSec) / 600f).ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(grade).Append('\t')
                        .Append(pct.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(rRate.ToString("0.#####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(rIterLen.ToString("0.#####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(rIterPct.ToString("0.#####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(rYearLen.ToString("0.#####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(rYearPct.ToString("0.#####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(minAvgGps.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(maxAvgGps.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(loR.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(hiR.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(loYp.ToString("0.####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(hiYp.ToString("0.####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(minIp.ToString("0.####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(maxIp.ToString("0.####", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(minIl.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(maxIl.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(minYl.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(maxYl.ToString("0.##", CultureInfo.InvariantCulture));
                    validCount++;
                }
                catch { }
            }

            sb.Append("OK geysers:").Append(validCount).Append(rows.ToString());
            return sb.ToString();
        }

        static string CmdGeyserCatalog()
        {
            if (s_tGeyserGenericConfig == null || s_mGeyserGenConfigs == null) return "ERR no-geyser-cfg";
            object gcfg = Activator.CreateInstance(s_tGeyserGenericConfig);
            IList configs = s_mGeyserGenConfigs.Invoke(gcfg, null) as IList;
            if (configs == null) return "ERR empty-configs";

            StringBuilder rows = new StringBuilder();
            int count = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < configs.Count; i++)
            {
                object p = configs[i];
                Type tp = p.GetType();
                string prefabId = tp.GetField("id", ALL).GetValue(p) as string ?? "";
                if (string.IsNullOrEmpty(prefabId) || !seen.Add(prefabId)) continue;
                object gt = tp.GetField("geyserType", ALL).GetValue(p);
                if (gt == null) continue;


                if (s_mAssetsGetPrefab != null && s_cTagStr != null)
                {
                    object tag = s_cTagStr.Invoke(new object[] { prefabId });
                    if (s_mAssetsGetPrefab.Invoke(null, new object[] { tag }) == null) continue;
                }

                Type tgt = gt.GetType();
                string rawId = tgt.GetField("id", ALL).GetValue(gt) as string ?? "";
                float tempK = (float)tgt.GetField("temperature", ALL).GetValue(gt);
                float minRate = (float)tgt.GetField("minRatePerCycle", ALL).GetValue(gt);
                float maxRate = (float)tgt.GetField("maxRatePerCycle", ALL).GetValue(gt);
                float minYp = (float)tgt.GetField("minYearPercent", ALL).GetValue(gt);
                float maxYp = (float)tgt.GetField("maxYearPercent", ALL).GetValue(gt);
                float minAvgGps = (minRate / 600f) * minYp * 1000f;
                float maxAvgGps = (maxRate / 600f) * maxYp * 1000f;

                object skObj = tp.GetField("nameStringKey", ALL).GetValue(p);
                string nameKey = skObj != null ? (skObj.GetType().GetField("String", ALL).GetValue(skObj) as string) : null;
                string loc = CleanRichText(TryGetString(nameKey) ?? "");
                if (string.IsNullOrEmpty(loc)) loc = prefabId;
                loc = loc.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

                string cat = ClassifyPoi(prefabId) ?? ClassifyPoi("GeyserGeneric_" + rawId) ?? "water_geyser";
                rows.Append('\n')
                    .Append(prefabId).Append('\t')
                    .Append(rawId).Append('\t')
                    .Append(cat).Append('\t')
                    .Append(loc).Append('\t')
                    .Append((tempK - 273.15f).ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(minAvgGps.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(maxAvgGps.ToString("0.##", CultureInfo.InvariantCulture));
                count++;
            }

            return "OK catalog:" + count + rows.ToString();
        }

        static string CmdGeyserSet(string arg)
        {
            string[] ps = (arg ?? "").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ps.Length < 6) return "ERR args";
            int instId;
            if (!int.TryParse(ps[0], out instId)) return "ERR bad-id";

            float rRate, rIterLen, rIterPct, rYearLen, rYearPct;
            if (!float.TryParse(ps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rRate) ||
                !float.TryParse(ps[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rIterLen) ||
                !float.TryParse(ps[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rIterPct) ||
                !float.TryParse(ps[4], NumberStyles.Float, CultureInfo.InvariantCulture, out rYearLen) ||
                !float.TryParse(ps[5], NumberStyles.Float, CultureInfo.InvariantCulture, out rYearPct))
                return "ERR bad-num";

            bool markStudied = ps.Length >= 7 && ps[6] == "1";

            object g = FindGeyserByInstId(instId);
            if (g == null) return "ERR not-found";
            object cfg = s_fGeyserConfig != null ? s_fGeyserConfig.GetValue(g) : null;
            if (cfg == null) return "ERR no-cfg";

            s_fGicRateRoll.SetValue(cfg, ClampRollSafe(rRate));
            s_fGicIterLenRoll.SetValue(cfg, ClampRollSafe(rIterLen));
            s_fGicIterPctRoll.SetValue(cfg, ClampRollSafe(rIterPct));
            s_fGicYearLenRoll.SetValue(cfg, ClampRollSafe(rYearLen));
            s_fGicYearPctRoll.SetValue(cfg, ClampRollSafe(rYearPct));

            s_mGicInit.Invoke(cfg, new object[] { true });
            if (s_mGeyserUpdateMod != null) s_mGeyserUpdateMod.Invoke(g, null);
            ClearGeyserMemoizers(g);

            if (markStudied && s_fGeyserStudyable != null)
                MarkStudyableStudied(s_fGeyserStudyable.GetValue(g));
            if (s_mGeyserRefreshMeter != null)
                try { s_mGeyserRefreshMeter.Invoke(g, null); } catch { }

            float avgKgS = (float)s_mGicGetAvg.Invoke(cfg, null);
            float emitKgS = (float)s_mGicGetEmit.Invoke(cfg, null);
            object gt = s_mGicGetType != null ? s_mGicGetType.Invoke(cfg, null) : null;
            float pct = ComputeAnalyticalPercentile(gt, avgKgS, 0f, 1f);
            byte grade = pct >= 85f ? (byte)1 : (pct >= 65f ? (byte)2 : (pct >= 35f ? (byte)3 : (byte)4));
            return string.Format(CultureInfo.InvariantCulture, "OK set:{0}|{1:0.###}|{2:0.###}|{3}|{4:0.#}",
                instId, avgKgS * 1000f, emitKgS * 1000f, grade, pct);
        }

        static string CmdGeyserType(string arg)
        {
            string[] ps = (arg ?? "").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ps.Length < 7) return "ERR args";
            int instId;
            if (!int.TryParse(ps[0], out instId)) return "ERR bad-id";
            string targetPrefabId = ps[1].Trim();
            float rRate, rIterLen, rIterPct, rYearLen, rYearPct;
            if (!float.TryParse(ps[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rRate) ||
                !float.TryParse(ps[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rIterLen) ||
                !float.TryParse(ps[4], NumberStyles.Float, CultureInfo.InvariantCulture, out rIterPct) ||
                !float.TryParse(ps[5], NumberStyles.Float, CultureInfo.InvariantCulture, out rYearLen) ||
                !float.TryParse(ps[6], NumberStyles.Float, CultureInfo.InvariantCulture, out rYearPct))
                return "ERR bad-num";
            bool keepStudied = ps.Length >= 8 && ps[7] == "1";

            if (s_mAssetsGetPrefab == null || s_cTagStr == null || s_mUtilKInst2 == null || s_mUtilKDest1 == null)
                return "ERR no-util-api";

            object oldG = FindGeyserByInstId(instId);
            if (oldG == null) return "ERR not-found";

            object tag = s_cTagStr.Invoke(new object[] { targetPrefabId });
            object prefab = s_mAssetsGetPrefab.Invoke(null, new object[] { tag });
            if (prefab == null) return "ERR unknown-prefab";

            object prefabGeyser = s_mGoGetComp != null ? s_mGoGetComp.Invoke(prefab, new object[] { s_tGeyser }) : null;
            if (prefabGeyser == null) return "ERR not-a-geyser";

            bool wasStudied = false;
            if (s_fGeyserStudyable != null && s_fStudyStudied != null)
            {
                object oldSt = s_fGeyserStudyable.GetValue(oldG);
                if (oldSt != null)
                {
                    object sv = s_fStudyStudied.GetValue(oldSt);
                    if (sv is bool) wasStudied = (bool)sv;
                }
            }

            PropertyInfo pCompGo = s_asmCore.GetType("UnityEngine.Component", true).GetProperty("gameObject", ALL);
            object oldGo = pCompGo.GetValue(oldG, null);
            object oldTr = s_pCompTransform.GetValue(oldG, null);
            object oldPos = s_mTrGetPos.Invoke(oldTr, null);

            s_mUtilKDest1.Invoke(null, new object[] { oldGo });

            object newGo = s_mUtilKInst2.Invoke(null, new object[] { prefab, oldPos });
            if (newGo == null) return "ERR instantiate-failed";

            object newG = s_mGoGetComp.Invoke(newGo, new object[] { s_tGeyser });
            object newGc = s_tGeyserCfg != null ? s_mGoGetComp.Invoke(newGo, new object[] { s_tGeyserCfg }) : null;
            object typeHash = (newGc != null && s_fGcPresetType != null) ? s_fGcPresetType.GetValue(newGc) : null;

            rRate = ClampRollSafe(rRate);
            rIterLen = ClampRollSafe(rIterLen);
            rIterPct = ClampRollSafe(rIterPct);
            rYearLen = ClampRollSafe(rYearLen);
            rYearPct = ClampRollSafe(rYearPct);

            if (newG != null && typeHash != null && s_tGeyserInstCfg != null)
            {
                object cfg = s_fGeyserConfig.GetValue(newG);
                if (cfg == null)
                {
                    cfg = Activator.CreateInstance(s_tGeyserInstCfg);
                    s_fGeyserConfig.SetValue(newG, cfg);
                }
                s_fGicTypeId.SetValue(cfg, typeHash);
                s_fGicRateRoll.SetValue(cfg, rRate);
                s_fGicIterLenRoll.SetValue(cfg, rIterLen);
                s_fGicIterPctRoll.SetValue(cfg, rIterPct);
                s_fGicYearLenRoll.SetValue(cfg, rYearLen);
                s_fGicYearPctRoll.SetValue(cfg, rYearPct);
                s_mGicInit.Invoke(cfg, new object[] { true });
            }

            if (s_mGoSetActive != null)
                s_mGoSetActive.Invoke(newGo, new object[] { true });

            if (newG != null)
            {

                object cfg2 = s_fGeyserConfig.GetValue(newG);
                if (cfg2 != null)
                {
                    if (typeHash != null) s_fGicTypeId.SetValue(cfg2, typeHash);
                    s_fGicRateRoll.SetValue(cfg2, rRate);
                    s_fGicIterLenRoll.SetValue(cfg2, rIterLen);
                    s_fGicIterPctRoll.SetValue(cfg2, rIterPct);
                    s_fGicYearLenRoll.SetValue(cfg2, rYearLen);
                    s_fGicYearPctRoll.SetValue(cfg2, rYearPct);
                    s_mGicInit.Invoke(cfg2, new object[] { true });
                }
                if (s_mGeyserUpdateMod != null) s_mGeyserUpdateMod.Invoke(newG, null);
                ClearGeyserMemoizers(newG);
                if (s_mGeyserGenName != null) try { s_mGeyserGenName.Invoke(newG, null); } catch { }
                if ((wasStudied || keepStudied) && s_fGeyserStudyable != null)
                    MarkStudyableStudied(s_fGeyserStudyable.GetValue(newG));
                if (s_mGeyserRefreshMeter != null)
                    try { s_mGeyserRefreshMeter.Invoke(newG, null); } catch { }

                object cfgFinal = s_fGeyserConfig.GetValue(newG);
                float avgKgS = cfgFinal != null ? (float)s_mGicGetAvg.Invoke(cfgFinal, null) : 0f;
                float emitKgS = cfgFinal != null ? (float)s_mGicGetEmit.Invoke(cfgFinal, null) : 0f;
                object gt = (cfgFinal != null && s_mGicGetType != null) ? s_mGicGetType.Invoke(cfgFinal, null) : null;
                float pct = ComputeAnalyticalPercentile(gt, avgKgS, 0f, 1f);
                byte grade = pct >= 85f ? (byte)1 : (pct >= 65f ? (byte)2 : (pct >= 35f ? (byte)3 : (byte)4));
                return string.Format(CultureInfo.InvariantCulture, "OK replaced:{0}|{1:0.###}|{2:0.###}|{3}|{4:0.#}",
                    GetUnityInstanceId(newG), avgKgS * 1000f, emitKgS * 1000f, grade, pct);
            }
            return "OK replaced:0|0|0|0|50";
        }

        static string CmdGeyserFocus(string arg)
        {
            int instId;
            if (!int.TryParse((arg ?? "").Trim(), out instId)) return "ERR bad-id";
            object g = FindGeyserByInstId(instId);
            if (g == null) return "ERR not-found";

            try
            {
                if (s_mGetMyWorldId != null && s_fClusterMgrInst != null && s_mClusterSetActiveWorld != null)
                {
                    int wid = (int)s_mGetMyWorldId.Invoke(null, new object[] { g });
                    object cm = s_fClusterMgrInst.GetValue(null);
                    if (cm != null) s_mClusterSetActiveWorld.Invoke(cm, new object[] { wid });
                }
            }
            catch { }

            object cam = s_fCamCtrlInst != null ? s_fCamCtrlInst.GetValue(null) : null;
            if (cam == null || s_mCamSetTargetPos == null) return "ERR no-camera";
            object tr = s_pCompTransform.GetValue(g, null);
            object pos = s_mTrGetPos.Invoke(tr, null);
            s_mCamSetTargetPos.Invoke(cam, new object[] { pos, 8f, true });
            return "OK focus:" + instId;
        }



        sealed class BuiltInGeyserRow
        {
            public string RawId, ElemName, ReqDlc;
            public int Shape;
            public float Temp, MinR, MaxR, MaxP, MinIL, MaxIL, MinIP, MaxIP, MinYL, MaxYL, MinYP, MaxYP, GTemp;
            public bool IsGeneric;
        }

        static readonly BuiltInGeyserRow[] s_builtInGeysers = new BuiltInGeyserRow[]
        {
            new BuiltInGeyserRow { RawId = "steam", ElemName = "Steam", Shape = 0, Temp = 383.15f, MinR = 1000f, MaxR = 2000f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "hot_steam", ElemName = "Steam", Shape = 0, Temp = 773.15f, MinR = 500f, MaxR = 1000f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "hot_water", ElemName = "Water", Shape = 1, Temp = 368.15f, MinR = 2000f, MaxR = 4000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "slush_water", ElemName = "DirtyWater", Shape = 1, Temp = 263.15f, MinR = 1000f, MaxR = 2000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 263f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "filthy_water", ElemName = "DirtyWater", Shape = 1, Temp = 303.15f, MinR = 2000f, MaxR = 4000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "slush_salt_water", ElemName = "Brine", Shape = 1, Temp = 263.15f, MinR = 1000f, MaxR = 2000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 263f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "salt_water", ElemName = "SaltWater", Shape = 1, Temp = 368.15f, MinR = 2000f, MaxR = 4000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "small_volcano", ElemName = "Magma", Shape = 2, Temp = 2000f, MinR = 400f, MaxR = 800f, MaxP = 150f, MinIL = 6000f, MaxIL = 12000f, MinIP = 0.005f, MaxIP = 0.01f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "big_volcano", ElemName = "Magma", Shape = 2, Temp = 2000f, MinR = 800f, MaxR = 1600f, MaxP = 150f, MinIL = 6000f, MaxIL = 12000f, MinIP = 0.005f, MaxIP = 0.01f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "liquid_co2", ElemName = "LiquidCarbonDioxide", Shape = 1, Temp = 218f, MinR = 100f, MaxR = 200f, MaxP = 50f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 218f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "hot_co2", ElemName = "CarbonDioxide", Shape = 0, Temp = 773.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "hot_hydrogen", ElemName = "Hydrogen", Shape = 0, Temp = 773.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "hot_po2", ElemName = "ContaminatedOxygen", Shape = 0, Temp = 773.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "slimy_po2", ElemName = "ContaminatedOxygen", Shape = 0, Temp = 333.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "chlorine_gas", ElemName = "ChlorineGas", Shape = 0, Temp = 333.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "chlorine_gas_cool", ElemName = "ChlorineGas", Shape = 0, Temp = 278.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = false },
            new BuiltInGeyserRow { RawId = "methane", ElemName = "Methane", Shape = 0, Temp = 423.15f, MinR = 70f, MaxR = 140f, MaxP = 5f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "molten_copper", ElemName = "MoltenCopper", Shape = 2, Temp = 2500f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "molten_iron", ElemName = "MoltenIron", Shape = 2, Temp = 2800f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "molten_gold", ElemName = "MoltenGold", Shape = 2, Temp = 2900f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "molten_aluminum", ElemName = "MoltenAluminum", Shape = 2, Temp = 2000f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "EXPANSION1_ID", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "molten_tungsten", ElemName = "MoltenTungsten", Shape = 2, Temp = 4000f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "EXPANSION1_ID", IsGeneric = false },
            new BuiltInGeyserRow { RawId = "molten_niobium", ElemName = "MoltenNiobium", Shape = 2, Temp = 3500f, MinR = 800f, MaxR = 1600f, MaxP = 150f, MinIL = 6000f, MaxIL = 12000f, MinIP = 0.005f, MaxIP = 0.01f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "EXPANSION1_ID", IsGeneric = false },
            new BuiltInGeyserRow { RawId = "molten_cobalt", ElemName = "MoltenCobalt", Shape = 2, Temp = 2500f, MinR = 200f, MaxR = 400f, MaxP = 150f, MinIL = 480f, MaxIL = 1080f, MinIP = 0.01666667f, MaxIP = 0.1f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "EXPANSION1_ID", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "oil_drip", ElemName = "CrudeOil", Shape = 1, Temp = 600f, MinR = 1f, MaxR = 250f, MaxP = 50f, MinIL = 600f, MaxIL = 600f, MinIP = 1f, MaxIP = 1f, MinYL = 100f, MaxYL = 500f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "liquid_sulfur", ElemName = "LiquidSulfur", Shape = 1, Temp = 438.35f, MinR = 1000f, MaxR = 2000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "EXPANSION1_ID", IsGeneric = true },
            new BuiltInGeyserRow { RawId = "murky_brine", ElemName = "Brine", Shape = 1, Temp = 368.15f, MinR = 2000f, MaxR = 4000f, MaxP = 500f, MinIL = 60f, MaxIL = 1140f, MinIP = 0.1f, MaxIP = 0.9f, MinYL = 15000f, MaxYL = 135000f, MinYP = 0.4f, MaxYP = 0.8f, GTemp = 372.15f, ReqDlc = "DLC5_ID", IsGeneric = false }
        };

        static uint GetFallbackElemRgb(string elemId, int state)
        {
            switch (elemId)
            {
                case "Void": return 0x0E1015u;
                case "Vacuum": return 0x0E1015u;
                case "Oxygen": return 0x7CD0FFu;
                case "ContaminatedOxygen": return 0x7DA864u;
                case "CarbonDioxide": return 0x525763u;
                case "Hydrogen": return 0xE664B4u;
                case "ChlorineGas": return 0x86C94Eu;
                case "Methane": return 0xD8823Bu;
                case "Steam": return 0xC8DCE8u;
                case "SourGas": return 0x75884Cu;
                case "Water": return 0x3A9CF2u;
                case "DirtyWater": return 0x6D7D43u;
                case "SaltWater": return 0x5FB7E8u;
                case "Brine": return 0x88C6D6u;
                case "CrudeOil": return 0x3B2F2Bu;
                case "Petroleum": return 0xB88E28u;
                case "Magma": return 0xF05626u;
                case "LiquidSulfur": return 0xD8BE32u;
                case "SandStone": return 0xB88C54u;
                case "Dirt": return 0x78563Cu;
                case "IgneousRock": return 0x6E5D64u;
                case "Granite": return 0x9C7076u;
                case "Obsidian": return 0x453C4Eu;
                case "Katairite": return 0x56396Bu;
                case "SedimentaryRock": return 0x7B7D6Bu;
                case "MaficRock": return 0x62735Cu;
                case "Algae": return 0x437841u;
                case "SlimeMold": return 0x6D8F3Eu;
                case "Carbon": return 0x35373Eu;
                case "Cuprite": return 0xB8643Du;
                case "IronOre": return 0x6C4E49u;
                case "GoldAmalgam": return 0xD8AF3Au;
                case "Wolframite": return 0x647182u;
                case "AluminumOre": return 0x7D9EB3u;
                case "Cobaltite": return 0x4C79B5u;
                case "Sand": return 0xD2C187u;
                case "Phosphorite": return 0x885C84u;
                case "BleachStone": return 0xB2C994u;
                case "OxyRock": return 0x66B0DCu;
                case "Fertilizer": return 0x8A7152u;
                case "Rust": return 0x9D4D33u;
                case "Salt": return 0xDFE6ECu;
                case "Ice": return 0x90D4F7u;
                case "Snow": return 0xDCEEF8u;
                case "CrushedIce": return 0x7AC0EBu;
                case "BrineIce": return 0x6FAEC9u;
                case "DirtyIce": return 0x698E80u;
                case "SolidCarbonDioxide": return 0xA8B0BAu;
                case "WoodLog": return 0x7B5030u;
                case "Cinnabar": return 0xBA443Au;
                case "NickelOre": return 0x7D8C78u;
                case "Shale": return 0x5C6066u;
                case "Fossil": return 0x8F8A7Cu;
                case "Unobtanium": return 0x261B36u;
            }
            if ((state & 3) == 0) return 0x161920u;
            int h = Math.Abs((elemId ?? "").GetHashCode());
            byte r = (byte)(75 + (h & 0x5F));
            byte g = (byte)(75 + ((h >> 7) & 0x5F));
            byte b = (byte)(85 + ((h >> 14) & 0x5F));
            return (uint)((r << 16) | (g << 8) | b);
        }

        static void LoadHeadlessPo(string gameDir)
        {
            lock (s_poLock)
            {
                if (s_headlessPo != null) return;
                Dictionary<string, string> po = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    string sa = Path.Combine(gameDir, "OxygenNotIncluded_Data", "StreamingAssets");
                    string zh = Path.Combine(sa, "strings", "strings_preinstalled_zh_klei.po");
                    if (File.Exists(zh))
                    {
                        using (StreamReader sr = new StreamReader(zh, Encoding.UTF8))
                        {
                            string curCtx = null;
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.StartsWith("msgctxt \"", StringComparison.Ordinal))
                                {
                                    if (line.Length >= 10) curCtx = line.Substring(9, line.Length - 10);
                                    else curCtx = null;
                                }
                                else if (curCtx != null && line.StartsWith("msgstr \"", StringComparison.Ordinal))
                                {
                                    if (line.Length >= 9)
                                    {
                                        string val = line.Substring(8, line.Length - 9).Replace("\\\"", "\"").Replace("\\n", "\n");
                                        val = CleanRichText(val);
                                        if (val.Length > 0) po[curCtx] = val;
                                    }
                                    curCtx = null;
                                }
                            }
                        }
                    }
                }
                catch { }
                s_headlessPo = po;
            }
        }

        static void PrepopulateDlcManager(string activeDlcCsv)
        {
            Type tDlc = s_asmFp.GetType("DlcManager", true);
            HashSet<string> active = ParseDlcCsv(activeDlcCsv);
            string[] allKnown = { "EXPANSION1_ID", "DLC2_ID", "DLC3_ID", "DLC4_ID", "DLC5_ID", "COSMETIC1_ID", "COSMETIC2_ID" };

            FieldInfo fMain = tDlc.GetField("IsMainThread", ALL);
            if (fMain != null) fMain.SetValue(null, true);

            string[] dictFields = { "dlcSubscribedCache", "dlcPurchasedCache", "dlcAssetsCache" };
            for (int f = 0; f < dictFields.Length; f++)
            {
                FieldInfo fi = tDlc.GetField(dictFields[f], ALL);
                IDictionary dict = fi != null ? (fi.GetValue(null) as IDictionary) : null;
                if (dict == null) continue;
                for (int i = 0; i < allKnown.Length; i++)
                    dict[allKnown[i]] = active.Contains(allKnown[i]);
            }


            Type tKpp = s_asmFp.GetType("KPlayerPrefs", false);
            if (tKpp != null)
            {
                MethodInfo mGetInst = tKpp.GetMethod("get_instance", ALL);
                object kpp = mGetInst != null ? mGetInst.Invoke(null, null) : null;
                if (kpp == null)
                {
                    kpp = Activator.CreateInstance(tKpp);
                    FieldInfo fInst = tKpp.GetField("_instance", ALL);
                    if (fInst != null) fInst.SetValue(null, kpp);
                }
                if (kpp != null)
                {
                    PropertyInfo pInts = tKpp.GetProperty("ints", ALL);
                    IDictionary ints = pInts != null ? (pInts.GetValue(kpp, null) as IDictionary) : null;
                    if (ints != null)
                    {
                        for (int i = 0; i < allKnown.Length; i++)
                            ints[allKnown[i] + ".ENABLED"] = active.Contains(allKnown[i]) ? 1 : 0;
                    }
                }
            }
        }

        static void SetupHeadlessGlobal()
        {
            try
            {
                Type tGlobal = s_asmCs.GetType("Global", false);
                Type tModMgr = s_asmCs.GetType("KMod.Manager", false);
                if (tGlobal == null || tModMgr == null) return;
                FieldInfo fInst = tGlobal.GetField("<Instance>k__BackingField", ALL);
                if (fInst == null || fInst.GetValue(null) != null) return;

                object g = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(tGlobal);
                object mgr = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(tModMgr);
                FieldInfo fMods = tModMgr.GetField("mods", ALL);
                if (fMods != null && fMods.GetValue(mgr) == null)
                    fMods.SetValue(mgr, Activator.CreateInstance(fMods.FieldType));
                FieldInfo fModMgr = tGlobal.GetField("modManager", ALL);
                if (fModMgr != null) fModMgr.SetValue(g, mgr);
                fInst.SetValue(null, g);
            }
            catch { }
        }

        static bool InitHeadlessWorldGen(string gameDir, string activeDlcCsv, string catalogPath, out string err)
        {
            err = null;
            try
            {
                LoadHeadlessPo(gameDir);
                PrepopulateDlcManager(activeDlcCsv);
                SetupHeadlessGlobal();


                Type tFs = s_asmFp.GetType("Klei.FileSystem", true);
                FieldInfo fSources = tFs.GetField("file_sources", ALL);
                IList sources = fSources != null ? (fSources.GetValue(null) as IList) : null;
                if (sources == null || sources.Count == 0)
                {
                    MethodInfo mFsInit = tFs.GetMethod("Initialize", ALL);
                    if (mFsInit != null) mFsInit.Invoke(null, null);
                }


                MethodInfo mScClear = s_tSettingsCache.GetMethod("Clear", ALL);
                if (mScClear != null)
                {
                    try { mScClear.Invoke(null, null); } catch { }
                }

                Type tYamlErr = s_asmFp.GetType("Klei.YamlIO+Error", false) ?? s_asmFp.GetType("YamlIO+Error", false);
                object errList = tYamlErr != null ? Activator.CreateInstance(typeof(List<>).MakeGenericType(tYamlErr)) : null;
                MethodInfo mLoadFiles = null;
                MethodInfo[] scMethods = s_tSettingsCache.GetMethods(ALL);
                for (int i = 0; i < scMethods.Length; i++)
                {
                    if (scMethods[i].Name == "LoadFiles" && scMethods[i].GetParameters().Length == 1)
                    {
                        mLoadFiles = scMethods[i];
                        break;
                    }
                }
                if (mLoadFiles == null) { err = "no-sc-loadfiles"; return false; }
                mLoadFiles.Invoke(null, new object[] { errList });

                bool tcInitted = false;
                if (s_pTcInitted != null) tcInitted = (bool)s_pTcInitted.GetValue(null, null);
                else if (s_fTcInitted != null) tcInitted = (bool)s_fTcInitted.GetValue(null);
                if (!tcInitted && s_mTcInit != null)
                    s_mTcInit.Invoke(null, null);


                Dictionary<int, uint> liveElemColors = new Dictionary<int, uint>();
                Dictionary<int, string> liveElemNames = new Dictionary<int, string>();
                List<string[]> liveGeyserRows = new List<string[]>();
                if (!string.IsNullOrEmpty(catalogPath) && File.Exists(catalogPath))
                {
                    try
                    {
                        string[] clines = File.ReadAllLines(catalogPath, Encoding.UTF8);
                        for (int i = 0; i < clines.Length; i++)
                        {
                            string cl = clines[i];
                            if (string.IsNullOrEmpty(cl)) continue;
                            string[] tp = cl.Split('\t');
                            if (tp[0] == "E" && tp.Length >= 7)
                            {
                                int eh, r, g, b;
                                if (int.TryParse(tp[1], out eh) && int.TryParse(tp[3], out r) && int.TryParse(tp[4], out g) && int.TryParse(tp[5], out b))
                                {
                                    liveElemColors[eh] = (uint)(((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF));
                                    if (!string.IsNullOrEmpty(tp[6])) liveElemNames[eh] = tp[6];
                                }
                            }
                            else if (tp[0] == "G" && tp.Length >= 22)
                            {
                                liveGeyserRows.Add(tp);
                            }
                        }
                    }
                    catch { }
                }

                InitHeadlessElements(liveElemColors, liveElemNames);
                InitHeadlessGeysers(activeDlcCsv, liveGeyserRows);

                if (s_fWgVoidElem.GetValue(null) == null && s_mWgSetupDefaultElems != null)
                    s_mWgSetupDefaultElems.Invoke(null, null);

                IList chkEl = s_fElements != null ? (s_fElements.GetValue(null) as IList) : null;
                FieldInfo fVac = s_tWorldGen.GetField("vacuumElement", ALL);
                if (chkEl == null || chkEl.Count == 0 || (fVac != null && fVac.GetValue(null) == null))
                {
                    err = string.Format("elems={0} void={1} vac={2}",
                        chkEl != null ? chkEl.Count : -1,
                        s_fWgVoidElem.GetValue(null) != null ? 1 : 0,
                        (fVac != null && fVac.GetValue(null) != null) ? 1 : 0);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                string st = ((inner.StackTrace ?? "") + " || OUTER: " + (e.StackTrace ?? "")).Replace('\r', ' ').Replace('\n', ' ');
                if (st.Length > 1400) st = st.Substring(0, 1400);
                err = inner.GetType().Name + ":" + inner.Message + " | " + st;
                return false;
            }
        }

        static void InitHeadlessElements(Dictionary<int, uint> liveColors, Dictionary<int, string> liveNames)
        {
            IList curElems = s_fElements != null ? (s_fElements.GetValue(null) as IList) : null;
            if (curElems != null && curElems.Count > 10) return;

            Type tElem = s_asmCs.GetType("Element", true);
            Type tSubst = s_asmCs.GetType("Substance", true);
            Type tCol32 = s_asmCore.GetType("UnityEngine.Color32", true);
            Type tDlcMgr = s_asmFp.GetType("DlcManager", true);
            Type tTagMgr = s_asmFp.GetType("TagManager", true);
            MethodInfo mTagCreate2 = tTagMgr.GetMethod("Create", ALL, null, new Type[] { typeof(string), typeof(string) }, null);
            MethodInfo mIsSub = tDlcMgr.GetMethod("IsContentSubscribed", ALL, null, new Type[] { typeof(string) }, null);
            MethodInfo mCollect = s_tElementLoader.GetMethod("CollectElementsFromYAML", ALL, null, Type.EmptyTypes, null);
            MethodInfo mCopy = s_tElementLoader.GetMethod("CopyEntryToElement", ALL);
            MethodInfo mFinal = s_tElementLoader.GetMethod("FinaliseElementsTable", ALL);
            FieldInfo fElemTable = s_tElementLoader.GetField("elementTable", ALL);
            FieldInfo fElemTagTable = s_tElementLoader.GetField("elementTagTable", ALL);

            IList entries = mCollect != null ? (mCollect.Invoke(null, null) as IList) : null;
            if (entries != null && entries.Count > 0)
            {
                IList elemsList = s_fElements.GetValue(null) as IList;
                if (elemsList == null)
                {
                    elemsList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tElem));
                    s_fElements.SetValue(null, elemsList);
                }
                IDictionary elemTable = fElemTable != null ? (fElemTable.GetValue(null) as IDictionary) : null;
                if (elemTable == null && fElemTable != null)
                {
                    elemTable = (IDictionary)Activator.CreateInstance(fElemTable.FieldType);
                    fElemTable.SetValue(null, elemTable);
                }
                IDictionary tagTable = fElemTagTable != null ? (fElemTagTable.GetValue(null) as IDictionary) : null;
                if (tagTable == null && fElemTagTable != null)
                {
                    tagTable = (IDictionary)Activator.CreateInstance(fElemTagTable.FieldType);
                    fElemTagTable.SetValue(null, tagTable);
                }
                elemsList.Clear();
                if (elemTable != null) elemTable.Clear();
                if (tagTable != null) tagTable.Clear();

                Type tEntry = entries[0].GetType();
                FieldInfo fEntId = tEntry.GetField("<elementId>k__BackingField", ALL);
                FieldInfo fEntDlc = tEntry.GetField("<dlcId>k__BackingField", ALL);
                FieldInfo fEntLoc = tEntry.GetField("<localizationID>k__BackingField", ALL);
                FieldInfo fEntState = tEntry.GetField("<state>k__BackingField", ALL);

                FieldInfo fElId = tElem.GetField("id", ALL);
                FieldInfo fElTag = tElem.GetField("tag", ALL);
                FieldInfo fElName = tElem.GetField("<name>k__BackingField", ALL);
                FieldInfo fElNameUpper = tElem.GetField("<nameUpperCase>k__BackingField", ALL);
                FieldInfo fElSubst = tElem.GetField("substance", ALL);

                FieldInfo fSubElemId = tSubst.GetField("elementID", ALL);
                FieldInfo fSubName = tSubst.GetField("name", ALL);
                FieldInfo fSubNameTag = tSubst.GetField("nameTag", ALL);
                FieldInfo fSubUiCol = tSubst.GetField("uiColour", ALL);
                FieldInfo fSubCol = tSubst.GetField("colour", ALL);
                ConstructorInfo cCol32 = tCol32.GetConstructor(new Type[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });

                Hashtable subTable = new Hashtable();
                Type tHash = s_asmFp.GetType("Hash", true);
                MethodInfo mSdbm = tHash.GetMethod("SDBMLower", ALL, null, new Type[] { typeof(string) }, null);

                for (int i = 0; i < entries.Count; i++)
                {
                    object ent = entries[i];
                    if (ent == null) continue;
                    string dlcId = fEntDlc != null ? (fEntDlc.GetValue(ent) as string) : null;
                    if (!string.IsNullOrEmpty(dlcId) && mIsSub != null && !(bool)mIsSub.Invoke(null, new object[] { dlcId }))
                        continue;

                    object idEnum = fEntId.GetValue(ent);
                    string idStr = idEnum != null ? idEnum.ToString() : "";
                    int idInt = (idEnum is string || !(idEnum is Enum || idEnum is IConvertible))
                        ? (int)mSdbm.Invoke(null, new object[] { idStr })
                        : Convert.ToInt32(idEnum);
                    if (elemTable != null && elemTable.Contains(idInt)) continue;

                    string locKey = fEntLoc != null ? (fEntLoc.GetValue(ent) as string) : null;
                    string disp = null;
                    if (!liveNames.TryGetValue(idInt, out disp) || string.IsNullOrEmpty(disp))
                        disp = TryGetString(locKey);
                    if (string.IsNullOrEmpty(disp))
                        disp = TryGetString("STRINGS.ELEMENTS." + idStr.ToUpperInvariant() + ".NAME");
                    if (string.IsNullOrEmpty(disp)) disp = idStr;
                    disp = CleanRichText(disp);

                    object stObj = fEntState != null ? fEntState.GetValue(ent) : null;
                    int stVal = 0;
                    if (stObj is Enum || stObj is int) stVal = Convert.ToInt32(stObj);
                    else if (stObj is string && string.Equals((string)stObj, "Gas", StringComparison.OrdinalIgnoreCase)) stVal = 0;
                    else stVal = 3;
                    uint rgb;
                    if (!liveColors.TryGetValue(idInt, out rgb))
                        rgb = GetFallbackElemRgb(idStr, stVal);
                    object col32 = cCol32.Invoke(new object[] { (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), (byte)255 });

                    object finalIdObj = Enum.ToObject(fElId.FieldType, idInt);
                    object tagObj = mTagCreate2 != null ? mTagCreate2.Invoke(null, new object[] { idStr, disp }) : null;

                    object sub = Activator.CreateInstance(tSubst);
                    if (fSubElemId != null) fSubElemId.SetValue(sub, finalIdObj);
                    if (fSubName != null) fSubName.SetValue(sub, disp);
                    if (fSubNameTag != null && tagObj != null) fSubNameTag.SetValue(sub, tagObj);
                    if (fSubUiCol != null) fSubUiCol.SetValue(sub, col32);
                    if (fSubCol != null) fSubCol.SetValue(sub, col32);

                    object el = Activator.CreateInstance(tElem);
                    fElId.SetValue(el, finalIdObj);
                    if (fElTag != null && tagObj != null) fElTag.SetValue(el, tagObj);
                    if (fElName != null) fElName.SetValue(el, disp);
                    if (fElNameUpper != null) fElNameUpper.SetValue(el, disp.ToUpperInvariant());
                    if (fElSubst != null) fElSubst.SetValue(el, sub);

                    mCopy.Invoke(null, new object[] { ent, el });

                    elemsList.Add(el);
                    if (elemTable != null) elemTable[idInt] = el;
                    if (tagTable != null && fElTag != null) tagTable[fElTag.GetValue(el)] = el;
                    subTable[finalIdObj] = sub;
                }

                if (mFinal != null)
                {
                    ParameterInfo[] fp = mFinal.GetParameters();
                    if (fp.Length == 1) mFinal.Invoke(null, new object[] { subTable });
                    else mFinal.Invoke(null, null);
                }
            }
        }

        static void InitHeadlessGeysers(string activeDlcCsv, List<string[]> liveRows)
        {
            if (s_tGeyserCfg == null || s_tGeyserType == null || s_fGcTypesList == null) return;
            Type tListGt = typeof(List<>).MakeGenericType(s_tGeyserType);
            IList gList = s_fGcTypesList.GetValue(null) as IList;
            if (gList == null)
            {
                gList = (IList)Activator.CreateInstance(tListGt);
                s_fGcTypesList.SetValue(null, gList);
            }
            else
            {
                gList.Clear();
            }

            Type tSimHashes = s_asmCs.GetType("SimHashes", true);
            Type tShape = s_asmCs.GetType("GeyserConfigurator+GeyserShape", true);
            ConstructorInfo cGt18 = s_tGeyserType.GetConstructor(ALL, null, new Type[]
            {
                typeof(string), tSimHashes, tShape, typeof(float), typeof(float), typeof(float), typeof(float),
                typeof(string[]), typeof(string[]),
                typeof(float), typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(float)
            }, null);
            if (cGt18 == null) return;

            List<GeyserPoolEntry> genericPool = new List<GeyserPoolEntry>();
            Dictionary<string, GeyserPoolEntry> allByPrefab = new Dictionary<string, GeyserPoolEntry>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, GeyserPoolEntry> allByRaw = new Dictionary<string, GeyserPoolEntry>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> activeDlcs = ParseDlcCsv(activeDlcCsv);

            if (liveRows != null && liveRows.Count > 0)
            {
                for (int i = 0; i < liveRows.Count; i++)
                {
                    string[] tp = liveRows[i];
                    string prefabId = tp[1];
                    string rawId = tp[2];
                    int elemHash = int.Parse(tp[3], CultureInfo.InvariantCulture);
                    int shapeVal = int.Parse(tp[4], CultureInfo.InvariantCulture);
                    float temp = float.Parse(tp[5], CultureInfo.InvariantCulture);
                    float minR = float.Parse(tp[6], CultureInfo.InvariantCulture);
                    float maxR = float.Parse(tp[7], CultureInfo.InvariantCulture);
                    float maxP = float.Parse(tp[8], CultureInfo.InvariantCulture);
                    float minIL = float.Parse(tp[9], CultureInfo.InvariantCulture);
                    float maxIL = float.Parse(tp[10], CultureInfo.InvariantCulture);
                    float minIP = float.Parse(tp[11], CultureInfo.InvariantCulture);
                    float maxIP = float.Parse(tp[12], CultureInfo.InvariantCulture);
                    float minYL = float.Parse(tp[13], CultureInfo.InvariantCulture);
                    float maxYL = float.Parse(tp[14], CultureInfo.InvariantCulture);
                    float minYP = float.Parse(tp[15], CultureInfo.InvariantCulture);
                    float maxYP = float.Parse(tp[16], CultureInfo.InvariantCulture);
                    float gTemp = float.Parse(tp[17], CultureInfo.InvariantCulture);
                    string[] reqDlc = string.IsNullOrEmpty(tp[18]) ? null : tp[18].Split(',');
                    string[] forbDlc = string.IsNullOrEmpty(tp[19]) ? null : tp[19].Split(',');
                    bool isGeneric = tp[20] == "1";
                    string locName = tp[21];

                    object gt = cGt18.Invoke(new object[]
                    {
                        rawId, Enum.ToObject(tSimHashes, elemHash), Enum.ToObject(tShape, shapeVal),
                        temp, minR, maxR, maxP, reqDlc, forbDlc,
                        minIL, maxIL, minIP, maxIP, minYL, maxYL, minYP, maxYP, gTemp
                    });
                    if (string.IsNullOrEmpty(locName))
                        locName = CleanRichText(TryGetString("STRINGS.CREATURES.SPECIES.GEYSER." + rawId.ToUpperInvariant() + ".NAME") ?? "");

                    RegisterHeadlessGeyserEntry(prefabId, rawId, gt, locName, reqDlc, forbDlc, isGeneric, activeDlcs,
                        gList, genericPool, allByPrefab, allByRaw);
                }
            }
            else
            {
                for (int i = 0; i < s_builtInGeysers.Length; i++)
                {
                    BuiltInGeyserRow r = s_builtInGeysers[i];
                    object elemEnum = Enum.Parse(tSimHashes, r.ElemName, true);
                    object shapeEnum = Enum.ToObject(tShape, r.Shape);
                    string[] reqDlc = string.IsNullOrEmpty(r.ReqDlc) ? null : new string[] { r.ReqDlc };
                    object gt = cGt18.Invoke(new object[]
                    {
                        r.RawId, elemEnum, shapeEnum,
                        r.Temp, r.MinR, r.MaxR, r.MaxP, reqDlc, null,
                        r.MinIL, r.MaxIL, r.MinIP, r.MaxIP, r.MinYL, r.MaxYL, r.MinYP, r.MaxYP, r.GTemp
                    });
                    string prefabId = "GeyserGeneric_" + r.RawId;
                    string locName = CleanRichText(TryGetString("STRINGS.CREATURES.SPECIES.GEYSER." + r.RawId.ToUpperInvariant() + ".NAME") ?? "");
                    RegisterHeadlessGeyserEntry(prefabId, r.RawId, gt, locName, reqDlc, null, r.IsGeneric, activeDlcs,
                        gList, genericPool, allByPrefab, allByRaw);
                }
            }

            s_geyserPoolCache[activeDlcCsv ?? ""] = new CachedGeyserPool
            {
                GenericPool = genericPool,
                AllByPrefab = allByPrefab,
                AllByRaw = allByRaw
            };
        }

        static void RegisterHeadlessGeyserEntry(
            string prefabId,
            string rawId,
            object gt,
            string locName,
            string[] reqDlc,
            string[] forbDlc,
            bool isGeneric,
            HashSet<string> activeDlcs,
            IList gList,
            List<GeyserPoolEntry> genericPool,
            Dictionary<string, GeyserPoolEntry> allByPrefab,
            Dictionary<string, GeyserPoolEntry> allByRaw)
        {
            GeyserPoolEntry entry = new GeyserPoolEntry
            {
                PrefabId = prefabId,
                RawId = rawId,
                LocalizedName = !string.IsNullOrEmpty(locName) ? locName : null,
                GeyserTypeObj = gt
            };
            allByPrefab[prefabId] = entry;
            allByRaw[rawId] = entry;


            bool ok = true;
            if (activeDlcs != null && activeDlcs.Count > 0)
            {
                if (reqDlc != null)
                    for (int k = 0; k < reqDlc.Length; k++)
                        if (!string.IsNullOrEmpty(reqDlc[k]) && !activeDlcs.Contains(reqDlc[k])) { ok = false; break; }
                if (ok && forbDlc != null)
                    for (int k = 0; k < forbDlc.Length; k++)
                        if (!string.IsNullOrEmpty(forbDlc[k]) && activeDlcs.Contains(forbDlc[k])) { ok = false; break; }
            }
            if (!ok) return;

            gList.Add(gt);
            if (isGeneric) genericPool.Add(entry);
        }

        public static int RunHeadlessWorker(string gameDir)
        {
            try
            {
                s_isHeadlessWorker = true;
                s_headlessGameDir = gameDir ?? "";
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;

                string managedDir = Path.Combine(s_headlessGameDir, "OxygenNotIncluded_Data", "Managed");
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    try
                    {
                        string name = new AssemblyName(e.Name).Name + ".dll";
                        string cand = Path.Combine(managedDir, name);
                        if (File.Exists(cand)) return Assembly.LoadFrom(cand);
                    }
                    catch { }
                    return null;
                };

                s_asmCore = Assembly.LoadFrom(Path.Combine(managedDir, "UnityEngine.CoreModule.dll"));
                s_asmFp = Assembly.LoadFrom(Path.Combine(managedDir, "Assembly-CSharp-firstpass.dll"));
                s_asmCs = Assembly.LoadFrom(Path.Combine(managedDir, "Assembly-CSharp.dll"));

                string initErr;
                if (!InitReflection(out initErr))
                {
                    Console.Error.WriteLine("ERR reflect:" + initErr);
                    return 10;
                }

                if (s_fKcrIgnoreAll != null && s_fKcrIgnoreAll.IsStatic)
                    try { s_fKcrIgnoreAll.SetValue(null, true); } catch { }
                if (s_fDebugLoggingDisabled != null && s_fDebugLoggingDisabled.IsStatic)
                    try { s_fDebugLoggingDisabled.SetValue(null, true); } catch { }

                Console.WriteLine("READY");
                Console.Out.Flush();

                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (string.Equals(line, "QUIT", StringComparison.OrdinalIgnoreCase))
                        break;

                    string resp;
                    try
                    {
                        if (line.StartsWith("INIT ", StringComparison.OrdinalIgnoreCase))
                        {
                            string rest = line.Substring(5);
                            int bar = rest.IndexOf('|');
                            string dlcCsv = bar >= 0 ? rest.Substring(0, bar).Trim() : rest.Trim();
                            string catPath = bar >= 0 ? rest.Substring(bar + 1).Trim() : "";
                            string iErr;
                            resp = InitHeadlessWorldGen(s_headlessGameDir, dlcCsv, catPath, out iErr)
                                ? "OK init"
                                : ("ERR init:" + iErr);
                        }
                        else if (line.StartsWith("BATCH ", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] ps = line.Substring(6).Split('|');
                            if (ps.Length < 6) resp = "ERR bad-batch-args";
                            else
                            {
                                string recOut = ps.Length > 6 ? ps[6].Trim() : "";
                                resp = RunBatchPoi(ps[0].Trim(), ps[1].Trim(), ps[2].Trim(), ps[3].Trim(), "", ps[4].Trim(), ps[5].Trim(), recOut);
                            }
                        }
                        else if (line.StartsWith("FAST ", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] ps = line.Substring(5).Split('|');
                            if (ps.Length < 6) resp = "ERR bad-fast-args";
                            else resp = GeneratePreview(ps[0].Trim() + "|" + ps[1].Trim() + "|" + ps[2].Trim() + "|" + ps[3].Trim() + "|" + ps[4].Trim() + "|" + ps[5].Trim() + "||FASTPOI");
                        }
                        else if (line.StartsWith("PREVIEW ", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] ps = line.Substring(8).Split('|');
                            if (ps.Length < 6) resp = "ERR bad-prev-args";
                            else resp = GeneratePreview(ps[0].Trim() + "|" + ps[1].Trim() + "|" + ps[2].Trim() + "|" + ps[3].Trim() + "|" + ps[4].Trim() + "|" + ps[5].Trim() + "|");
                        }
                        else
                        {
                            resp = "ERR unknown-worker-cmd";
                        }
                    }
                    catch (Exception ex)
                    {
                        Exception inner = ex;
                        while (inner.InnerException != null) inner = inner.InnerException;
                        resp = "ERR worker-exc:" + SanitizeSingleLine(inner.Message);
                    }

                    Console.WriteLine(resp);
                    Console.Out.Flush();
                }

                return 0;
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                Console.Error.WriteLine("FATAL: " + inner.GetType().Name + ": " + inner.Message);
                return 11;
            }
        }
    }
}
