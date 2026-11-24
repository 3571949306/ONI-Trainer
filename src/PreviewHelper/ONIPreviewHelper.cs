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

        static Type s_tWorld;
        static FieldInfo s_fWorldInst;
        static Type s_tGrid;
        static FieldInfo s_fGridW, s_fGridH, s_fGridC, s_fGridWm, s_fGridHm;
        static FieldInfo s_fGridElemIdx, s_fGridMass, s_fGridTemp, s_fGridDisIdx, s_fGridDisCnt;
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
        static FieldInfo s_fWgVoidElem, s_fWgIsStarting;
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
        static FieldInfo s_fGtId, s_fGtIdHash, s_fGtMinRate, s_fGtMaxRate, s_fGtMinIterPct, s_fGtMaxIterPct, s_fGtMinYearPct, s_fGtMaxYearPct;
        static Type s_tCgs;
        static FieldInfo s_fCgsInstance;
        static MethodInfo s_mCgsGetStoryCode, s_mCgsParseStoryCode, s_mCgsGetMixCode, s_mCgsParseMixCode, s_mCgsGetStories, s_mCgsGetActiveWorldMix;
        static Type s_tWorldgenMixing, s_tMutatedLayout;
        static MethodInfo s_mDoWorldMixing;
        static Type s_tAssets, s_tTag, s_tHashedString;
        static MethodInfo s_mAssetsGetPrefab;
        static ConstructorInfo s_cTagStr;


        static bool s_inSaveResolved;
        static Type s_tComponents, s_tStudyable, s_tModifiers, s_tAmounts, s_tAttrLevels, s_tTraits;
        static Type s_tAttrMod, s_tPopFX, s_tPlayerCtrl, s_tSimMessages, s_tSaveLoader, s_tUnityObj, s_tSimHashes;
        static FieldInfo s_fCompLiveMinions, s_fCompGeysers, s_fGeyserStudyable;
        static FieldInfo s_fStudyStudied, s_fStudyMarked, s_fModAmounts, s_fModAttrs, s_fTraitList, s_fTraitPositive, s_fAttrModDesc;
        static FieldInfo s_fPopInst, s_fPopReady, s_fPopSpritePlus, s_fPcInst;
        static PropertyInfo s_pSaveLoaderInst;
        static MethodInfo s_mSaveGetPath, s_mStudyCancel, s_mStudyRefresh, s_mStudyTrigger;
        static MethodInfo s_mGetComponent, s_mAmountsGet, s_mAmtGetMax, s_mAmtSetVal;
        static MethodInfo s_mAttrsGet, s_mAttrInstAdd, s_mAttrInstRemove, s_mAttrLevelGet, s_mAttrLevelSet;
        static FieldInfo s_fAttrInstMods, s_fAttrLevelVal;
        static MethodInfo s_mTraitsRemove, s_mTraitGetName, s_mPcGetCursor, s_mPopSpawnFx, s_mSimReplaceElem, s_mUnityFindObjects;
        static ConstructorInfo s_cAttrMod6;
        static object s_simVacuumEnum;
        static Type s_tVec3;

        static string s_lastGenError;
        static Delegate s_progressCb;
        static Delegate s_errorCb;
        static Action<Array, ushort[], short[]> s_extractCells;

        static readonly float[] s_quantileSteps = new float[]
        {
            0.05f, 0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 0.95f
        };

        static readonly string[] s_dupeAttrIds = new string[]
        {
            "Construction", "Digging", "Machinery", "Athletics", "Learning",
            "Cooking", "Caring", "Strength", "Art", "Botanist", "Ranching"
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

                s_tSimData = s_asmCs.GetType("ProcGenGame.WorldgenSimData", true);

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
                        s_fGtMinIterPct = s_tGeyserType.GetField("minIterationPercent", ALL);
                        s_fGtMaxIterPct = s_tGeyserType.GetField("maxIterationPercent", ALL);
                        s_fGtMinYearPct = s_tGeyserType.GetField("minYearPercent", ALL);
                        s_fGtMaxYearPct = s_tGeyserType.GetField("maxYearPercent", ALL);
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
                        string storyCode = s_mCgsGetStoryCode != null ? (s_mCgsGetStoryCode.Invoke(cgs, null) as string ?? "") : "";
                        string mixingCode = s_mCgsGetMixCode != null ? (s_mCgsGetMixCode.Invoke(cgs, null) as string ?? "") : "";
                        List<string> stories = new List<string>();
                        if (s_mCgsGetStories != null)
                        {
                            IEnumerable st = s_mCgsGetStories.Invoke(cgs, null) as IEnumerable;
                            if (st != null)
                            {
                                foreach (object o in st)
                                {
                                    string id = o as string;
                                    if (!string.IsNullOrEmpty(id)) stories.Add(id);
                                }
                            }
                        }
                        return "OK storyCode=" + storyCode + "|mixingCode=" + mixingCode + "|stories=" + string.Join(",", stories.ToArray());
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

                    if (s_fWorldInst != null && s_fWorldInst.GetValue(null) != null)
                        return "ERR in-save";

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

                    try
                    {

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

                        s_mGridReset.Invoke(null, new object[] { sizeX, sizeY });

                        bool okGen = (bool)s_mWgGenOffline.Invoke(wg, null);
                        if (!okGen)
                            return "ERR gen-offline:" + (s_lastGenError ?? "unknown");

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
                            return "ERR render-offline:" + (s_lastGenError ?? "unknown");

                        Array simCells = s_tSimData.GetField("cells", ALL).GetValue(simData) as Array;
                        if (simCells == null || simCells.Length < sizeX * sizeY)
                            return "ERR bad-simcells";

                        int totalCells = sizeX * sizeY;
                        ushort[] outElem = new ushort[totalCells];
                        short[] outTemp = new short[totalCells];
                        s_extractCells(simCells, outElem, outTemp);

                        object wgData = s_pWgData.GetValue(wg, null);
                        byte[] zoneGrid = RasterizeBiomes(wg, wgData, sizeX, sizeY, wpX, wpY);

                        HashSet<string> activeDlcs = ParseDlcCsv(activeDlcCsv);
                        int baseStartX, baseStartY;
                        List<PoiOut> pois = ExtractPois(wgData, seed, wpX, wpY, sizeX, sizeY, activeDlcs, out baseStartX, out baseStartY);

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

        static List<string> BuildStoryTraitList(string storyArg)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(storyArg) && !storyArg.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = storyArg.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = parts[i].Trim();
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

            IList ovCells = wgData.GetType().GetField("overworldCells", ALL).GetValue(wgData) as IList;
            if (ovCells == null || ovCells.Count == 0) return grid;

            Type tTc = ovCells[0].GetType();
            PropertyInfo pPoly = tTc.GetProperty("poly", ALL);
            PropertyInfo pNode = tTc.GetProperty("node", ALL);
            PropertyInfo pNodeType = null;
            object wgSettings = s_pWgSettings != null ? s_pWgSettings.GetValue(wg, null) : null;
            MethodInfo mHasSubworld = wgSettings != null ? wgSettings.GetType().GetMethod("HasSubworld", ALL, null, new Type[] { typeof(string) }, null) : null;
            MethodInfo mGetSubWorld = wgSettings != null ? wgSettings.GetType().GetMethod("GetSubWorld", ALL, null, new Type[] { typeof(string) }, null) : null;
            PropertyInfo pZoneType = null;

            Type tPoly = s_asmFp.GetType("Delaunay.Geo.Polygon", true);
            FieldInfo fVerts = tPoly.GetField("vertices", ALL);
            PropertyInfo pBounds = tPoly.GetProperty("bounds", ALL);

            Type tRect = s_asmCore.GetType("UnityEngine.Rect", true);
            PropertyInfo pXMax = tRect.GetProperty("xMax", ALL);
            PropertyInfo pYMax = tRect.GetProperty("yMax", ALL);

            Type tVec2 = s_asmCore.GetType("UnityEngine.Vector2", true);
            FieldInfo fV2x = tVec2.GetField("x", ALL);
            FieldInfo fV2y = tVec2.GetField("y", ALL);

            float maxPolyX = 0f, maxPolyY = 0f;
            for (int i = 0; i < ovCells.Count; i++)
            {
                object poly = pPoly != null ? pPoly.GetValue(ovCells[i], null) : null;
                if (poly == null) continue;
                object rect = pBounds.GetValue(poly, null);
                float xmax = (float)pXMax.GetValue(rect, null);
                float ymax = (float)pYMax.GetValue(rect, null);
                if (xmax > maxPolyX) maxPolyX = xmax;
                if (ymax > maxPolyY) maxPolyY = ymax;
            }
            float offX = (wpX > 0 && maxPolyX > w + 4f) ? (float)wpX : 0f;
            float offY = (wpY > 0 && maxPolyY > h + 4f) ? (float)wpY : 0f;

            for (int c = 0; c < ovCells.Count; c++)
            {
                object ov = ovCells[c];
                byte zone = 255;
                if (wgSettings != null && mGetSubWorld != null && pNode != null)
                {
                    object nodeObj = pNode.GetValue(ov, null);
                    if (nodeObj != null)
                    {
                        if (pNodeType == null) pNodeType = nodeObj.GetType().GetProperty("type", ALL);
                        string subName = pNodeType != null ? (pNodeType.GetValue(nodeObj, null) as string) : null;
                        if (!string.IsNullOrEmpty(subName) &&
                            (mHasSubworld == null || (bool)mHasSubworld.Invoke(wgSettings, new object[] { subName })))
                        {
                            object subWorld = mGetSubWorld.Invoke(wgSettings, new object[] { subName });
                            if (subWorld != null)
                            {
                                if (pZoneType == null) pZoneType = subWorld.GetType().GetProperty("zoneType", ALL);
                                if (pZoneType != null) zone = Convert.ToByte(pZoneType.GetValue(subWorld, null));
                            }
                        }
                    }
                }
                object poly = pPoly != null ? pPoly.GetValue(ov, null) : null;
                if (poly == null) continue;

                IList verts = fVerts.GetValue(poly) as IList;
                if (verts == null || verts.Count < 3) continue;

                int vn = verts.Count;
                float[] vx = new float[vn];
                float[] vy = new float[vn];
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int v = 0; v < vn; v++)
                {
                    object pt = verts[v];
                    float px = (float)fV2x.GetValue(pt) - offX;
                    float py = (float)fV2y.GetValue(pt) - offY;
                    vx[v] = px;
                    vy[v] = py;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }

                int x0 = Math.Max(0, (int)Math.Floor(minX));
                int x1 = Math.Min(w - 1, (int)Math.Ceiling(maxX));
                int y0 = Math.Max(0, (int)Math.Floor(minY));
                int y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY));

                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    int rowOff = y * w;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        bool inside = false;
                        for (int i = 0, j = vn - 1; i < vn; j = i++)
                        {
                            float yi = vy[i], yj = vy[j];
                            if (((yi > py) != (yj > py)) &&
                                (px < (vx[j] - vx[i]) * (py - yi) / (yj - yi) + vx[i]))
                                inside = !inside;
                        }
                        if (inside) grid[rowOff + x] = zone;
                    }
                }
            }
            return grid;
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


            GeyserPoolEntry e;
            if (allByPrefab.TryGetValue(prefabId, out e) && e.GeyserTypeObj != null)
            {
                geyserTypeObj = e.GeyserTypeObj;
                typeIdHash = s_fGtIdHash.GetValue(geyserTypeObj);
                return true;
            }
            string guessRaw = null;
            if (prefabId.StartsWith("GeyserGeneric_", StringComparison.Ordinal)) guessRaw = prefabId.Substring("GeyserGeneric_".Length);
            else if (string.Equals(prefabId, "Geyser", StringComparison.Ordinal)) guessRaw = "steam";
            else if (string.Equals(prefabId, "MethaneGeyser", StringComparison.Ordinal)) guessRaw = "methane";
            else if (string.Equals(prefabId, "ChlorineGeyser", StringComparison.Ordinal)) guessRaw = "chlorine";
            else if (string.Equals(prefabId, "SmallReefGeyser", StringComparison.Ordinal)) guessRaw = "salt_water";
            else if (string.Equals(prefabId, "NiobiumGeyser", StringComparison.Ordinal)) guessRaw = "molten_niobium";

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
                float minIp = (float)s_fGtMinIterPct.GetValue(gt);
                float maxIp = (float)s_fGtMaxIterPct.GetValue(gt);
                float minYp = (float)s_fGtMinYearPct.GetValue(gt);
                float maxYp = (float)s_fGtMaxYearPct.GetValue(gt);

                float loR = minRate + (maxRate - minRate) * pMin;
                float hiR = minRate + (maxRate - minRate) * pMax;
                float loIp = minIp + (maxIp - minIp) * pMin;
                float hiIp = minIp + (maxIp - minIp) * pMax;
                float loYp = minYp + (maxYp - minYp) * pMin;
                float hiYp = minYp + (maxYp - minYp) * pMax;

                if (Math.Abs(hiR - loR) < 1e-4f && Math.Abs(hiYp - loYp) < 1e-4f)
                    return 50f;

                int below = 0;
                int n = s_quantileSteps.Length;
                for (int i = 0; i < n; i++)
                {
                    float r = loR + (hiR - loR) * s_quantileSteps[i];
                    for (int j = 0; j < n; j++)
                    {
                        float yp = loYp + (hiYp - loYp) * s_quantileSteps[j];
                        for (int k = 0; k < n; k++)
                        {

                            float ip = loIp + (hiIp - loIp) * s_quantileSteps[k];
                            float sampleAvg = (ip >= 0f ? (r / 600f) * yp : 0f);
                            if (sampleAvg <= avgKgS + 1e-6f) below++;
                        }
                    }
                }
                return (below * 100f) / (n * n * n);
            }
            catch
            {
                return 50f;
            }
        }

        static List<PoiOut> ExtractPois(
            object wgData, int seed, int wpX, int wpY, int w, int h,
            HashSet<string> activeDlcs, out int baseStartX, out int baseStartY)
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

            List<GeyserPoolEntry> geyserPool;
            Dictionary<string, GeyserPoolEntry> allByPrefab;
            Dictionary<string, GeyserPoolEntry> allByRaw;
            BuildGeyserPools(activeDlcs, out geyserPool, out allByPrefab, out allByRaw);

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
            if (string.IsNullOrEmpty(key) || s_mStringsTryGet == null || s_fStringEntryVal == null) return null;
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

            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmpPath, outPath);
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
                    s_mTraitGetName = tTrait.GetMethod("GetName", ALL);
                    s_fTraitPositive = tTrait.GetField("PositiveTrait", ALL);
                }

                Type tComp = s_asmCore.GetType("UnityEngine.Component", true);
                s_mGetComponent = tComp.GetMethod("GetComponent", ALL, null, new Type[] { typeof(Type) }, null);
                s_tUnityObj = s_asmCore.GetType("UnityEngine.Object", true);
                s_mUnityFindObjects = s_tUnityObj.GetMethod("FindObjectsOfType", ALL, null, new Type[] { typeof(Type) }, null);

                s_tVec3 = s_asmCore.GetType("UnityEngine.Vector3", true);

                s_tGrid = s_asmCs.GetType("Grid", true);
                s_fGridW = s_tGrid.GetField("WidthInCells", ALL);
                s_fGridC = s_tGrid.GetField("CellCount", ALL);
                s_fGridElemIdx = s_tGrid.GetField("elementIdx", ALL);
                s_fGridMass = s_tGrid.GetField("mass", ALL);
                s_fGridTemp = s_tGrid.GetField("temperature", ALL);
                s_fGridDisIdx = s_tGrid.GetField("diseaseIdx", ALL);
                s_fGridDisCnt = s_tGrid.GetField("diseaseCount", ALL);
                s_mGridIsValidCell = s_tGrid.GetMethod("IsValidCell", ALL, null, new Type[] { typeof(int) }, null);
                s_mGridPosToCell = s_tGrid.GetMethod("PosToCell", ALL, null, new Type[] { s_tVec3 }, null);

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
                if (string.Equals(verb, "WORKSPEED", StringComparison.OrdinalIgnoreCase))
                    return CmdWorkSpeed(arg);
                if (string.Equals(verb, "SURVIVAL_NOW", StringComparison.OrdinalIgnoreCase))
                {
                    TickMainThread(31);
                    return "OK survival_now";
                }
                if (string.Equals(verb, "DUPELIST", StringComparison.OrdinalIgnoreCase))
                    return CmdDupeList();
                if (string.Equals(verb, "SETATTR", StringComparison.OrdinalIgnoreCase))
                    return CmdSetAttr(arg);
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
            HashSet<object> list = new HashSet<object>();
            if (s_fCompGeysers != null && s_fGeyserStudyable != null)
            {
                object gComp = s_fCompGeysers.GetValue(null);
                if (gComp != null)
                {
                    IList geysers = gComp.GetType().GetProperty("Items", ALL).GetValue(gComp, null) as IList;
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
                total++;
                bool already = (bool)s_fStudyStudied.GetValue(st);
                if (already) continue;
                s_fStudyStudied.SetValue(st, true);
                if (s_fStudyMarked != null) s_fStudyMarked.SetValue(st, false);
                if (s_mStudyCancel != null) { try { s_mStudyCancel.Invoke(st, null); } catch { } }
                if (s_mStudyRefresh != null) { try { s_mStudyRefresh.Invoke(st, null); } catch { } }
                if (s_mStudyTrigger != null) { try { s_mStudyTrigger.Invoke(st, new object[] { -1436775550, null }); } catch { } }
                studied++;
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
                            tname = CleanRichText(tname).Replace('|', '/').Replace(';', ' ').Replace(':', ' ');
                            if (trSb.Length > 0) trSb.Append(';');
                            trSb.Append(tid).Append(':').Append(tname).Append(':').Append(pos ? "1" : "0");
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

        static bool TryGetCursorCell(out int cell)
        {
            cell = -1;
            if (s_fPcInst == null || s_mPcGetCursor == null || s_mGridPosToCell == null) return false;
            object pc = s_fPcInst.GetValue(null);
            if (pc == null) return false;
            object pos = s_mPcGetCursor.Invoke(pc, null);
            cell = (int)s_mGridPosToCell.Invoke(null, new object[] { pos });
            return (bool)s_mGridIsValidCell.Invoke(null, new object[] { cell });
        }

        static string CmdCellTemp(string arg)
        {
            float deltaC;
            if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out deltaC))
                return "ERR bad-delta";
            int cell;
            if (!TryGetCursorCell(out cell)) return "ERR bad-cell";
            if (s_mSimReplaceElem == null) return "ERR no-sim";

            ushort[] eidxArr = s_fGridElemIdx.GetValue(null) as ushort[];
            float[] massArr = s_fGridMass.GetValue(null) as float[];
            float[] tempArr = s_fGridTemp.GetValue(null) as float[];
            byte[] dIdxArr = s_fGridDisIdx.GetValue(null) as byte[];
            int[] dCntArr = s_fGridDisCnt.GetValue(null) as int[];
            IList elems = s_fElements.GetValue(null) as IList;
            if (eidxArr == null || massArr == null || tempArr == null || elems == null) return "ERR grid";

            ushort idx = eidxArr[cell];
            if (idx >= elems.Count) return "ERR elem-idx";
            object elem = elems[idx];
            Type te = elem.GetType();
            int st = Convert.ToInt32(te.GetField("state", ALL).GetValue(elem)) & 3;
            if (st == 3) return "ERR solid";
            float mass = massArr[cell];
            if (st == 0 || mass <= 0f) return "ERR vacuum";

            object elemIdEnum = te.GetField("id", ALL).GetValue(elem);
            float curK = tempArr[cell];
            float newK = curK + deltaC;
            if (newK < 1.5f) newK = 1.5f;
            if (newK > 9999f) newK = 9999f;
            byte di = dIdxArr != null ? dIdxArr[cell] : (byte)255;
            int dc = dCntArr != null ? dCntArr[cell] : 0;

            s_mSimReplaceElem.Invoke(null, new object[] { cell, elemIdEnum, null, mass, newK, di, dc, -1 });
            return "OK celltemp:" + (newK - 273.15f).ToString("0.#", CultureInfo.InvariantCulture);
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

            int gridW = (int)s_fGridW.GetValue(null);
            int cellCount = (int)s_fGridC.GetValue(null);
            ushort[] eidxArr = s_fGridElemIdx.GetValue(null) as ushort[];
            IList elems = s_fElements.GetValue(null) as IList;
            if (gridW <= 0 || eidxArr == null || elems == null) return "ERR grid";

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
                    ushort idx = eidxArr[c];
                    if (idx >= elems.Count) continue;
                    int st = Convert.ToInt32(fState.GetValue(elems[idx])) & 3;
                    if (st == 3) { skippedSolid++; continue; }
                    s_mSimReplaceElem.Invoke(null, new object[] { c, s_simVacuumEnum, null, 0f, 0f, (byte)255, 0, -1 });
                    cleared++;
                }
            }
            if (cleared == 0 && skippedSolid > 0) return "ERR solid";
            return "OK vac:" + cleared;
        }

        static string CmdPopFx(string text)
        {
            if (string.IsNullOrEmpty(text) || s_tPopFX == null || s_mPopSpawnFx == null) return "ERR no-popfx";
            object inst = s_fPopInst != null ? s_fPopInst.GetValue(null) : null;
            if (inst == null) return "ERR no-popfx-inst";
            if (s_fPopReady != null && !(bool)s_fPopReady.GetValue(inst)) return "ERR not-ready";
            object sprite = s_fPopSpritePlus != null ? s_fPopSpritePlus.GetValue(inst) : null;

            object offset = Activator.CreateInstance(s_tVec3);
            if (s_fPcInst != null && s_mPcGetCursor != null)
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
    }
}
