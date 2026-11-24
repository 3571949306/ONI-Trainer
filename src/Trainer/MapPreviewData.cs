using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ONITrainer
{
    public sealed class BridgePreviewState
    {
        public Bridge Bridge;
        public bool InSave;
    }

    public enum MapRenderMode
    {
        BiomeTerrain = 0,
        ElementOnly = 1,
        Temperature = 2
    }

    public class MapElementDef
    {
        public byte R;
        public byte G;
        public byte B;
        public byte State;
        public string Name;
    }

    public class MapPoiItem
    {
        public string PrefabId;
        public string Category;
        public string DisplayName;
        public int X;
        public int Y;
        public bool IsRandomGeyser;
        public int DistFromStart;


        public bool HasGeyserStats;
        public float AvgYieldGps;
        public float EruptRateGps;
        public float OutputTempC;
        public float EruptSec;
        public float IdleSec;
        public float ActiveCycles;
        public float DormantCycles;
        public byte Grade;
        public float Percentile;

        public string GradeText()
        {
            if (Grade == 1) return "S";
            if (Grade == 2) return "A";
            if (Grade == 3) return "B";
            if (Grade == 4) return "C";
            return "";
        }

        public string FormatYieldShort()
        {
            if (!HasGeyserStats) return "";
            if (AvgYieldGps >= 1000f)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##}kg/s", AvgYieldGps / 1000f);
            return string.Format(CultureInfo.InvariantCulture, "{0:0}g/s", AvgYieldGps);
        }

        public string FormatDetailText()
        {
            if (!HasGeyserStats)
            {
                return string.Format("{0} ({1})\r\n坐标 ({2}, {3})  ·  距打印舱 {4} 格",
                    DisplayName, PrefabId, X, Y, DistFromStart);
            }
            string g = GradeText();
            return string.Format(CultureInfo.InvariantCulture,
                "{0}{1}  |  均产 {2:0.##} g/s · 喷发 {3:0.##} g/s · {4:0.#}℃\r\n" +
                "喷发 {5:0.#}s / 间歇 {6:0.#}s  |  活跃 {7:0.#}周期 / 休眠 {8:0.#}周期  |  距起点 {9}格",
                DisplayName,
                string.IsNullOrEmpty(g) ? "" : (" [" + g + "级 " + Percentile.ToString("0") + "%]"),
                AvgYieldGps, EruptRateGps, OutputTempC,
                EruptSec, IdleSec, ActiveCycles, DormantCycles, DistFromStart);
        }
    }

    public class PoiFilterCatalogEntry
    {
        public string PrefabId;
        public string Category;
        public string DisplayName;

        public override string ToString()
        {
            return "[" + CategoryLabel(Category) + "] " + DisplayName;
        }

        public static string CategoryLabel(string cat)
        {
            if (cat == "metal_volcano") return "金属火山";
            if (cat == "volcano") return "火山";
            if (cat == "water_geyser") return "水源/液泉";
            if (cat == "gas_geyser") return "气体喷口";
            if (cat == "oil") return "油井";
            if (cat == "story") return "故事特质";
            if (cat == "poi") return "遗迹/传送";
            return "起点";
        }
    }

    public class PoiFilterRule
    {
        public string PrefabId;
        public string Category;
        public string DisplayName;
        public int MinCount;
        public bool CompareAtMost;
        public int MaxDist;
        public float MinYieldGps;
        public byte MinGrade;

        public override string ToString()
        {
            string op = CompareAtMost ? "≤" : "≥";
            string s = string.Format("[{0}] {1}  {2} {3}",
                PoiFilterCatalogEntry.CategoryLabel(Category), DisplayName, op, MinCount);
            if (MaxDist > 0) s += string.Format("  距≤{0}", MaxDist);
            if (MinYieldGps > 0f) s += string.Format(CultureInfo.InvariantCulture, "  产≥{0:0.##}g/s", MinYieldGps);
            if (MinGrade == 1) s += "  [仅S级]";
            else if (MinGrade == 2) s += "  [A级及以上]";
            else if (MinGrade == 3) s += "  [B级及以上]";
            return s;
        }
    }

    public class MapPreviewData
    {
        public string ClusterFilePath;
        public int Seed;
        public int WorldIndex;
        public string WorldName;
        public int Width;
        public int Height;
        public int WorldOffsetX;
        public int WorldOffsetY;
        public int BaseStartX;
        public int BaseStartY;
        public int ElapsedMs;
        public string TraitsSummary;
        public bool IsPoiOnly;

        public MapElementDef[] Elements;
        public ushort[] ElemIdx;
        public short[] TempDeciC;
        public byte[] ZoneType;
        public List<MapPoiItem> Pois = new List<MapPoiItem>();

        static readonly string[] s_zoneNames =
        {
            "苔原生态",
            "水晶生态",
            "湿地生态",
            "砂岩生态",
            "丛林生态",
            "岩浆生态",
            "油质生态",
            "太空生态",
            "海洋生态",
            "铁锈生态",
            "森林生态",
            "辐射生态",
            "沼泽生态",
            "荒地生态",
            "火箭内部",
            "金属生态",
            "岩漠生态",
            "海牛生态",
            "冰窟生态",
            "冷池生态",
            "花蜜生态",
            "花园生态",
            "寒羽生态",
            "险沼生态",
            "藻林生态",
            "珊瑚生态",
            "深渊生态",
            "沙滩生态"
        };

        static readonly int[] s_zoneColors =
        {
            0x6AB4DF,
            0x9B72CF,
            0x5E7A45,
            0xC8A86C,
            0x8E4B7C,
            0xD4492A,
            0x363646,
            0x181B24,
            0x347E9E,
            0xA8603C,
            0x4A9658,
            0x5CC93C,
            0x628E6E,
            0xB8824B,
            0x464B58,
            0x9A8D5E,
            0x847A70,
            0x6E8058,
            0x79C2E6,
            0x578696,
            0xC4748E,
            0x5BA362,
            0x6E8B98,
            0x4F7D5C,
            0x2D7A6E,
            0x2E8EB0,
            0x1F2E4D,
            0xD9C28B
        };

        static readonly Dictionary<string, string> s_poiFallbackNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Headquarters", "打印舱（起点）" },
            { "OilWell", "储油石" },
            { "MassiveHeatSink", "反熵热量中和器" },
            { "GeneShuffler", "神经振荡仪" },
            { "WarpPortal", "传送发射器" },
            { "WarpReceiver", "传送接收器" },
            { "WarpConduitSender", "供给传送器输入端" },
            { "WarpConduitReceiver", "供给传送器输出端" },
            { "CryoTank", "低温箱（3015）" },
            { "TemporalTearOpener", "时间裂口开放器" },
            { "TemporalTearAnalyzer", "时间裂口分析仪" },
            { "SapTree", "实验体 52B" },
            { "GeothermalControllerEntity", "地热热泵控制端" },
            { "GeothermalVentEntity", "地热排气口" },
            { "HeatCompressor", "热能压缩机" },
            { "POICeresTechUnlock", "庄严科技终端（寒霜）" },
            { "POIDlc4TechUnlock", "庄严科技终端（史前）" },
            { "POIDlc5TechUnlock", "庄严科技终端（汪洋）" },
            { "PropSurfaceSatellite1", "坠毁的卫星" },
            { "PropSurfaceSatellite2", "坠毁的卫星（II）" },
            { "PropSurfaceSatellite3", "坠毁的卫星（III）" },
            { "MissileLauncher", "流星爆破炮" },
            { "GravitasPedestal", "庄严基座" },
            { "MegaBrainTank", "故事特质：梦境合成仪" },
            { "LonelyMinionHouse", "故事特质：神秘隐士小屋" },
            { "LonelyMailBox", "故事特质：神秘隐士信箱" },
            { "FossilDig", "故事特质：远古标本" },
            { "MorbRoverMaker", "故事特质：生机机构仪" },
            { "HijackedHeadquarters", "故事特质：打印截能仪" },
            { "GravitasCreatureManipulator", "故事特质：小动物衍变器" },
            { "GeyserGeneric_molten_gold", "金火山" },
            { "GeyserGeneric_molten_copper", "铜火山" },
            { "GeyserGeneric_molten_iron", "铁火山" },
            { "GeyserGeneric_molten_aluminum", "铝火山" },
            { "GeyserGeneric_molten_tungsten", "钨火山" },
            { "GeyserGeneric_molten_cobalt", "钴火山" },
            { "GeyserGeneric_molten_niobium", "铌火山" },
            { "NiobiumGeyser", "铌火山" },
            { "GeyserGeneric_small_volcano", "小型火山" },
            { "GeyserGeneric_big_volcano", "火山" },
            { "GeyserGeneric_steam", "低温蒸汽喷孔" },
            { "GeyserGeneric_hot_steam", "蒸汽喷孔" },
            { "GeyserGeneric_hot_water", "清水泉" },
            { "GeyserGeneric_slush_water", "低温泥浆泉" },
            { "GeyserGeneric_slush_salt_water", "低温盐泥泉" },
            { "GeyserGeneric_salt_water", "盐水泉" },
            { "GeyserGeneric_filthy_water", "低温污水泉" },
            { "GeyserGeneric_murky_brine", "污染浓盐泉" },
            { "GeyserGeneric_liquid_sulfur", "液硫泉" },
            { "GeyserGeneric_oil_drip", "漏油裂缝" },
            { "SmallReefGeyser", "潮汐泉" },
            { "GeyserGeneric_methane", "天然气喷孔" },
            { "MethaneGeyser", "天然气喷孔" },
            { "GeyserGeneric_hot_hydrogen", "氢气喷孔" },
            { "GeyserGeneric_chlorine_gas", "氯气喷孔" },
            { "GeyserGeneric_chlorine_gas_cool", "低温氯气喷孔" },
            { "ChlorineGeyser", "氯气喷孔" },
            { "GeyserGeneric_hot_co2", "二氧化碳喷孔" },
            { "GeyserGeneric_liquid_co2", "液态二氧化碳泉" },
            { "GeyserGeneric_hot_po2", "高温污氧喷孔" },
            { "GeyserGeneric_slimy_po2", "含菌污氧喷孔" }
        };

        public static List<PoiFilterCatalogEntry> GetFilterCatalog()
        {
            List<PoiFilterCatalogEntry> list = new List<PoiFilterCatalogEntry>();
            string[,] items = new string[,]
            {
                { "@metal_volcano", "metal_volcano", "[任意] 金属火山" },
                { "@volcano", "volcano", "[任意] 岩浆火山" },
                { "@water_geyser", "water_geyser", "[任意] 水源/液泉" },
                { "@gas_geyser", "gas_geyser", "[任意] 气体喷孔" },
                { "@oil", "oil", "[任意] 油井" },
                { "GeyserGeneric_molten_gold", "metal_volcano", "金火山" },
                { "GeyserGeneric_molten_copper", "metal_volcano", "铜火山" },
                { "GeyserGeneric_molten_iron", "metal_volcano", "铁火山" },
                { "GeyserGeneric_molten_aluminum", "metal_volcano", "铝火山（眼冒金星！）" },
                { "GeyserGeneric_molten_cobalt", "metal_volcano", "钴火山（眼冒金星！）" },
                { "GeyserGeneric_molten_tungsten", "metal_volcano", "钨火山（眼冒金星！）" },
                { "GeyserGeneric_molten_niobium", "metal_volcano", "铌火山（眼冒金星！）" },
                { "GeyserGeneric_small_volcano", "volcano", "小型火山" },
                { "GeyserGeneric_big_volcano", "volcano", "火山" },
                { "GeyserGeneric_steam", "water_geyser", "低温蒸汽喷孔" },
                { "GeyserGeneric_hot_steam", "water_geyser", "蒸汽喷孔" },
                { "GeyserGeneric_hot_water", "water_geyser", "清水泉" },
                { "GeyserGeneric_slush_water", "water_geyser", "低温泥浆泉" },
                { "GeyserGeneric_slush_salt_water", "water_geyser", "低温盐泥泉" },
                { "GeyserGeneric_salt_water", "water_geyser", "盐水泉" },
                { "GeyserGeneric_filthy_water", "water_geyser", "低温污水泉" },
                { "GeyserGeneric_liquid_sulfur", "water_geyser", "液硫泉（眼冒金星！）" },
                { "GeyserGeneric_murky_brine", "water_geyser", "污染浓盐泉（汪洋行星包）" },
                { "SmallReefGeyser", "water_geyser", "潮汐泉（汪洋行星包）" },
                { "GeyserGeneric_oil_drip", "water_geyser", "漏油裂缝" },
                { "GeyserGeneric_methane", "gas_geyser", "天然气喷孔" },
                { "GeyserGeneric_hot_hydrogen", "gas_geyser", "氢气喷孔" },
                { "GeyserGeneric_chlorine_gas", "gas_geyser", "氯气喷孔" },
                { "GeyserGeneric_chlorine_gas_cool", "gas_geyser", "低温氯气喷孔" },
                { "GeyserGeneric_hot_co2", "gas_geyser", "二氧化碳喷孔" },
                { "GeyserGeneric_liquid_co2", "gas_geyser", "液态二氧化碳泉" },
                { "GeyserGeneric_hot_po2", "gas_geyser", "高温污氧喷孔" },
                { "GeyserGeneric_slimy_po2", "gas_geyser", "含菌污氧喷孔" },
                { "OilWell", "oil", "储油石" },
                { "MassiveHeatSink", "poi", "反熵热量中和器" },
                { "GeneShuffler", "poi", "神经振荡仪" },
                { "SapTree", "poi", "实验体 52B" }
            };
            int n = items.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                list.Add(new PoiFilterCatalogEntry
                {
                    PrefabId = items[i, 0],
                    Category = items[i, 1],
                    DisplayName = items[i, 2]
                });
            }
            return list;
        }

        public static string GetZoneName(byte z)
        {
            if (z < s_zoneNames.Length) return s_zoneNames[z];
            return "未知区域";
        }

        public static int CategoryOrder(string cat)
        {
            if (cat == "start") return 0;
            if (cat == "metal_volcano") return 1;
            if (cat == "volcano") return 2;
            if (cat == "water_geyser") return 3;
            if (cat == "gas_geyser") return 4;
            if (cat == "oil") return 5;
            if (cat == "story") return 6;
            return 7;
        }

        public static MapPreviewData LoadFromFile(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int b0 = fs.ReadByte();
                int b1 = fs.ReadByte();
                fs.Position = 0;
                if (b0 == 0x1F && b1 == 0x8B)
                {
                    using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                        return LoadFromStream(gz);
                }
                return LoadFromStream(fs);
            }
        }

        public static MapPreviewData LoadFromStream(Stream stream)
        {
            using (BinaryReader br = new BinaryReader(stream, Encoding.UTF8))
            {
                byte[] magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'O' || magic[1] != 'N' || magic[2] != 'I' || magic[3] != 'P')
                    throw new InvalidDataException("无效的地图预览数据格式");
                int ver = br.ReadInt32();
                if (ver != 1 && ver != 2 && ver != 3)
                    throw new InvalidDataException("不支持的预览数据版本：" + ver);

                MapPreviewData d = new MapPreviewData();
                d.IsPoiOnly = (ver == 3);
                d.ClusterFilePath = br.ReadString();
                d.Seed = br.ReadInt32();
                d.WorldIndex = br.ReadInt32();
                d.WorldName = br.ReadString();
                d.Width = br.ReadInt32();
                d.Height = br.ReadInt32();
                d.WorldOffsetX = br.ReadInt32();
                d.WorldOffsetY = br.ReadInt32();
                d.BaseStartX = br.ReadInt32();
                d.BaseStartY = br.ReadInt32();
                d.ElapsedMs = br.ReadInt32();
                d.TraitsSummary = br.ReadString();

                if (ver == 3)
                {
                    d.Elements = new MapElementDef[0];
                }
                else
                {
                    int elemCount = br.ReadInt32();
                    d.Elements = new MapElementDef[elemCount];
                    for (int i = 0; i < elemCount; i++)
                    {
                        d.Elements[i] = new MapElementDef
                        {
                            R = br.ReadByte(),
                            G = br.ReadByte(),
                            B = br.ReadByte(),
                            State = br.ReadByte(),
                            Name = br.ReadString()
                        };
                    }

                    int total = d.Width * d.Height;
                    d.ElemIdx = new ushort[total];
                    d.TempDeciC = new short[total];
                    for (int i = 0; i < total; i++) d.ElemIdx[i] = br.ReadUInt16();
                    for (int i = 0; i < total; i++) d.TempDeciC[i] = br.ReadInt16();
                    d.ZoneType = br.ReadBytes(total);
                }

                int poiCount = br.ReadInt32();
                for (int i = 0; i < poiCount; i++)
                {
                    string pid = br.ReadString();
                    string cat = br.ReadString();
                    string disp = br.ReadString();
                    int px = br.ReadInt32();
                    int py = br.ReadInt32();
                    bool isRand = br.ReadByte() != 0;

                    bool hasStats = false;
                    float avgY = 0f, eruptY = 0f, tempC = 0f, eSec = 0f, iSec = 0f, aCyc = 0f, dCyc = 0f, pct = 0f;
                    byte grade = 0;
                    if (ver >= 2)
                    {
                        hasStats = br.ReadByte() != 0;
                        avgY = br.ReadSingle();
                        eruptY = br.ReadSingle();
                        tempC = br.ReadSingle();
                        eSec = br.ReadSingle();
                        iSec = br.ReadSingle();
                        aCyc = br.ReadSingle();
                        dCyc = br.ReadSingle();
                        grade = br.ReadByte();
                        pct = br.ReadSingle();
                    }

                    if (string.IsNullOrEmpty(disp) || string.Equals(disp, pid, StringComparison.OrdinalIgnoreCase))
                    {
                        string fb;
                        if (s_poiFallbackNames.TryGetValue(pid, out fb)) disp = fb;
                    }

                    MapPoiItem item = new MapPoiItem
                    {
                        PrefabId = pid,
                        Category = cat,
                        DisplayName = disp,
                        X = px,
                        Y = py,
                        IsRandomGeyser = isRand,
                        HasGeyserStats = hasStats,
                        AvgYieldGps = avgY,
                        EruptRateGps = eruptY,
                        OutputTempC = tempC,
                        EruptSec = eSec,
                        IdleSec = iSec,
                        ActiveCycles = aCyc,
                        DormantCycles = dCyc,
                        Grade = grade,
                        Percentile = pct
                    };
                    item.DistFromStart = d.DistanceFromStart(item);
                    d.Pois.Add(item);
                }

                d.Pois.Sort((a, b) =>
                {
                    int c = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
                    if (c != 0) return c;
                    c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
                    if (c != 0) return c;
                    return a.DistFromStart.CompareTo(b.DistFromStart);
                });

                return d;
            }
        }

        public void SaveToGzipFile(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Compress))
                using (BinaryWriter bw = new BinaryWriter(gz, Encoding.UTF8))
                {
                    bw.Write(new byte[] { (byte)'O', (byte)'N', (byte)'I', (byte)'P' });
                    bw.Write(2);
                    bw.Write(ClusterFilePath ?? "");
                    bw.Write(Seed);
                    bw.Write(WorldIndex);
                    bw.Write(WorldName ?? "");
                    bw.Write(Width);
                    bw.Write(Height);
                    bw.Write(WorldOffsetX);
                    bw.Write(WorldOffsetY);
                    bw.Write(BaseStartX);
                    bw.Write(BaseStartY);
                    bw.Write(ElapsedMs);
                    bw.Write(TraitsSummary ?? "");

                    int ec = Elements != null ? Elements.Length : 0;
                    bw.Write(ec);
                    for (int i = 0; i < ec; i++)
                    {
                        MapElementDef ed = Elements[i];
                        bw.Write(ed.R);
                        bw.Write(ed.G);
                        bw.Write(ed.B);
                        bw.Write(ed.State);
                        bw.Write(ed.Name ?? "");
                    }

                    int total = Width * Height;
                    for (int i = 0; i < total; i++) bw.Write(ElemIdx != null && i < ElemIdx.Length ? ElemIdx[i] : (ushort)0);
                    for (int i = 0; i < total; i++) bw.Write(TempDeciC != null && i < TempDeciC.Length ? TempDeciC[i] : (short)0);
                    if (ZoneType != null && ZoneType.Length >= total)
                        bw.Write(ZoneType, 0, total);
                    else
                    {
                        byte[] pad = new byte[total];
                        if (ZoneType != null && ZoneType.Length > 0)
                            Buffer.BlockCopy(ZoneType, 0, pad, 0, Math.Min(total, ZoneType.Length));
                        bw.Write(pad, 0, total);
                    }

                    int pc = Pois != null ? Pois.Count : 0;
                    bw.Write(pc);
                    for (int i = 0; i < pc; i++)
                    {
                        MapPoiItem p = Pois[i];
                        bw.Write(p.PrefabId ?? "");
                        bw.Write(p.Category ?? "");
                        bw.Write(p.DisplayName ?? "");
                        bw.Write(p.X);
                        bw.Write(p.Y);
                        bw.Write((byte)(p.IsRandomGeyser ? 1 : 0));
                        bw.Write((byte)(p.HasGeyserStats ? 1 : 0));
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
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        public int DistanceFromStart(MapPoiItem p)
        {
            if (p == null) return 0;
            int dx = p.X - BaseStartX;
            int dy = p.Y - BaseStartY;
            return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
        }

        public int CountPrefab(string prefabId)
        {
            int cnt = 0;
            for (int i = 0; i < Pois.Count; i++)
                if (string.Equals(Pois[i].PrefabId, prefabId, StringComparison.OrdinalIgnoreCase))
                    cnt++;
            return cnt;
        }

        public int CountMatchingRule(PoiFilterRule rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.PrefabId)) return 0;
            bool isWildcard = rule.PrefabId.StartsWith("@", StringComparison.Ordinal);
            string wildCat = isWildcard ? rule.PrefabId.Substring(1) : null;
            int cnt = 0;
            for (int i = 0; i < Pois.Count; i++)
            {
                MapPoiItem p = Pois[i];
                if (isWildcard)
                {
                    if (!string.Equals(p.Category, wildCat, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else
                {
                    if (!string.Equals(p.PrefabId, rule.PrefabId, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (rule.MaxDist > 0 && p.DistFromStart > rule.MaxDist)
                    continue;
                if (rule.MinYieldGps > 0f && (!p.HasGeyserStats || p.AvgYieldGps < rule.MinYieldGps))
                    continue;
                if (rule.MinGrade > 0 && (!p.HasGeyserStats || p.Grade == 0 || p.Grade > rule.MinGrade))
                    continue;
                cnt++;
            }
            return cnt;
        }

        public bool MatchesRule(PoiFilterRule rule)
        {
            if (rule == null) return true;
            int cnt = CountMatchingRule(rule);
            return rule.CompareAtMost ? (cnt <= rule.MinCount) : (cnt >= rule.MinCount);
        }

        public bool MatchesAllRules(List<PoiFilterRule> rules)
        {
            if (rules == null || rules.Count == 0) return true;
            for (int i = 0; i < rules.Count; i++)
            {
                if (!MatchesRule(rules[i]))
                    return false;
            }
            return true;
        }

        public Bitmap RenderBitmap(MapRenderMode mode)
        {
            if (IsPoiOnly || ElemIdx == null || Elements == null || Elements.Length == 0)
                throw new InvalidOperationException("当前数据仅含间歇泉快速筛查结果，不包含地形体素数据。");
            int w = Width, h = Height;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int total = w * h;
                byte[] pixels = new byte[total * 4];
                int elemLen = Elements != null ? Elements.Length : 0;

                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w;
                    int dstRow = (h - 1 - y) * w;
                    for (int x = 0; x < w; x++)
                    {
                        int si = srcRow + x;
                        int di = (dstRow + x) * 4;

                        byte r = 20, g = 22, b = 30;
                        if (mode == MapRenderMode.Temperature)
                        {
                            double tc = TempDeciC[si] / 10.0;
                            ushort eidx = ElemIdx[si];
                            byte st = (eidx < elemLen) ? Elements[eidx].State : (byte)0;
                            TempToRgb(tc, st == 0, out r, out g, out b);
                        }
                        else
                        {
                            ushort eidx = ElemIdx[si];
                            byte er = 26, eg = 29, eb = 38, st = 0;
                            if (eidx < elemLen)
                            {
                                MapElementDef ed = Elements[eidx];
                                er = ed.R; eg = ed.G; eb = ed.B; st = ed.State;
                            }

                            if (mode == MapRenderMode.ElementOnly)
                            {
                                if (st == 0) { r = 16; g = 18; b = 26; }
                                else if (st == 1) { r = (byte)((er * 75 + 20 * 25) / 100); g = (byte)((eg * 75 + 24 * 25) / 100); b = (byte)((eb * 75 + 34 * 25) / 100); }
                                else if (st == 2) { r = (byte)Math.Min(255, er + 18); g = (byte)Math.Min(255, eg + 18); b = (byte)Math.Min(255, eb + 18); }
                                else { r = er; g = eg; b = eb; }
                            }
                            else
                            {
                                byte z = ZoneType[si];
                                int zcol = (z < s_zoneColors.Length) ? s_zoneColors[z] : 0x262B38;
                                byte zr = (byte)((zcol >> 16) & 0xFF);
                                byte zg = (byte)((zcol >> 8) & 0xFF);
                                byte zb = (byte)(zcol & 0xFF);

                                if (st == 0)
                                {
                                    r = (byte)((zr * 45 + 14 * 55) / 100);
                                    g = (byte)((zg * 45 + 16 * 55) / 100);
                                    b = (byte)((zb * 45 + 22 * 55) / 100);
                                }
                                else if (st == 1)
                                {
                                    r = (byte)((er * 45 + zr * 55) / 100);
                                    g = (byte)((eg * 45 + zg * 55) / 100);
                                    b = (byte)((eb * 45 + zb * 55) / 100);
                                }
                                else if (st == 2)
                                {
                                    r = (byte)((er * 82 + zr * 18) / 100);
                                    g = (byte)((eg * 82 + zg * 18) / 100);
                                    b = (byte)((eb * 82 + zb * 18) / 100);
                                }
                                else
                                {
                                    r = (byte)((er * 65 + zr * 35) / 100);
                                    g = (byte)((eg * 65 + zg * 35) / 100);
                                    b = (byte)((eb * 65 + zb * 35) / 100);
                                }

                                bool biomeEdge = (y + 1 < h && ZoneType[(y + 1) * w + x] != z) ||
                                                 (x > 0 && ZoneType[si - 1] != z);
                                if (biomeEdge)
                                {
                                    r = (byte)((r * 80) / 100);
                                    g = (byte)((g * 80) / 100);
                                    b = (byte)((b * 80) / 100);
                                }
                                else if (st == 3)
                                {
                                    int topIdx = y + 1 < h ? (y + 1) * w + x : si;
                                    int leftIdx = x > 0 ? si - 1 : si;
                                    int botIdx = y > 0 ? (y - 1) * w + x : si;
                                    int rightIdx = x + 1 < w ? si + 1 : si;
                                    ushort ti = ElemIdx[topIdx], li = ElemIdx[leftIdx], bi = ElemIdx[botIdx], ri = ElemIdx[rightIdx];
                                    byte ts = ti < elemLen ? Elements[ti].State : (byte)0;
                                    byte ls = li < elemLen ? Elements[li].State : (byte)0;
                                    byte bs = bi < elemLen ? Elements[bi].State : (byte)0;
                                    byte rs = ri < elemLen ? Elements[ri].State : (byte)0;
                                    if (ts != 3 || ls != 3)
                                    {
                                        r = (byte)Math.Min(255, r + 14);
                                        g = (byte)Math.Min(255, g + 14);
                                        b = (byte)Math.Min(255, b + 14);
                                    }
                                    else if (bs != 3 || rs != 3)
                                    {
                                        r = (byte)((r * 86) / 100);
                                        g = (byte)((g * 86) / 100);
                                        b = (byte)((b * 86) / 100);
                                    }
                                }
                            }
                        }

                        pixels[di] = b;
                        pixels[di + 1] = g;
                        pixels[di + 2] = r;
                        pixels[di + 3] = 255;
                    }
                }

                Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }

        static void TempToRgb(double tempC, bool isVacuum, out byte r, out byte g, out byte b)
        {
            if (isVacuum)
            {
                r = 22; g = 24; b = 32;
                return;
            }
            if (tempC <= -40.0) { r = 40; g = 90; b = 220; return; }
            if (tempC <= 0.0)
            {
                double t = (tempC + 40.0) / 40.0;
                r = (byte)(40 + t * 20);
                g = (byte)(90 + t * 130);
                b = (byte)(220 + t * 35);
                return;
            }
            if (tempC <= 22.0)
            {
                double t = tempC / 22.0;
                r = (byte)(60 + t * 10);
                g = (byte)(220 - t * 20);
                b = (byte)(255 - t * 165);
                return;
            }
            if (tempC <= 45.0)
            {
                double t = (tempC - 22.0) / 23.0;
                r = (byte)(70 + t * 175);
                g = (byte)(200 + t * 30);
                b = (byte)(90 - t * 50);
                return;
            }
            if (tempC <= 120.0)
            {
                double t = (tempC - 45.0) / 75.0;
                r = 245;
                g = (byte)(230 - t * 120);
                b = (byte)(40 - t * 15);
                return;
            }
            double tHot = Math.Min(1.0, (tempC - 120.0) / 380.0);
            r = 245;
            g = (byte)(110 - tHot * 80);
            b = (byte)(25 + tHot * 95);
        }
    }
}
