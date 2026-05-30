using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOBlackBox
{
    internal static class ResearchReflectionProbe
    {
        private static string dumpDir = "";
        private static float lastDumpTime = 0f;
        private static int sessionRun = 0;

        private static void EnsureDumpDir()
        {
            if (string.IsNullOrEmpty(dumpDir))
            {
                string baseDir = Path.Combine(BepInEx.Paths.PluginPath, "NOBlackBox", "research-dumps");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                dumpDir = Path.Combine(baseDir, $"{timestamp}_session-{sessionRun++}");
                Directory.CreateDirectory(dumpDir);
                Plugin.Logger?.LogInfo($"Research dump directory: {dumpDir}");
            }
        }

        public static void WriteSessionInfo(string missionName, string mapPath, int unitCount)
        {
            if (!Configuration.ResearchLoggingEnabled.Value)
                return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "session-info.txt");
            using (var w = File.CreateText(path))
            {
                w.WriteLine($"Nuclear Option: {Application.version}");
                w.WriteLine($"NOBlackBox: 0.3.8.2");
                w.WriteLine($"Mission: {missionName}");
                w.WriteLine($"Map: {mapPath}");
                w.WriteLine($"Unit count: {unitCount}");
                w.WriteLine($"Timestamp: {DateTime.Now:O}");
            }
        }

        public static void DumpUnitFields(Unit unit)
        {
            if (!Configuration.ResearchDumpApiFields.Value)
                return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "unit-fields.csv");
            bool header = !File.Exists(path);

            using (var w = File.AppendText(path))
            {
                if (header)
                    w.WriteLine("unitId,unitName,code,type,fieldName,fieldType,fieldValue");

                DumpFieldsTo(w, unit, unit.persistentID.Id, unit.definition.unitName, unit.definition.code, unit.GetType().Name);
            }
        }

        private static void DumpFieldsTo(StreamWriter w, object obj, long unitId, string unitName, string code, string typeName)
        {
            var seen = new HashSet<string>();
            Type t = obj.GetType();

            while (t != null && t != typeof(object))
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (seen.Contains(f.Name)) continue;
                    seen.Add(f.Name);

                    try
                    {
                        object val = f.GetValue(obj);
                        string valStr = FormatValue(val);
                        w.WriteLine($"{unitId},{EscapeCsv(unitName)},{EscapeCsv(code)},{EscapeCsv(typeName)},{EscapeCsv(f.Name)},{EscapeCsv(f.FieldType.Name)},{EscapeCsv(valStr)}");
                    }
                    catch { }
                }
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (seen.Contains(p.Name)) continue;
                    seen.Add(p.Name);

                    try
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        object val = p.GetValue(obj);
                        string valStr = FormatValue(val);
                        w.WriteLine($"{unitId},{EscapeCsv(unitName)},{EscapeCsv(code)},{EscapeCsv(typeName)},{EscapeCsv(p.Name)},{EscapeCsv(p.PropertyType.Name)},{EscapeCsv(valStr)}");
                    }
                    catch { }
                }
                t = t.BaseType;
            }
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is string s) return s;
            if (val is bool b) return b ? "1" : "0";
            if (val is float f) return f.ToString("0.##", CultureInfo.InvariantCulture);
            if (val is int i) return i.ToString(CultureInfo.InvariantCulture);
            if (val is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (val is Vector3 v3) return $"({v3.x:F2},{v3.y:F2},{v3.z:F2})";
            if (val is Vector2 v2) return $"({v2.x:F2},{v2.y:F2})";
            if (val is Quaternion q) return $"({q.x:F2},{q.y:F2},{q.z:F2},{q.w:F2})";
            if (val is Enum e) return e.ToString();
            if (val is Unit u) return $"{u.definition.unitName}({u.persistentID.Id})";
            if (val is UnityEngine.Object uo) return uo.name ?? uo.GetType().Name;
            return val.ToString() ?? val.GetType().Name;
        }

        public static void DumpTargets(Unit[] targets, string sourceUnitName, long sourceUnitId)
        {
            if (!Configuration.ResearchDumpTargets.Value)
                return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "targets.csv");
            bool header = !File.Exists(path);

            using (var w = File.AppendText(path))
            {
                if (header)
                    w.WriteLine("time,targetCount,sourceUnitId,sourceUnitName,targetId,targetName,targetCode");

                foreach (Unit t in targets)
                {
                    if (t == null) continue;
                    w.WriteLine($"{Time.time:F2},{targets.Length},{sourceUnitId},{EscapeCsv(sourceUnitName)},{t.persistentID.Id},{EscapeCsv(t.definition.unitName)},{t.definition.code}");
                }
            }
        }

        public static void DumpBuildingCounts(Unit[] allUnits)
        {
            if (!Configuration.ResearchDumpBuildings.Value)
                return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "buildings.csv");
            bool header = !File.Exists(path);

            int buildingCount = 0;
            int sceneryCount = 0;
            int otherCount = 0;
            int networkedCount = 0;

            foreach (Unit u in allUnits)
            {
                if (u is Building) buildingCount++;
                else if (u is Scenery) sceneryCount++;
                else otherCount++;

                if (u.networked) networkedCount++;
            }

            using (var w = File.AppendText(path))
            {
                if (header)
                    w.WriteLine("time,totalUnits,buildings,scenery,other,networked,nonNetworked");

                int nonNetworked = allUnits.Length - networkedCount;
                w.WriteLine($"{Time.time:F2},{allUnits.Length},{buildingCount},{sceneryCount},{otherCount},{networkedCount},{nonNetworked}");
            }
        }

        private static readonly string[] ewKeywords = new[] { "jam", "ecm", "ew", "noise", "rwr", "chaff", "flar", "irJam", "countermeasure", "cm" };
        private static readonly string[] detectionKeywords = new[] { "detect", "datalink", "sensor", "trackedBy", "radarContact", "spot", "lock", "spotted", "aware" };
        private static Dictionary<long, float> lastEwDump = new();
        private static Dictionary<long, float> lastDetDump = new();

        private static bool ShouldDumpEW(long unitId)
        {
            float interval = Configuration.ResearchDumpIntervalSeconds.Value;
            if (lastEwDump.TryGetValue(unitId, out float last) && (Time.time - last) < interval)
                return false;
            lastEwDump[unitId] = Time.time;
            return true;
        }

        private static bool ShouldDumpDetection(long unitId)
        {
            float interval = Configuration.ResearchDumpIntervalSeconds.Value;
            if (lastDetDump.TryGetValue(unitId, out float last) && (Time.time - last) < interval)
                return false;
            lastDetDump[unitId] = Time.time;
            return true;
        }

        public static void DumpEW(Unit unit)
        {
            if (!Configuration.ResearchDumpEW.Value) return;
            if (!ShouldDumpEW(unit.persistentID.Id)) return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "ew.csv");
            bool header = !File.Exists(path);

            using (var w = File.AppendText(path))
            {
                if (header)
                    w.WriteLine("time,unitId,unitName,code,typeName,fieldName,fieldType,fieldValue");

                DumpFieldsMatching(w, unit, unit.persistentID.Id, unit.definition.unitName, unit.definition.code,
                    unit.GetType().Name, ewKeywords);

                DumpWeaponStations(w, unit, header);
            }
        }

        private static void DumpWeaponStations(StreamWriter w, Unit unit, bool header)
        {
            try
            {
                FieldInfo wsField = unit.GetType().GetField("weaponStations",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (wsField == null) return;

                object wsObj = wsField.GetValue(unit);
                if (wsObj == null) return;

                PropertyInfo countProp = wsObj.GetType().GetProperty("Count");
                PropertyInfo indexer = wsObj.GetType().GetProperty("Item");
                if (countProp == null || indexer == null) return;

                int count = (int)countProp.GetValue(wsObj);
                for (int i = 0; i < count; i++)
                {
                    object station = indexer.GetValue(wsObj, new object[] { i });
                    if (station == null) continue;

                    string prefix = $"ws[{i}].";
                    var seen = new HashSet<string>();
                    Type t = station.GetType();

                    while (t != null && t != typeof(object))
                    {
                        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (seen.Contains(f.Name)) continue;
                            seen.Add(f.Name);

                            try
                            {
                                object val = f.GetValue(station);
                                string valStr = FormatValue(val);
                                w.WriteLine($"{Time.time:F2},{unit.persistentID.Id},{EscapeCsv(unit.definition.unitName)},{unit.definition.code},{unit.GetType().Name},{prefix}{EscapeCsv(f.Name)},{EscapeCsv(f.FieldType.Name)},{EscapeCsv(valStr)}");
                            }
                            catch { }
                        }
                        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (seen.Contains(p.Name)) continue;
                            seen.Add(p.Name);

                            try
                            {
                                if (p.GetIndexParameters().Length > 0) continue;
                                object val = p.GetValue(station);
                                string valStr = FormatValue(val);
                                w.WriteLine($"{Time.time:F2},{unit.persistentID.Id},{EscapeCsv(unit.definition.unitName)},{unit.definition.code},{unit.GetType().Name},{prefix}{EscapeCsv(p.Name)},{EscapeCsv(p.PropertyType.Name)},{EscapeCsv(valStr)}");
                            }
                            catch { }
                        }
                        t = t.BaseType;
                    }
                }
            }
            catch { }
        }

        public static void DumpDetection(Unit unit)
        {
            if (!Configuration.ResearchDumpDetection.Value) return;
            if (!ShouldDumpDetection(unit.persistentID.Id)) return;

            EnsureDumpDir();
            string path = Path.Combine(dumpDir, "detection.csv");
            bool header = !File.Exists(path);

            using (var w = File.AppendText(path))
            {
                if (header)
                    w.WriteLine("time,unitId,unitName,code,typeName,fieldName,fieldType,fieldValue");

                DumpFieldsMatching(w, unit, unit.persistentID.Id, unit.definition.unitName, unit.definition.code,
                    unit.GetType().Name, detectionKeywords);
            }
        }

        private static void DumpFieldsMatching(StreamWriter w, object obj, long unitId, string unitName, string code, string typeName, string[] keywords)
        {
            var seen = new HashSet<string>();
            Type t = obj.GetType();

            while (t != null && t != typeof(object))
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (seen.Contains(f.Name)) continue;
                    seen.Add(f.Name);

                    if (!keywords.Any(k => f.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    try
                    {
                        object val = f.GetValue(obj);
                        string valStr = FormatValue(val);
                        w.WriteLine($"{Time.time:F2},{unitId},{EscapeCsv(unitName)},{EscapeCsv(code)},{EscapeCsv(typeName)},{EscapeCsv(f.Name)},{EscapeCsv(f.FieldType.Name)},{EscapeCsv(valStr)}");
                    }
                    catch { }
                }
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (seen.Contains(p.Name)) continue;
                    seen.Add(p.Name);

                    if (!keywords.Any(k => p.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    try
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        object val = p.GetValue(obj);
                        string valStr = FormatValue(val);
                        w.WriteLine($"{Time.time:F2},{unitId},{EscapeCsv(unitName)},{EscapeCsv(code)},{EscapeCsv(typeName)},{EscapeCsv(p.Name)},{EscapeCsv(p.PropertyType.Name)},{EscapeCsv(valStr)}");
                    }
                    catch { }
                }
                t = t.BaseType;
            }
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}
