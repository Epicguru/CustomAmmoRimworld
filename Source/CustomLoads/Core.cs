using CombatExtended;
using CustomLoads.Utils;
using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using CustomLoads.Bullet;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using EpicUtils;
using HarmonyLib;

namespace CustomLoads;

[HotSwapAll]
public class Core : Mod
{
    public static ModContentPack ContentPack { get; private set; }

    internal static void Log(string message)
    {
        Verse.Log.Message($"<color=#8595ff>[CustomLoads]</color> {message ?? "<null>"}");
    }

    internal static void Warn(string message)
    {
        Verse.Log.Warning($"<color=#8595ff>[CustomLoads]</color> {message ?? "<null>"}");
    }

    internal static void Error(string message, Exception e = null)
    {
        Verse.Log.Error($"<color=#8595ff>[CustomLoads]</color> {message ?? "<null>"}");
        if (e != null)
            Verse.Log.Error(e.ToString());
    }

    public Core(ModContentPack content) : base(content)
    {
        Log("Hello, world!");

        new Harmony(content.PackageId).PatchAll();
        ContentPack = content;
    }

    private static FieldInfo[] ammoDefFields;

    public static AmmoDef Clone(AmmoDef original)
    {
        if (original == null)
            return null;

        ammoDefFields ??= typeof(AmmoDef).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var created = new AmmoDef();

        foreach (var field in ammoDefFields)
        {
            var value = field.GetValue(original);
            field.SetValue(created, value);
        }

        return created;
    }

    public static ThingDef Clone(ThingDef original)
    {
        if (original == null)
            return null;

        ThingDef created = original.GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(original, Array.Empty<object>()) as ThingDef;

        // Special clone projectile properties.
        var cloneMethod = original.projectile.GetType().GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
        var proj = cloneMethod.Invoke(original.projectile, Array.Empty<object>()) as ProjectilePropertiesCE;

        if (original.projectile is ProjectilePropertiesCE { secondaryDamage: { } } pce)
        {
            var newList = pce.secondaryDamage.Select(item => new SecondaryDamage()
            {
                def = item.def,
                amount = item.amount,
                chance = item.chance

            }).ToList();

            proj.secondaryDamage = newList;
        }

        created.projectile = proj;
        return created;
    }
}

public class MyGameComp : GameComponent
{
    private string name = "CUSTOM AMMO";
    private int i;
    private CustomLoad load = new CustomLoad();

    private AmmoDef baseAmmo;
    private ThingDef baseBullet;

    public MyGameComp(Game _)
    {
    }

    public override void GameComponentOnGUI()
    {
        base.GameComponentOnGUI();

        name = GUILayout.TextField(name, GUILayout.MinWidth(100));

        var materials = DefDatabase<BulletMaterialDef>.AllDefsListForReading;

        foreach (BulletPart e in Enum.GetValues(typeof(BulletPart)))
        {
            BulletMaterialDef value = load.Parts.GetValueOrDefault(e);

            if (GUILayout.Button($"[{e}]: {value?.TechLabel ?? "<None>"}"))
            {
                var elem = materials.Where(m => m.CanApplyTo(e)).RandomElementWithFallback();
                if (elem != null)
                    load.Parts[e] = elem;
            }

            if (value != null)
            {
                var desc = value.TryGetModFor(e).Description;
                GUILayout.Label(desc);
            }
        }

        if (load.Ammo != null)
        {
            GUILayout.Space(20);
            GUILayout.Label(load.TechnicalDescription);
            GUILayout.Space(20);
        }

        if (GUILayout.Button("Select Base Bullet"))
        {
            var rawItems = DefDatabase<AmmoSetDef>.AllDefsListForReading.Where(s => !s.ammoTypes[0].ammo.menuHidden);
            var items = BetterFloatMenu.MakeItems(rawItems, r => new MenuItemText(r, r.LabelCap, r.ammoTypes[0].ammo.IconTexture()));
            BetterFloatMenu.Open(items, sel =>
            {
                var set = sel.GetPayload<AmmoSetDef>();

                var newItems = BetterFloatMenu.MakeItems(set.ammoTypes,
                    link => new MenuItemText(link, link.ammo.LabelCap, link.ammo.IconTexture()));

                BetterFloatMenu.Open(newItems, link =>
                {
                    var pl = link.GetPayload<AmmoLink>();
                    baseAmmo = pl.ammo;
                    baseBullet = pl.projectile;

                }).optionalTitle = "Select type to inherit from";

            }).optionalTitle = "Select Caliber";
        }

        if (baseAmmo == null || baseBullet == null || !GUILayout.Button("Create!"))
            return;

        load.GenerateDefs(baseAmmo, baseBullet, $"{baseAmmo.defName}_custom{i++}");

        load.Ammo.label = name;
        load.Bullet.label = $"{name} bullet";

        Core.Log($"Generated {load.Ammo} based on {baseAmmo}");
    }
}