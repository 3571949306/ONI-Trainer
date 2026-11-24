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
        static readonly object s_lock = new object();

        static Assembly s_asmCs;
        static Assembly s_asmFp;
        static Assembly s_asmCore;

        static Type s_tWorld;
        static FieldInfo s_fWorldInst;
        static Type s_tGrid;
        static FieldInfo s_fGridW, s_fGridH, s_fGridC, s_fGridWm, s_fGridHm;
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
        static PropertyInfo s_pWgData;
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
        static MethodInfo s_mKRandomNext2;

        static string s_lastGenError;
        static Delegate s_progressCb;
        static Delegate s_errorCb;
        static Action<Array, ushort[], short[]> s_extractCells;

        static void LogLastError(object errInfoObj)
        {
            if (errInfoObj == null) return;
            try
            {
                Type t = errInfoObj.GetType();
                FieldInfo fd = t.GetField("errorDesc", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fe = t.GetField("exception", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string desc = fd != null ? (fd.GetValue(errInfoObj) as string) : null;
                Exception ex = fe != null ? (fe.GetValue(errInfoObj) as Exception) : null;
                if (!string.IsNullOrEmpty(desc)) s_lastGenError = desc;
                else if (ex != null) s_lastGenError = ex.Message;
            }
            catch { }
        }

        static bool InitReflection(out string err)
        {
            err = null;
            if (s_tWorld != null && s_mWgRenderOffline != null && s_extractCells != null)
                return true;

            try
            {
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

                const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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

        struct SavedPlacementState
        {
            public int X, Y, W, H;
            public bool StartWorld;
        }

        sealed class GeyserPoolEntry
        {
            public string PrefabId;
            public string RawId;
            public string LocalizedName;
        }

        sealed class PoiOut
        {
            public string PrefabId;
            public string Category;
            public string DisplayName;
            public int X;
            public int Y;
            public byte IsRandomGeyser;
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
            string storyCsv = parts[4].Trim();
            string outPath = parts[5].Trim();
            if (string.IsNullOrEmpty(outPath)) return "ERR bad-outpath";

            lock (s_lock)
            {
                CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
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

                    const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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
                    MethodInfo mWpSetSize = tWp.GetMethod("SetSize", ALL);

                    for (int i = 0; i < placements.Count; i++)
                    {
                        object wp = placements[i];
                        savedPlacements[i] = new SavedPlacementState
                        {
                            X = (int)fWpX.GetValue(wp),
                            Y = (int)fWpY.GetValue(wp),
                            W = (int)fWpW.GetValue(wp),
                            H = (int)fWpH.GetValue(wp),
                            StartWorld = (bool)fWpStart.GetValue(wp)
                        };
                    }

                    try
                    {
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
                        List<string> storyTraits = BuildStoryTraitList(storyCsv);

                        object wg = s_cWg4.Invoke(new object[] { targetWorldId, chosenTraits, storyTraits, false });
                        s_mWgSetSize.Invoke(wg, new object[] { sizeX, sizeY });
                        object posObj = s_cVector2I.Invoke(new object[] { wpX, wpY });
                        s_mWgSetPos.Invoke(wg, new object[] { posObj });
                        s_mWgSetHiddenY.Invoke(wg, new object[] { hiddenY });
                        bool isStart = (worldIndex == startWorldIndex);
                        if (s_fWgIsStarting != null) s_fWgIsStarting.SetValue(wg, isStart);

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
                        for (int i = 0; i < placements.Count; i++)
                        {
                            object wp = placements[i];
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
                    Thread.CurrentThread.CurrentCulture = prevCulture;
                }
            }
        }

        static object FindClusterFallback(object clusterLayoutsObj, string target)
        {
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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

        static List<string> BuildStoryTraitList(string storyCsv)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(storyCsv))
            {
                string[] parts = storyCsv.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = parts[i].Trim();
                    if (s.Length > 0) list.Add(s);
                }
                return list;
            }

            try
            {
                Type tCgs = s_asmCs.GetType("CustomGameSettings", false);
                Type tDb = s_asmCs.GetType("Db", false);
                if (tCgs != null && tDb != null)
                {
                    const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    MethodInfo mInst = tCgs.GetMethod("get_Instance", ALL);
                    object cgs = mInst != null ? mInst.Invoke(null, null) : null;
                    MethodInfo mDbGet = tDb.GetMethod("Get", ALL);
                    object db = mDbGet != null ? mDbGet.Invoke(null, null) : null;
                    if (cgs != null && db != null)
                    {
                        MethodInfo mCur = tCgs.GetMethod("GetCurrentStories", ALL);
                        IEnumerable stories = mCur != null ? (mCur.Invoke(cgs, null) as IEnumerable) : null;
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
                const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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

            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            IList ovCells = wgData.GetType().GetField("overworldCells", ALL).GetValue(wgData) as IList;
            if (ovCells == null || ovCells.Count == 0) return grid;

            Type tTc = ovCells[0].GetType();
            PropertyInfo pPoly = tTc.GetProperty("poly", ALL);
            PropertyInfo pNode = tTc.GetProperty("node", ALL);
            PropertyInfo pNodeType = null;
            PropertyInfo pWgSettings = wg != null ? wg.GetType().GetProperty("Settings", ALL) : null;
            object wgSettings = pWgSettings != null ? pWgSettings.GetValue(wg, null) : null;
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

        static List<GeyserPoolEntry> BuildActiveGeyserPool(HashSet<string> activeDlcs)
        {
            List<GeyserPoolEntry> pool = new List<GeyserPoolEntry>();
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            object gcfg = Activator.CreateInstance(s_tGeyserGenericConfig);
            IList configs = s_mGeyserGenConfigs.Invoke(gcfg, null) as IList;
            if (configs == null) return pool;

            for (int i = 0; i < configs.Count; i++)
            {
                object p = configs[i];
                Type tp = p.GetType();
                bool isGeneric = (bool)tp.GetField("isGenericGeyser", ALL).GetValue(p);
                if (!isGeneric) continue;

                object gt = tp.GetField("geyserType", ALL).GetValue(p);
                Type tgt = gt.GetType();
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

                string prefabId = tp.GetField("id", ALL).GetValue(p) as string;
                string rawId = tgt.GetField("id", ALL).GetValue(gt) as string;
                object skObj = tp.GetField("nameStringKey", ALL).GetValue(p);
                string nameKey = skObj != null ? (skObj.GetType().GetField("String", ALL).GetValue(skObj) as string) : null;
                string locName = TryGetString(nameKey);

                pool.Add(new GeyserPoolEntry
                {
                    PrefabId = prefabId ?? "",
                    RawId = rawId ?? "",
                    LocalizedName = !string.IsNullOrEmpty(locName) ? CleanRichText(locName) : null
                });
            }
            return pool;
        }

        static List<PoiOut> ExtractPois(
            object wgData, int seed, int wpX, int wpY, int w, int h,
            HashSet<string> activeDlcs, out int baseStartX, out int baseStartY)
        {
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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

            List<GeyserPoolEntry> geyserPool = BuildActiveGeyserPool(activeDlcs);
            Dictionary<string, string> geyserNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < geyserPool.Count; i++)
                if (!string.IsNullOrEmpty(geyserPool[i].LocalizedName))
                    geyserNameMap[geyserPool[i].PrefabId] = geyserPool[i].LocalizedName;

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
                    if (string.Equals(id, "GeyserGeneric", StringComparison.Ordinal))
                    {
                        if (geyserPool.Count > 0)
                        {
                            int gSeed = unchecked(seed + (wpX + lx) + (wpY + ly));
                            object krand = s_cKRandomInt.Invoke(new object[] { gSeed });
                            int pick = (int)s_mKRandomNext2.Invoke(krand, new object[] { 0, geyserPool.Count });
                            if (pick >= 0 && pick < geyserPool.Count)
                                id = geyserPool[pick].PrefabId;
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
                    res.Add(new PoiOut
                    {
                        PrefabId = id,
                        Category = cat,
                        DisplayName = disp,
                        X = lx,
                        Y = ly,
                        IsRandomGeyser = isRandom
                    });
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
                    id.IndexOf("po2", StringComparison.Ordinal) >= 0)
                    return "gas_geyser";
                if (id.IndexOf("liquid_co2", StringComparison.Ordinal) >= 0)
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
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
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
                bw.Write(1);
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
                }
            }

            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmpPath, outPath);
        }
    }
}
