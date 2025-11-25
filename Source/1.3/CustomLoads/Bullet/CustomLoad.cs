using CombatExtended;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace CustomLoads.Bullet;

public class CustomLoad : IExposable
{
    private static (AmmoDef ammo, ThingDef bullet) CloneAmmo(AmmoDef ammoTemplate, ThingDef bulletTemplate, string defName, bool register)
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

        var newBases = new List<StatModifier>();
        foreach (var b in ammoTemplate.statBases)
        {
            newBases.Add(new StatModifier
            {
                stat = b.stat,
                value = b.value
            });
        }
        ammo.statBases = newBases;

        if (!register)
            return (ammo, bullet);

        foreach (var set in ammoTemplate.AmmoSetDefs)
        {
            set.ammoTypes.Add(new AmmoLink(ammo, bullet));
        }

        // Add ammo hyperlinks.
        foreach (var user in ammo.Users)
        {
            // Add ammo to gun hyperlinks.
            user.descriptionHyperlinks ??= new List<DefHyperlink>();
            user.descriptionHyperlinks.Add(ammo);
        }

        return (ammo, bullet);
    }

    public string LabelCap => Label?.CapitalizeFirst();
    public string TechnicalDescription => cachedTechDesc ??= MakeTechDescription();
    public IReadOnlyList<BulletPartMod.ModData> MergedMods => mergedMods ??= MergeMods();
    public bool IsErrored => AmmoTemplate == null || BulletTemplate == null;

    public RecipeDef TemplateRecipe => templateRecipeCached ??= DefDatabase<RecipeDef>.AllDefsListForReading.First(d => d.ProducedThingDef == AmmoTemplate);

    // Stuff saved and loaded:
    public int PowderLoad;
    public string DefName;
    public string Label;
    public string Designation;
    public bool IsLocked;
    public AmmoDef AmmoTemplate;
    public ThingDef BulletTemplate;
    public GunpowderEffectsDef GunpowderEffects;
    public Dictionary<BulletPart, BulletMaterialDef> Parts = new();
    public float PowderSliderFloat;

    // Stuff generated:
    public AmmoDef Ammo;
    public ThingDef Bullet;
    public AmmoCategoryDef Category;
    public RecipeDef Recipe;

    private string ammoTemplateDefName;
    private string bulletTemplateDefName;
    private BulletPartMod.ModData[] mergedMods;
    private string cachedTechDesc;
    private RecipeDef templateRecipeCached;

    public void ExposeData()
    {
        Scribe_Values.Look(ref PowderLoad, "PowderLoad");
        Scribe_Values.Look(ref DefName, "DefName");
        Scribe_Values.Look(ref Label, "Label");
        Scribe_Values.Look(ref Designation, "Designation");
        Scribe_Values.Look(ref IsLocked, "IsLocked");
        Scribe_Defs.Look(ref GunpowderEffects, "GunpowderEffects");

        if (Scribe.mode == LoadSaveMode.Saving && AmmoTemplate != null && BulletTemplate != null)
        {
            ammoTemplateDefName = AmmoTemplate.defName;
            bulletTemplateDefName = BulletTemplate.defName;
        }

        Scribe_Values.Look(ref ammoTemplateDefName, "AmmoTemplate");
        Scribe_Values.Look(ref bulletTemplateDefName, "BulletTemplate");

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            AmmoTemplate   = DefDatabase<AmmoDef>.GetNamed(ammoTemplateDefName);
            BulletTemplate = DefDatabase<ThingDef>.GetNamed(bulletTemplateDefName);
        }

        Scribe_Collections.Look(ref Parts, "Parts", LookMode.Value, LookMode.Def);

        Label ??= "";
        Designation ??= "";
    }

    public void Refresh()
    {
        cachedTechDesc = MakeTechDescription();
        mergedMods = MergeMods();
    }

    public IEnumerable<SecondaryDamage> AllSecondaryDamages()
    {
        foreach (var pair in Parts)
        {
            var mod = pair.Value.TryGetModFor(pair.Key);
            foreach (var dmg in mod.secondaryDamages)
                yield return dmg;
        }

        GunpowderEffects ??= DefDatabase<GunpowderEffectsDef>.AllDefsListForReading[0];
        var gp = GunpowderEffects.TryGetMod(PowderLoad);
        if (gp?.secondaryDamages == null)
            yield break;

        foreach (var dmg in gp.secondaryDamages)
            yield return dmg;
    }

    public IEnumerable<(ThingDef item, int count)> GetAdditionalCostRaw(int toCraft)
    {
        // Cost of each part.
        foreach (var mat in Parts.Values)
        {
            if (mat.costPerBullet <= 0f)
                continue;

            int amount = Mathf.Max(1, Mathf.RoundToInt(mat.costPerBullet * toCraft * (mat.material.smallVolume ? ThingDef.SmallVolumePerUnit : 1f)));
            yield return (mat.material, amount);
        }

        // Gunpowder is a special case.
        GunpowderEffects ??= DefDatabase<GunpowderEffectsDef>.AllDefsListForReading[0];
        var gp = GunpowderEffects.TryGetMod(PowderLoad);
        if (GunpowderEffects.material != null && GunpowderEffects.costPerBullet > 0f && gp != null)
        {
            float coef = PowderLoad * 0.25f;
            if (coef <= 0f)
                yield break;
            int amount = Mathf.Max(1, Mathf.RoundToInt(GunpowderEffects.costPerBullet * toCraft * coef));
            yield return (GunpowderEffects.material, amount);
        }
    }

    public static List<IngredientCount> CloneRecipeIngredients(List<IngredientCount> ingredients)
    {
        var list = new List<IngredientCount>(ingredients.Count);

        foreach (var item in ingredients)
        {
            var filter = new ThingFilter();
            filter.SetDisallowAll();
            filter.CopyAllowancesFrom(item.filter);

            list.Add(new IngredientCount
            {
                count = item.count,
                filter = filter
            });
        }

        return list;
    }

    public void MergeRecipeIngredients(List<IngredientCount> existingIngredients, int toCraftCount)
    {
        foreach (var pair in GetAdditionalCostRaw(toCraftCount))
        {
            var mat = pair.item;
            bool found = false;

            foreach (var item in existingIngredients)
            {
                if (item.filter.AllowedDefCount == 1 && item.filter.allowedDefs.Contains(mat))
                {
                    item.count += pair.count;
                    found = true;
                    break;
                }
            }

            if (found)
                continue;

            var filter = new ThingFilter();
            filter.SetDisallowAll();
            filter.SetAllow(mat, true);

            existingIngredients.Add(new IngredientCount
            {
                count = pair.count,
                filter = filter
            });
        }
    }

    public string MakeTechDescription()
    {
        var str = new StringBuilder(128);

        str.Append("The ").Append(AmmoTemplate?.AmmoSetDefs?[0].LabelCap);
        str.Append(" (").Append(Designation).Append(") '").Append(LabelCap).Append("' is a ");

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

        GunpowderEffects ??= DefDatabase<GunpowderEffectsDef>.AllDefsListForReading[0];

        foreach (BulletPartMod mod in Parts.Select(pair => pair.Value.TryGetModFor(pair.Key)).Concat(GunpowderEffects.TryGetMod(PowderLoad)).Where(mod => !string.IsNullOrWhiteSpace(mod.desc)))
        {
            str.AppendLine(mod.desc);
        }

        str.AppendLine();
        //str.AppendLine("Mod effects:");
        str.Append(BulletPartMod.MakeDescription(MergedMods, AllSecondaryDamages()));

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

        GunpowderEffects ??= DefDatabase<GunpowderEffectsDef>.AllDefsListForReading[0];

        var powder = GunpowderEffects.TryGetMod(PowderLoad);
        if (powder == null)
            yield break;
        
        foreach (var item in powder.AllMods)
        {
            if (item?.StatDefName == null)
                continue;

            yield return new StatMod
            {
                Material = null,
                BulletPart = BulletPart.Powder,
                Mod = item
            };
        }
    }

    public BulletPartMod.ModData[] MergeMods()
    {
        BulletPartMod.ModData[] collection = null;

        GunpowderEffects ??= DefDatabase<GunpowderEffectsDef>.AllDefsListForReading[0];

        foreach (BulletPartMod modCollection in Parts.Select(pair => pair.Value.TryGetModFor(pair.Key)).Concat(GunpowderEffects.TryGetMod(PowderLoad)))
        {
            if (modCollection == null)
                continue;

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
                        LargerIsBetter = modCollection.AllMods[i]?.LargerIsBetter ?? true,
                        OffsetUnit = modCollection.AllMods[i]?.OffsetUnit,
                        Range = modCollection.AllMods[i]?.Range,
                        FormatString = modCollection.AllMods[i]?.FormatString,
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
                item.OffsetUnit = mod.OffsetUnit;
                item.Range = mod.Range;
                item.FormatString = mod.FormatString;

                float multi = mod.Coefficient - 1f;
                item.Coefficient += multi;
                item.Offset += mod.Offset;
            }
        }

        collection ??= Array.Empty<BulletPartMod.ModData>();
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

    public void UpdateLabels()
    {
        if (Ammo == null)
            return;

        Ammo.label = $"{Ammo.AmmoSetDefs[0].LabelCap} ({Designation}) '{Label}'";
        Ammo.cachedLabelCap = null;

        Bullet.label = Ammo.label + " bullet";
        Bullet.cachedLabelCap = null;

        if (Recipe == null)
            return;

        Recipe.label = $"make {Ammo.AmmoSetDefs[0].label} {Label} x{Recipe.products[0].count}";
        Recipe.description = $"Craft {Recipe.products[0].count} {Ammo.AmmoSetDefs[0].label} {Label}.";
        Recipe.jobString = $"making {Ammo.AmmoSetDefs[0].label} {Label}.";
        Recipe.cachedLabelCap = null;
    }

    public void GenerateDefs(AmmoDef ammoTemplate, ThingDef bulletTemplate, bool register)
    {
        AmmoTemplate = ammoTemplate;
        BulletTemplate = bulletTemplate;

        var clones = CloneAmmo(ammoTemplate, bulletTemplate, DefName, register);
        Ammo = clones.ammo;
        Bullet = clones.bullet;

        if (register)
        {
            Ammo.shortHash = 0;
            Bullet.shortHash = 0;
            ShortHashGiver.GiveShortHash(Ammo, Ammo.GetType());
            ShortHashGiver.GiveShortHash(Bullet, Bullet.GetType());

            // Register to def databases...
            DefDatabase<Def>.Add(Ammo);
            DefDatabase<BuildableDef>.Add(Ammo);
            DefDatabase<ThingDef>.Add(Ammo);
            DefDatabase<AmmoDef>.Add(Ammo);

            DefDatabase<Def>.Add(Bullet);
            DefDatabase<BuildableDef>.Add(Bullet);
            DefDatabase<ThingDef>.Add(Bullet);

            // Register with ThingCategories, needed for ThingFilters.
            // Which enables the item to be stored.
            void AddAndRebuild(ThingCategoryDef def)
            {
                def.childThingDefs.Add(Ammo);

                var cats = new List<ThingCategoryDef>();
                while (def != null)
                {
                    cats.Add(def);
                    def = def.parent;
                }

                for (int i = cats.Count - 1; i >= 0; i--)
                {
                    var cat = cats[i];
                    cat.allChildThingDefsCached = null;
                    cat.sortedChildThingDefsCached = null;
                    cat.ResolveReferences();
                }
            }

            foreach (var cat in Ammo.thingCategories)
            {
                AddAndRebuild(cat);
            }

            // Register with resource readout.
            ResourceCounter.resources.Add(Ammo);

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    map.resourceCounter.countedAmounts[Ammo] = 0;
                }
            }
        }

        var ext = new AmmoModExtension();

        void AddStatMod(StatMod statMod)
        {
            if (!ext.statMods.TryGetValue(statMod.Stat, out var list))
            {
                list = new List<StatMod>();
                ext.statMods.Add(statMod.Stat, list);
            }
            list.Add(statMod);
        }

        // Projectile properties:

        var proj = Bullet.projectile as ProjectilePropertiesCE;
        float minSpeed = Mathf.Min(10f, proj.speed);
        float mass = AmmoTemplate.GetStatValueAbstract(StatDefOf.Mass);

        GetModOrDefault("damage").Apply(ref proj.damageAmountBase, 1);
        GetModOrDefault("speed").Apply(ref proj.speed);
        GetModOrDefault("apSharp").Apply(ref proj.armorPenetrationSharp);
        GetModOrDefault("apBlunt").Apply(ref proj.armorPenetrationBlunt);
        GetModOrDefault("pelletCount").Apply(ref proj.pelletCount, 1);
        GetModOrDefault("mass").Apply(ref mass);

        proj.speed = Mathf.Max(minSpeed, proj.speed);
        proj.armorPenetrationSharp = Mathf.Max(0, proj.armorPenetrationSharp);
        proj.armorPenetrationBlunt = Mathf.Max(0, proj.armorPenetrationBlunt);
        mass = Mathf.Max(0.001f, mass);

        // Apply mass.
        Ammo.SetStatBaseValue(StatDefOf.Mass, mass);

        // Rate of fire gets special treatment.
        foreach (var p in Parts)
        {
            var mod = p.Value.TryGetModFor(p.Key);
            if (mod.rateOfFire == null || mod.rateOfFire.Coefficient == 1f)
                continue;

            if (mod.rateOfFire.Offset != 0)
                Core.Error("RateOfFire offset should not be used!");

            AddStatMod(new StatMod()
            {
                BulletPart = p.Key,
                Material = p.Value,
                Mod = new BulletPartMod.ModData
                {
                    Coefficient = 1f / mod.rateOfFire.Coefficient,
                    StatDefName = "TicksBetweenBurstShots"
                }
            });
        }

        // Stats:
        foreach (var statMod in GetStatMods())
            AddStatMod(statMod);

        Ammo.modExtensions ??= new List<DefModExtension>();
        Ammo.modExtensions.Add(ext);
        Ammo.tradeability = Tradeability.None;

        if (!register)
        {
            UpdateLabels();
            return;
        }

        // Ammo category.
        Category = new AmmoCategoryDef
        {
            defName = Ammo.defName + "_cat",
            label = Label,
            labelShort = Designation,
            description = $"Autogenerated ammo type for custom ammo '{Label}'",
            modContentPack = Core.ContentPack,
            generated = true,
            descriptionHyperlinks = new List<DefHyperlink>() { Ammo }
        };

        DefDatabase<AmmoCategoryDef>.Add(Category);
        Ammo.ammoClass = Category;

        // Secondary damages:
        proj.secondaryDamage ??= new List<SecondaryDamage>();
        proj.secondaryDamage.AddRange(AllSecondaryDamages());

        // Recipe.
        Recipe = TemplateRecipe.GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(TemplateRecipe, Array.Empty<object>()) as RecipeDef;

        Recipe.ingredients = CloneRecipeIngredients(Recipe.ingredients);
        Recipe.modContentPack = Core.ContentPack;
        Recipe.defName = Ammo.defName + "_recipe";
        Recipe.cachedLabelCap = null;
        Recipe.generated = true;
        Recipe.workAmount *= 1f + Parts.Count * 0.1f;

        var oldProducts = Recipe.products;
        Recipe.products = new List<ThingDefCountClass>();
        foreach (var old in oldProducts)
            Recipe.products.Add(new ThingDefCountClass(old.thingDef, old.count));

        Recipe.products.Find(i => i.thingDef == AmmoTemplate).thingDef = Ammo;
        Recipe.premultipliedSmallIngredients = null;

        MergeRecipeIngredients(Recipe.ingredients, Recipe.products[0].count);

        DefDatabase<RecipeDef>.Add(Recipe);

        // Clear ammo bench cache so that the new recipe shows up.
        foreach (var user in Recipe.recipeUsers)
        {
            user.allRecipesCached = null;
        }

        // Descriptions, and other misc.
        UpdateLabels();
        Ammo.description = MakeTechDescription();
    }

    public float GetValueBefore(string id, Thing gun) => GetValue(id, gun, AmmoTemplate, BulletTemplate);

    public float GetValueAfter(string id, Thing gun) => GetValue(id, gun, Ammo, Bullet);

    private float GetValue(string id, Thing gun, AmmoDef ammo, ThingDef bullet)
    {
        return id switch
        {
            "damage" => bullet.projectile.damageAmountBase,
            "speed" => bullet.projectile.speed,
            "apSharp" => (bullet.projectile as ProjectilePropertiesCE).armorPenetrationSharp,
            "apBlunt" => (bullet.projectile as ProjectilePropertiesCE).armorPenetrationBlunt,
            "pelletCount" => (bullet.projectile as ProjectilePropertiesCE).pelletCount,
            "spread" => gun.GetStatValue(CE_StatDefOf.ShotSpread),
            "recoil" => gun.GetStatValue(CE_StatDefOf.Recoil),
            "muzzleFlash" => gun.GetStatValue(CE_StatDefOf.MuzzleFlash),
            "rateOfFire" => 3600f / gun.GetStatValue(CE_StatDefOf.TicksBetweenBurstShots),
            "burstShotCount" => gun.GetStatValue(CE_StatDefOf.BurstShotCount),
            "mass" => ammo.GetStatValueAbstract(StatDefOf.Mass),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };
    }

    public BulletPart GetDisabledParts()
    {
        BulletPart mask = 0;
        foreach (var pair in Parts)
        {
            var mod = pair.Value.TryGetModFor(pair.Key);
            mask |= mod.disables;
        }
        return mask;
    }
}