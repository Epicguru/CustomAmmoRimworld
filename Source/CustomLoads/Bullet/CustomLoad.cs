using CombatExtended;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace CustomLoads.Bullet;

public class CustomLoad : IExposable
{
    private static (AmmoDef ammo, ThingDef bullet) CloneAmmo(AmmoDef ammoTemplate, ThingDef bulletTemplate, string defName)
    {
        if (ammoTemplate == null)
            return (null, null);

        // Clone ammo def.
        var ammo = Core.Clone(ammoTemplate);
        ammo.defName = defName;
        ammo.label = defName;
        ammo.cachedLabelCap = null;
        ammo.description = "<Runtime-generated def>";
        ammo.descriptionDetailedCached = null;
        ammo.modContentPack = Core.ContentPack;
        ammo.menuHidden = false;
        ammo.destroyOnDrop = false;

        // Clone bullet.
        var bullet = Core.Clone(bulletTemplate);
        bullet.defName = $"{defName}_bullet";
        bullet.label = $"{defName} bullet";
        bullet.cachedLabelCap = null;
        bullet.description = defName;
        bullet.descriptionDetailedCached = null;
        bullet.modContentPack = Core.ContentPack;

        ammo.cookOffProjectile = bullet;
        foreach (var set in ammoTemplate.AmmoSetDefs)
        {
            set.ammoTypes.Add(new AmmoLink(ammo, bullet));
        }

        // Register to def databases...
        DefDatabase<Def>.Add(ammo);
        DefDatabase<BuildableDef>.Add(ammo);
        DefDatabase<ThingDef>.Add(ammo);
        DefDatabase<AmmoDef>.Add(ammo);

        DefDatabase<Def>.Add(bullet);
        DefDatabase<BuildableDef>.Add(bullet);
        DefDatabase<ThingDef>.Add(bullet);

        // Add ammo hyperlinks.
        foreach (var user in ammo.Users)
        {
            // Add ammo to gun hyperlinks.
            user.descriptionHyperlinks ??= new List<DefHyperlink>();
            user.descriptionHyperlinks.Add(ammo);
        }

        return (ammo, bullet);
    }

    public string LabelCap => Ammo?.LabelCap ?? "<uninitialized>";
    public string TechnicalDescription => cachedTechDesc ??= MakeTechDescription();
    public IReadOnlyList<BulletPartMod.ModData> MergedMods => mergedMods ??= MergeMods();

    public AmmoDef Ammo;
    public ThingDef Bullet;
    public Dictionary<BulletPart, BulletMaterialDef> Parts = new Dictionary<BulletPart, BulletMaterialDef>();

    private BulletPartMod.ModData[] mergedMods;
    private string cachedTechDesc;

    public void ExposeData()
    {
        Scribe_Defs.Look(ref Ammo, "Def");
        Scribe_Collections.Look(ref Parts, "Parts", LookMode.Value, LookMode.Def);
    }

    public string MakeTechDescription()
    {
        var str = new StringBuilder(128);

        if (Parts.TryGetValue(BulletPart.BulletCore, out var core))
        {
            str.Append(core.TechLabel).Append("-core ");
        }
        if (Parts.TryGetValue(BulletPart.BulletTip, out var tip))
        {
            str.Append(tip.TechLabel).Append("-tipped ");
        }
        if (Parts.TryGetValue(BulletPart.BulletJacket, out var jacket))
        {
            str.Append(jacket.TechLabel).Append(' ');
        }

        str.Append("bullet");

        var extra = Parts.Values.Where(m => !string.IsNullOrWhiteSpace(m.extraDesc)).Select(s => s.extraDesc).ToList();
        if (extra.Count > 0)
        {
            str.Append(" with ");
            for (int i = 0; i < extra.Count; i++)
            {
                var item = extra[i];
                str.Append(item.Trim().UncapitalizeFirst());
                if (i == extra.Count - 2)
                    str.Append(" and ");
                else if (i != extra.Count - 1)
                    str.Append(", ");
            }
        }

        if (Parts.TryGetValue(BulletPart.Casing, out var casing))
        {
            str.Append(" in a ");
            str.Append(casing.TechLabel);
            str.Append(" cartridge case");
        }

        str.AppendLine(".\n");
        str.AppendLine("Mod effects:");
        str.Append(BulletPartMod.MakeDescription(MergedMods));

        return str.ToString().TrimEnd();
    }

    public IEnumerable<StatMod> GetStatMods()
    {
        foreach (var pair in Parts)
        {
            var mod = pair.Value.TryGetModFor(pair.Key);

            foreach (var item in mod.AllMods)
            {
                if (item?.StatDefName == null)
                    continue;

                yield return new StatMod
                {
                    Material = pair.Value,
                    BulletPart = pair.Key,
                    Mod = item
                };
            }
        }
    }

    public BulletPartMod.ModData[] MergeMods()
    {
        BulletPartMod.ModData[] collection = null;

        foreach (BulletPartMod modCollection in Parts.Select(pair => pair.Value.TryGetModFor(pair.Key)))
        {
            if (collection == null)
            {
                collection = new BulletPartMod.ModData[modCollection.AllMods.Count];
                for (int i = 0; i < collection.Length; i++)
                {
                    collection[i] = new BulletPartMod.ModData
                    {
                        Coefficient = 1f,
                        Offset = 0f,
                        ID = modCollection.AllMods[i]?.ID,
                        Label = modCollection.AllMods[i]?.Label,
                        LargerIsBetter = modCollection.AllMods[i]?.LargerIsBetter ?? true
                    };
                }
            }

            for (int i = 0; i < modCollection.AllMods.Count; i++)
            {
                var mod = modCollection.AllMods[i];
                if (mod == null)
                    continue;

                ref var item = ref collection[i];
                item.Label = mod.Label;
                item.LargerIsBetter = mod.LargerIsBetter;
                item.ID = mod.ID;

                float multi = mod.Coefficient - 1f;
                item.Coefficient += multi;
                item.Offset += mod.Offset;
            }
        }

        return collection;
    }

    private BulletPartMod.ModData GetModOrDefault(string id)
    {
        var merged = MergedMods;
        foreach (var mod in merged)
        {
            if (mod == null)
                continue;

            if (mod.ID == id)
                return mod;
        }

        return new BulletPartMod.ModData
        {
            Coefficient = 1f,
            Offset = 0f
        };
    }

    public void GenerateDefs(AmmoDef ammoTemplate, ThingDef bulletTemplate, string defName)
    {
        var pair = CloneAmmo(ammoTemplate, bulletTemplate, defName);

        Ammo = pair.ammo;
        Bullet = pair.bullet;
        var ext = new AmmoModExtension();

        #region Ammo values

        var proj = Bullet.projectile as ProjectilePropertiesCE;

        GetModOrDefault("damage").Apply(ref proj.damageAmountBase, 1);
        GetModOrDefault("speed").Apply(ref proj.speed);
        GetModOrDefault("apSharp").Apply(ref proj.armorPenetrationSharp);
        GetModOrDefault("apBlunt").Apply(ref proj.armorPenetrationBlunt);
        GetModOrDefault("pelletCount").Apply(ref proj.pelletCount, 1);

        #endregion

        #region Stats


        foreach (var factor in GetStatMods())
        {
            if (!ext.statMods.TryGetValue(factor.Stat, out var list))
            {
                list = new List<StatMod>();
                ext.statMods.Add(factor.Stat, list);
            }
            list.Add(factor);
        }

        #endregion

        Ammo.modExtensions ??= new List<DefModExtension>();
        Ammo.modExtensions.Add(ext);
        Ammo.tradeability = Tradeability.None;
    }
}