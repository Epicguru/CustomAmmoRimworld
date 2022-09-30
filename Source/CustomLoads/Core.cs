using CombatExtended;
using CustomLoads.UI;
using CustomLoads.Utils;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace CustomLoads;

[HotSwapAll]
public class Core : Mod
{
    public static Core Instance { get; private set; }
    public static ModContentPack ContentPack { get; private set; }
    public static Settings Settings { get; private set; }

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
        Instance = this;
        //Log("Hello, world!");

        new Harmony(content.PackageId).PatchAll();
        ContentPack = content;

        LongEventHandler.QueueLongEvent(Window_CustomLoadEditor.Init, "Custom Loads: Init", false, null);
        LongEventHandler.QueueLongEvent(GenerateAmmo, "Custom Loads: Generate Ammo", false, null);
    }

    private void GenerateAmmo()
    {
        try
        {
            Settings = GetSettings<Settings>();
        }
        catch (Exception e)
        {
            Error("Failed to load settings - this is probably because of a missing mod.", e);
        }

        foreach (var load in Settings.CustomAmmo)
        {
            if (load == null)
            {
                Error("Loaded null custom ammo. This will result in loss of the ammo, and possibly broken saves. Please report, including details about what mods you added or removed.");
                continue;
            }

            if (load.IsErrored)
            {
                Warn($"Custom ammo '{load.Label}' failed to load - this is probably because of a missing mod.");
                continue;
            }

            load.GenerateDefs(load.AmmoTemplate, load.BulletTemplate, load.IsLocked);
            if (load.IsLocked)
                Log($"Generated ammo, bullet, recipe for '{load.Label}'");
        }
    }

    public override string SettingsCategory()
    {
        return ContentPack.Name;
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        Settings?.Draw(inRect);
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
