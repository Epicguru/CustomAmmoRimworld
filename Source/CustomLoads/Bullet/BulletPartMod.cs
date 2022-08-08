using JetBrains.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace CustomLoads.Bullet
{
    public class BulletPartMod
    {
        private static readonly StringBuilder str = new();
        private static FieldInfo[] dataFields;

        public static string MakeDescription(IEnumerable<ModData> mods, IEnumerable<StatModifier> statFactors, IEnumerable<StatModifier> statOffsets)
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

                    str.Append(mod.Name).Append(' ');
                    if (multi > 0f)
                        str.Append('+');
                    str.Append(multi.ToString("P0"));

                    str.AppendLine(END_COLOR);
                }

                if (mod.Offset != 0)
                {
                    str.Append(mod.IsGood(mod.Offset) ? POSITIVE : NEGATIVE);

                    str.Append(mod.Name).Append(' ');
                    if (mod.Offset > 0)
                        str.Append('+');
                    str.Append(mod.Offset.ToString("0.##"));

                    str.AppendLine(END_COLOR);
                }
            }

            if (statFactors != null)
            {
                foreach (var stat in statFactors)
                {
                    str.Append(stat.stat.LabelForFullStatListCap).Append(' ');
                    str.AppendLine(stat.ToStringAsFactor);
                }
            }

            if (statOffsets != null)
            {
                foreach (var stat in statOffsets)
                {
                    str.Append(stat.stat.LabelForFullStatListCap).Append(' ');
                    str.AppendLine(stat.ValueToStringAsOffset);
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
        public string Description => description ??= MakeDescription(AllMods, statFactors, statOffsets);

        public BulletPart parts;

        [ModInfo("Damage")]
        public ModData damage;

        [ModInfo("Speed")]
        public ModData speed;

        [ModInfo("Spread", false)]
        public ModData spread;

        [ModInfo("AP (Sharp)")]
        public ModData apSharp;

        [ModInfo("AP (Blunt)")]
        public ModData apBlunt;

        public List<StatModifier> statFactors = new List<StatModifier>();
        public List<StatModifier> statOffsets = new List<StatModifier>();

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
                data.Name = attr.Label;
                data.LargerIsBetter = attr.LargerIsBetter;

                allMods[i] = data;
            }
        }

        public class ModData
        {
            public float Coefficient;
            public float Offset;

            public string Name; // Not written to in XML.
            public bool LargerIsBetter; // Not written to in XML.

            public bool IsGood(float change)
            {
                return change > 0f == LargerIsBetter;
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

            public ModInfoAttribute(string label, bool largerIsBetter = true)
            {
                Label = label;
                LargerIsBetter = largerIsBetter;
            }
        }
    }
}
