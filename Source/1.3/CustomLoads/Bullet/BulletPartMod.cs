using JetBrains.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using CombatExtended;
using UnityEngine;
using Verse;

namespace CustomLoads.Bullet;

public class BulletPartMod
{
    private static readonly StringBuilder str = new();
    private static FieldInfo[] dataFields;

    public static string MakeDescription(IEnumerable<ModData> mods, IEnumerable<SecondaryDamage> damages, string localDesc = null)
    {
        str.Clear();

        const string POSITIVE = "<color=green>";
        const string NEGATIVE = "<color=red>";
        const string END_COLOR = "</color>";

        void PrintFactor(ModData mod, float multi)
        {
            str.Append(mod.IsGood(multi) ? POSITIVE : NEGATIVE);
            str.Append(mod.Label).Append(' ');
            if (multi > 0f)
                str.Append('+');
            str.Append(multi.ToString("P0"));

            str.AppendLine(END_COLOR);
        }

        void PrintOffset(ModData mod)
        {
            str.Append(mod.IsGood(mod.Offset) ? POSITIVE : NEGATIVE);

            str.Append(mod.Label).Append(' ');
            if (mod.Offset > 0)
                str.Append('+');
            str.Append(mod.Offset.ToString("0.##"));
            if (mod.OffsetUnit != null)
                str.Append(mod.OffsetUnit);

            str.AppendLine(END_COLOR);
        }

        if (!string.IsNullOrWhiteSpace(localDesc))
        {
            str.AppendLine(localDesc).AppendLine();
        }

        foreach (var dmg in damages)
        {
            str.Append($"{POSITIVE}Adds damage: ").Append(dmg.amount.ToString("0.#"));
            str.Append(' ').Append(dmg.def.LabelCap);

            if (dmg.chance < 1f)
                str.Append(" (").Append(dmg.chance.ToString("P0")).Append(')');

            str.AppendLine(END_COLOR);
        }

        // Good factors
        foreach (var mod in mods)
        {
            if (mod == null)
                continue;

            float multi = mod.Coefficient - 1f;
            if (multi != 0 && mod.IsGood(multi))
                PrintFactor(mod, multi);
        }

        // Good offsets
        foreach (var mod in mods)
        {
            if (mod == null)
                continue;
            
            if (mod.Offset != 0 && mod.IsGood(mod.Offset))
                PrintOffset(mod);
        }



        // Bad factors
        foreach (var mod in mods)
        {
            if (mod == null)
                continue;

            float multi = mod.Coefficient - 1f;
            if (multi != 0 && !mod.IsGood(multi))
                PrintFactor(mod, multi);
        }

        // Bad offsets
        foreach (var mod in mods)
        {
            if (mod == null)
                continue;

            if (mod.Offset != 0 && !mod.IsGood(mod.Offset))
                PrintOffset(mod);
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
    public string Description => description ??= MakeDescription(AllMods, secondaryDamages, desc);
    public string Summary => summary ??= MakeDescription(AllMods, secondaryDamages);

    public Texture2D OverrideTexture => overrideTexture == null ? null : overrideTextureCached ??= ContentFinder<Texture2D>.Get(overrideTexture);

    public BulletPart parts;
    public BulletPart disables;
    public string overrideTexture;
    public string desc;

    [ModInfo("Damage", offsetUnit: " HP")]
    [Range(0, 100)]
    public ModData damage;

    [ModInfo("Speed", offsetUnit: " tiles/sec")]
    [Range(0, 250)]
    public ModData speed;

    [ModInfo("Spread", false, "ShotSpread", "°")]
    [Range(0, 20)]
    public ModData spread;

    [ModInfo("AP (Sharp)", offsetUnit: " RHA")]
    [Range(0, 60)]
    public ModData apSharp;

    [ModInfo("AP (Blunt)", offsetUnit: " MPa")]
    [Range(0, 300)]
    public ModData apBlunt;

    [ModInfo("Bullets Per Shot")]
    [Range(0, 20)]
    public ModData pelletCount;

    [ModInfo("Recoil", false, "Recoil")]
    [Range(0, 10)]
    public ModData recoil;

    [ModInfo("Muzzle Flash Size", false, "MuzzleFlash")]
    [Range(0, 10)]
    public ModData muzzleFlash;

    [ModInfo("Rate of Fire", offsetUnit: " RPM")]
    [Range(0, 1200)]
    public ModData rateOfFire;

    [ModInfo("Burst Shot Count", statDefName: "BurstShotCount", offsetUnit: " shots")]
    [Range(1, 30)]
    public ModData burstShotCount;

    [ModInfo("Mass", false, offsetUnit: "kg", formatString: "0.####")]
    [Range(0f, 0.15f)]
    public ModData mass;

    public List<SecondaryDamage> secondaryDamages = new();

    //Too complicated to implement: range is not a simple stat mod or property, unfortunately..
    //[ModInfo("Range", statDefName: "Range", offsetUnit: " tiles")]
    //public ModData range;

    [XmlIgnore] private ModData[] allMods;
    [XmlIgnore] private string description;
    [XmlIgnore] private string summary;
    [XmlIgnore] private Texture2D overrideTextureCached;

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
            var attr2 = dataFields[i].GetCustomAttribute<RangeAttribute>();

            data.ID = dataFields[i].Name;
            data.Label = attr.Label;
            data.LargerIsBetter = attr.LargerIsBetter;
            data.StatDefName = attr.StatDefName;
            data.OffsetUnit = attr.OffsetUnit;
            data.FormatString = attr.FormatString;
            data.Range = attr2;

            allMods[i] = data;
        }
    }

    public class ModData
    {
        public float Coefficient;
        public float Offset;

        public string ID;
        public string Label;
        public bool LargerIsBetter;
        public string StatDefName;
        public string OffsetUnit;
        public string FormatString;
        public RangeAttribute Range;

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
        public readonly string OffsetUnit;
        public readonly string FormatString;

        public ModInfoAttribute(string label, bool largerIsBetter = true, string statDefName = null, string offsetUnit = null, string formatString = "0.##")
        {
            Label = label;
            LargerIsBetter = largerIsBetter;
            StatDefName = statDefName;
            OffsetUnit = offsetUnit;
            FormatString = formatString;
        }
    }
}