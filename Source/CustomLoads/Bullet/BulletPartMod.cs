using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace CustomLoads.Bullet;

public class BulletPartMod
{
    private static readonly StringBuilder str = new();
    private static FieldInfo[] dataFields;

    public static string MakeDescription(IEnumerable<ModData> mods)
    {
        str.Clear();

        const string POSITIVE = "<color=green>";
        const string NEGATIVE = "<color=red>";
        const string END_COLOR = "</color>";

        foreach (var mod in mods)
        {
            if (mod == null)
                continue;

            float multi = mod.Coefficient - 1f;
            if (multi != 0f)
            {
                str.Append(mod.IsGood(multi) ? POSITIVE : NEGATIVE);

                str.Append(mod.Label).Append(' ');
                if (multi > 0f)
                    str.Append('+');
                str.Append(multi.ToString("P0"));

                str.AppendLine(END_COLOR);
            }

            if (mod.Offset != 0)
            {
                str.Append(mod.IsGood(mod.Offset) ? POSITIVE : NEGATIVE);

                str.Append(mod.Label).Append(' ');
                if (mod.Offset > 0)
                    str.Append('+');
                str.Append(mod.Offset.ToString("0.##"));

                str.AppendLine(END_COLOR);
            }
        }

        return str.ToString();
    }

    public IReadOnlyList<ModData> AllMods
    {
        get
        {
            if (allMods == null)
                RefreshData();

            return allMods;
        }
    }
    public string Description => description ??= MakeDescription(AllMods);

    public BulletPart parts;

    [ModInfo("Damage")]
    public ModData damage;

    [ModInfo("Speed")]
    public ModData speed;

    [ModInfo("Spread", false, "ShotSpread")]
    public ModData spread;

    [ModInfo("AP (Sharp)")]
    public ModData apSharp;

    [ModInfo("AP (Blunt)")]
    public ModData apBlunt;

    [ModInfo("Bullets Per Shot")]
    public ModData pelletCount;

    [ModInfo("Recoil", false, "Recoil")]
    public ModData recoil;

    [ModInfo("Muzzle Flash Size", false, "MuzzleFlash")]
    public ModData muzzleFlash;

    [ModInfo("Rate of Fire", statDefName: "TicksBetweenBurstShots")]
    public ModData rateOfFire;

    [XmlIgnore] private ModData[] allMods;
    [XmlIgnore] private string description;

    public void RefreshData()
    {
        description = null; // Re-cache description.

        dataFields ??= typeof(BulletPartMod).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType == typeof(ModData)).ToArray();

        allMods = new ModData[dataFields.Length];
        for (int i = 0; i < allMods.Length; i++)
        {
            var data = (ModData)dataFields[i].GetValue(this);
            if (data == null)
                continue;

            var attr = dataFields[i].GetCustomAttribute<ModInfoAttribute>();
            data.ID = dataFields[i].Name;
            data.Label = attr.Label;
            data.LargerIsBetter = attr.LargerIsBetter;
            data.StatDefName = attr.StatDefName;

            allMods[i] = data;
        }
    }

    public class ModData
    {
        public float Coefficient;
        public float Offset;

        public string ID; // Not written  to in XML.
        public string Label; // Not written to in XML.
        public bool LargerIsBetter; // Not written to in XML.
        public string StatDefName; // Not written in XML.

        public bool IsGood(float change)
        {
            return change > 0f == LargerIsBetter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(ref float value)
        {
            value *= Coefficient;
            value += Offset;
        }

        public void Apply(ref int value, int? min = null)
        {
            float v = value;
            v *= Coefficient;
            v += Offset;
            value = Mathf.RoundToInt(v);
            if (min != null && value < min.Value)
                value = min.Value;
        }

        [UsedImplicitly]
        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            Coefficient = 1f;
            Offset = 0f;

            string txt = xmlRoot.InnerText.Trim();
            string[] parts = txt.Split(' ');

            foreach (var part in parts)
            {
                var p = part.Trim();

                bool multi = p[0] == 'x' || p[0] == 'X' || p[0] == '*';
                if (multi)
                {
                    p = p.Substring(1);
                    if (float.TryParse(p, out var c))
                        Coefficient = c;
                    else
                        Core.Error($"Failed to parse '{p}' as a coefficient float.");
                        
                    continue;
                }

                if (p[0] == '+')
                    p = p.Substring(1);

                if (float.TryParse(p, out var o))
                    Offset = o;
                else
                    Core.Error($"Failed to parse '{p}' as an offset float.");
            }
        }
    }
        
    [AttributeUsage(AttributeTargets.Field)]
    [MeansImplicitUse]
    public class ModInfoAttribute : Attribute
    {
        public readonly string Label;
        public readonly bool LargerIsBetter;
        public readonly string StatDefName;

        public ModInfoAttribute(string label, bool largerIsBetter = true, string statDefName = null)
        {
            Label = label;
            LargerIsBetter = largerIsBetter;
            StatDefName = statDefName;
        }
    }
}