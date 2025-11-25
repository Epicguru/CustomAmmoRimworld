using CombatExtended;
using CustomLoads.Bullet;
using EpicUtils;
using JetBrains.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using UnityEngine;
using Verse;

namespace CustomLoads.UI;

[StaticConstructorOnStartup]
public class Window_CustomLoadEditor : Window
{
    public static readonly Texture2D BulletTexture = ContentFinder<Texture2D>.Get("CustomLoads/BulletIcon");

    private static readonly Texture2D gunpowderTexture = ContentFinder<Texture2D>.Get("CustomLoads/GunpowderIcon");
    private static readonly Texture2D lockTexture = ContentFinder<Texture2D>.Get("CustomLoads/LockIcon");
    private static HashSet<AmmoSetDef> allowedAmmoSetDefs;
    private static List<MenuItemBase> ammoSetMenuItems;
    private static AmmoCategoryDef fmjDef;

    public static void Init()
    {
        allowedAmmoSetDefs = new HashSet<AmmoSetDef>();
        foreach (var ammo in DefDatabase<AmmoSetDef>.AllDefsListForReading)
        {
            // menuHidden means that it is disabled.
            // because no guns use it.
            if (ammo.ammoTypes[0].ammo.menuHidden)
                continue;

            // No mortar stuff, thanks.
            if (ammo.isMortarAmmoSet)
                continue;

            // Must have an FMJ type.
            if (TryGetFMJ(ammo, out _) == null)
                continue;

            // TODO filter out crap like arrows, grenades.
            allowedAmmoSetDefs.Add(ammo);
        }

        ammoSetMenuItems = new List<MenuItemBase>(allowedAmmoSetDefs.Count);
        foreach (var set in allowedAmmoSetDefs)
        {
            string label = set.LabelCap;
            var icon = set.ammoTypes[0].ammo.IconTexture();
            string tooltip = $"Guns that use this:\n{string.Join("\n", set.ammoTypes.SelectMany(l => l.ammo.Users).Distinct().Select(t => t.LabelCap.ToString()))}";

            var item = new MenuItemText(set, label, icon, tooltip: tooltip);
            ammoSetMenuItems.Add(item);
        }
    }

    private static AmmoDef TryGetFMJ(AmmoSetDef ammoSet, out ThingDef bullet)
    {
        fmjDef ??= DefDatabase<AmmoCategoryDef>.GetNamed("FullMetalJacket");

        foreach (var type in ammoSet.ammoTypes)
        {
            if (type.ammo.ammoClass == fmjDef)
            {
                bullet = type.projectile;
                return type.ammo;
            }
        }

        bullet = null;
        return null;
    }

    [UsedImplicitly]
    [DebugAction("CustomLoads", "Open Ammo Editor", allowedGameStates = AllowedGameStates.Playing)]
    private static void OpenDebug()
    {
        var window = Open();
        window.CustomLoads = Core.Settings.CustomAmmo;
    }

    public static Window_CustomLoadEditor Open()
    {
        var window = new Window_CustomLoadEditor();
        Find.WindowStack.Add(window);
        window.CustomLoads = Core.Settings.CustomAmmo;
        return window;
    }

    public override Vector2 InitialSize => new(1600f, 880f);

    public CustomLoad Editing
    {
        get => next ?? current;
        set
        {
            next = value;
            if (value != null && !CustomLoads.Contains(value))
                CustomLoads.Add(value);
        }
    }

    public List<CustomLoad> CustomLoads = new();
    public UIAnimationDef Animation;

    private readonly HashSet<BulletPart> drawnParts = new();
    private ThingDef currentAmmoGun;
    private CustomLoad current;
    private CustomLoad next;
    private CustomLoad drawing;
    private Rect activeBounds;
    private Vector2 scroll, scroll2, scroll3;
    private float time;

    public Window_CustomLoadEditor()
    {
        onlyOneOfTypeAllowed = true;
        doCloseX = true;
        shadowAlpha = 1f;
        forcePause = false;
        draggable = true;
        drawShadow = true;
        preventCameraMotion = false;
        doCloseButton = false;
    }

    [Pure]
    private static float Ease(float t)
    {
        const float C5 = (float)(2 * Math.PI) / 4.5f;

        t = Mathf.Clamp01(t);
        return t == 0
            ? 0
            : t == 1f
                ? 1
                : t < 0.5
                    ? -(Mathf.Pow(2, 20 * t - 10) * Mathf.Sin((20 * t - 11.125f) * C5)) / 2
                    : (Mathf.Pow(2, -20 * t + 10) * Mathf.Sin((20 * t - 11.125f) * C5)) / 2 + 1;
    }

    public void OpenMaterialSelect(BulletPart part, BulletMaterialDef currentMaterial)
    {
        var rawItems = DefDatabase<BulletMaterialDef>.AllDefsListForReading.Concat((BulletMaterialDef)null).Where(m => m != currentMaterial && (m?.CanApplyTo(part) ?? true));
        var items = BetterFloatMenu.MakeItems(rawItems, m =>
        {
            string label = m?.TechLabel.CapitalizeFirst() ?? "Default material";
            string tooltip = m?.TryGetModFor(part).Description ?? "The standard material this part is made out of. No extra cost, no special effects.";
            if (string.IsNullOrWhiteSpace(tooltip))
                tooltip = "<i>No special effect</i>";

            return new MenuItemText(m, label, m?.Icon, tooltip: tooltip);
        });

        var matSelect = BetterFloatMenu.Open(items, mi =>
        {
            var load = mi.GetPayload<BulletMaterialDef>();
            if (load != null)
                Editing.Parts[part] = load;
            else if (Editing.Parts.ContainsKey(part))
                Editing.Parts.Remove(part);
        });

        matSelect.optionalTitle = $"Select material for the {part.Label()}";
    }

    private void PreDrawPart(UIAnimationDef.AnimatedPart part, UIAnimationDef.Keyframe key)
    {
        if (part.ID == null)
            return;

        if (!Enum.TryParse<BulletPart>(part.ID, out var bp))
            return;

        if (drawing.GetDisabledParts().HasFlag(bp))
        {
            key.PreventDraw = true;
            if (drawing.Parts.ContainsKey(bp))
            {
                drawing.Parts.Remove(bp);
            }
            return;
        }

        // Gunpowder readout above casing.
        if (bp == BulletPart.Casing)
        {
            Text.Font = GameFont.Medium;

            var iconPos = new Rect(key.pos + new Vector2(part.Texture.width * 0.5f - 16f, -80f), new Vector2(32, 32));
            float load = drawing.PowderLoad / 4f + 1f;
            string txt = $"Gunpowder load: {load:P0}";
            iconPos.x -= 100;

            Color blue = new Color32(90, 148, 219, 255);
            Color green = new Color32(173, 227, 170, 255);
            Color red = new Color32(245, 113, 113, 255);

            Color gpColor = load < 1f ? Color.Lerp(blue, green, load) : Color.Lerp(green, red, load - 1f);
            gpColor.a = Mathf.Clamp01((key.time - 0.5f) / 0.5f);
            GUI.color = gpColor;
            GUI.DrawTexture(iconPos, gunpowderTexture);

            var gpPos = iconPos;
            gpPos.x += 38;
            gpPos.width = 230;
            gpPos.height = 100;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((key.time - 0.5f) / 0.5f));
            Widgets.Label(gpPos, txt);

            var slider = new Rect(iconPos.position + new Vector2(0, 38), new Vector2(iconPos.width + 220, 12));

            Widgets.DrawBoxSolid(slider.ExpandedBy(6, 3), Color.white * 0.45f * Mathf.Clamp01((key.time - 0.5f) / 0.5f));

            if (Mathf.RoundToInt(drawing.PowderSliderFloat) != drawing.PowderLoad)
                drawing.PowderSliderFloat = drawing.PowderLoad;

            drawing.PowderSliderFloat = Widgets.HorizontalSlider(slider, drawing.PowderSliderFloat, -3, 4, roundTo: 1);
            int updated = Mathf.RoundToInt(drawing.PowderSliderFloat);
            if (updated != drawing.PowderLoad && !drawing.IsLocked)
                drawing.PowderLoad = updated;

            key.PostDraw = delegate
            {
                float txtA = Mathf.Clamp01((0.3f - key.time) / 0.3f);
                GUI.color = new Color(0, 0, 0, txtA);
                string desc = $"<b>'{drawing.LabelCap}'\n{drawing.Designation}\n<i>{drawing.AmmoTemplate?.AmmoSetDefs[0]?.LabelCap}</i></b>";
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(key.pos + new Vector2(-130, 16), new Vector2(part.Texture.width, part.Texture.height)), desc);
                Text.Anchor = TextAnchor.UpperLeft;
            };
        }

        drawing.Parts.TryGetValue(bp, out var mat);

        if (mat != null)
            key.color *= mat.Tint;
        else
            key.color *= bp <= BulletPart.BulletTip ? new Color32(255, 167, 36, 255) : new Color32(227, 190, 98, 255);

        float a = Mathf.Clamp01((key.time - 0.75f) / 0.25f);

        if (part.texture == "CustomLoads/BulletBG")
            return;

        var mod = mat?.TryGetModFor(bp);
        var tex = part.Texture;
        if (mod != null && mod.overrideTexture != null)
        {
            tex = mod.OverrideTexture;
            key.OverrideTexture = tex;
        }

        if (a == 0f  || !drawnParts.Add(bp))
            return;

        bool drawAbove = bp switch
        {
            BulletPart.BulletCore => true,
            BulletPart.BulletJacket => false,
            BulletPart.BulletTip => false,
            BulletPart.Casing => false,
            BulletPart.Primer => true,
            BulletPart.Powder => true,
            _ => throw new ArgumentOutOfRangeException()
        };

        Rect pos = new(key.pos + new Vector2(10, !drawAbove ? tex.height + 10 : -40), new Vector2(300, 100));

        Text.Font = GameFont.Medium;
        string title = mat != null ? $"{mat.TechLabel.CapitalizeFirst()} {bp.Label()}" : $"{bp.Label().CapitalizeFirst()} (default)";

        var size = Text.CalcSize(title);
        float x = key.pos.x + tex.width * 0.5f - size.x * 0.5f;
        pos.x = x;

        var iconRect = pos;
        iconRect.x -= 28;
        iconRect.y += 2;
        iconRect.width = 24;
        iconRect.height = 24;

        var fade = new Color(1, 1, 1, a);

        if (mat != null)
            Widgets.DefIcon(iconRect, mat.material, color: fade);

        GUI.color = fade;

        if (key.time > 0.75f)
        {
            var clickArea = new Rect(key.pos, new Vector2(tex.width, tex.height)).ExpandedBy(10);
            Widgets.DrawHighlightIfMouseover(clickArea);

            if (Widgets.ButtonInvisible(clickArea))
            {
                if (drawing.IsLocked)
                {
                    Messages.Message("This ammo has already been created, so it cannot be changed!",
                        MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    OpenMaterialSelect(bp, mat);
                }
            }
        }

        Widgets.Label(pos, title);

        if (mat == null)
            return;

        string desc = mod.Summary;
        if (string.IsNullOrWhiteSpace(desc))
            desc = "<i>No effect</i>";
        desc = desc.Trim();

        size = Text.CalcSize(desc);
        var descPos = pos.position;
        descPos.x += size.x * -0.5f + Text.CalcSize(title).x * 0.5f - 12f;
        descPos.y += drawAbove ? -size.y - 8 : 38;
        var descRect = new Rect(descPos, size);
        Widgets.DrawBoxSolidWithOutline(descRect.ExpandedBy(5), new Color(0, 0, 0, a * 0.4f), new Color(1, 1, 1, a * 0.5f));
        Widgets.Label(descRect, desc);
    }

    private void UpdateTime()
    {
        if (current is { IsErrored: true })
            current = null;

        if (current == null)
        {
            // Attempt to just grab next.
            if (next != null)
            {
                current = next;
                next = null;
                time = -1f;
            }
            else if (CustomLoads.Count > 0)
            {
                // Take first custom.
                current = CustomLoads[0];
                time = -1f;
            }
            else
            {
                return;
            }
        }

        const float SPEED = 1.35f;

        if (next != null)
        {
            if (next == current)
            {
                // Nothing to do.
                next = null;
            }
            else
            {
                // Begin reducing time until it is -1.
                time -= Time.deltaTime * SPEED * (time < 0f ? 2.5f : 1f);
                if (time <= -1f)
                {
                    // Done exiting, begin transition back in by setting current.
                    current = next;
                    next = null;
                }
            }
        }
        else
        {
            // Next is null so just pull current into the target position.
            time += Time.deltaTime * SPEED * (time < 0f ? 2.5f : 1f);
        }
    }

    private ThingDef GetCurrentAmmoGun()
    {
        return current.Ammo.Users.FirstOrDefault();
    }

    private void SelectNewGun()
    {
        var options = current.Ammo.Users.Except(currentAmmoGun);
        var items = BetterFloatMenu.MakeItems(options, gun => new MenuItemText(gun, gun.LabelCap, gun.IconTexture(), tooltip: gun.description));

        var menu = BetterFloatMenu.Open(items, item =>
        {
            var gun = item.GetPayload<ThingDef>();

            // Check because menu can stay open even after ammo is changed.
            if (current.Ammo.Users.Contains(gun))
                currentAmmoGun = gun;
        });

        menu.optionalTitle = $"Select {current.Ammo.AmmoSetDefs[0].LabelCap} gun to preview";
    }

    private void ClickedCreateNew()
    {
        var menu = BetterFloatMenu.Open(ammoSetMenuItems, item =>
        {
            var set = item.GetPayload<AmmoSetDef>();
            var fmj = TryGetFMJ(set, out var bullet);

            // Should never happen, sanity check.
            if (fmj == null || bullet == null)
            {
                Core.Error($"Failed to get FMJ from {set}");
                return;
            }

            var load = new CustomLoad
            {
                DefName = $"CustomAmmo_{Guid.NewGuid().ToString().Replace('d', '_')}", // Don't ask why replacing 'd' is necessary. It is.
                AmmoTemplate = fmj,
                BulletTemplate = bullet
            };

            load.GenerateDefs(load.AmmoTemplate, load.BulletTemplate, false);

            CustomLoads.Add(load);
            next = load;

            Core.Log($"Created new custom ammo based on {fmj}: {load.DefName}");

        });
        menu.optionalTitle = "Select caliber for new ammo";
    }

    public static string GetErrorPreCreate(CustomLoad ammo)
    {
        if (string.IsNullOrWhiteSpace(ammo.Label))
            return "You must provide a name for this ammo!";

        if (string.IsNullOrWhiteSpace(ammo.Designation))
            return "You must provide a designation for this ammo! Should be between 1 and 3 letters.";

        var existing = new HashSet<string>();
        foreach (var set in ammo.AmmoTemplate.AmmoSetDefs)
        {
            foreach (var link in set.ammoTypes)
            {
                if (link.ammo.ammoClass != null)
                {
                    existing.Add(link.ammo.ammoClass.labelShort.ToLower());
                }
            }
        }

        if (existing.Contains(ammo.Designation.ToLower()))
            return $"Ammo designation '{ammo.Designation}' already exists in {ammo.AmmoTemplate.AmmoSetDefs[0].LabelCap}, you must create a new designation.";

        return null;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Animation ??= DefDatabase<UIAnimationDef>.AllDefsListForReading[0];
        drawnParts.Clear();

        if (Event.current.type == EventType.Repaint || current is { IsErrored: true })
            UpdateTime();

        time = Mathf.Clamp(time, -1f, 1f);
        Text.Font = GameFont.Small;

        #region Stats box outline
        var statsBox = inRect;
        statsBox.xMin += 450;
        statsBox.yMin += 580;
        statsBox.width *= 0.4f;
        if (current != null)
        {
            current.Refresh();
            Widgets.DrawBox(statsBox);
        }
        #endregion

        if (current != null)
        {
            #region Edit Name
            // Rename.
            Rect rename = statsBox;
            rename.x -= 105;
            rename.height = 24;
            rename.width = 100;
            Widgets.Label(rename, new GUIContent("Name:", "The display name of this ammo."));
            rename.y += 20;
            current.Label = Widgets.TextField(rename, current.Label);
            #endregion

            #region Edit Designation
            rename.y += 32;
            Widgets.Label(rename, new GUIContent("Designation:", "The designation of the bullet, such as FMJ, AP etc.\n" +
                                                                 "The designation should be 1-3 letters long and should not be the same as any existing designations of the same caliber (such as AP, HP)."));
            rename.y += 20;
            current.Designation = Widgets.TextField(rename, current.Designation);
            current.Designation = current.Designation.ToUpper();
            if (current.Designation.Length > 3)
                current.Designation = current.Designation.Substring(0, 3);

            rename.y += 52;
            rename.height += 10;
            current.UpdateLabels();
            #endregion

            #region Submit
            if (!current.IsLocked)
            {
                GUI.color = Color.Lerp(Color.green, Color.white, 0.4f);
                bool create = Widgets.ButtonText(rename, "<color=white>Submit</color>");
                GUI.color = Color.white;

                if (create)
                {
                    string error = GetErrorPreCreate(current);
                    if (error == null)
                    {
                        current.GenerateDefs(current.AmmoTemplate, current.BulletTemplate, true);
                        current.IsLocked = true;
                        Messages.Message($"Created new ammo: '{current.LabelCap}'! You can now craft and use it.", MessageTypeDefOf.PositiveEvent, false);
                    }
                    else
                    {
                        Messages.Message($"Cannot create ammo: {error}", MessageTypeDefOf.RejectInput, false);
                    }
                }
                rename.y += 40;
            }
            #endregion

            #region Delete
            GUI.color = Color.Lerp(Color.red, Color.white, 0.5f);
            bool delete = Widgets.ButtonText(rename, "<color=white>Delete...</color>");
            GUI.color = Color.white;

            if (delete)
            {
                if (!current.IsLocked)
                {
                    var old = current;
                    CustomLoads.Remove(current);
                    current = null;
                    next = null;
                    if (CustomLoads.Count > 0)
                        next = CustomLoads[0];
                    Messages.Message($"Deleted '{old.Label}'. No save games were affected.", MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    var old = current;
                    CustomLoads.Remove(current);
                    current = null;
                    next = null;
                    if (CustomLoads.Count > 0)
                        next = CustomLoads[0];
                    Messages.Message($"Deleted '{old.Label}'. Your save games may have been affected.", MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                Messages.Message("Deleting custom ammo may damage or break any save game that uses it.\nPlease back up any saves that have used this ammo.\n<b>Hold SHIFT and click the delete button to confirm and delete.</b>",MessageTypeDefOf.RejectInput, false);
            }
            #endregion

            #region Locked warning
            if (current.IsLocked)
            {
                rename.y = inRect.yMax - rename.width;
                rename.yMax = inRect.yMax;
                rename = rename.ExpandedBy(-10);
                GUI.color = Color.yellow;
                Widgets.DrawTextureFitted(rename.ExpandedBy(-10), lockTexture, 1f);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(rename, "This ammo has been created and can now be used in-game.\n" +
                                                 "This means that you can no longer change it's properties.\n" +
                                                 "You can change it's name and designation though, but you should restart the game after you do so.");
            }
            #endregion

            // Description and title
            Widgets.LabelScrollable(statsBox.ExpandedBy(-6, -2), current.TechnicalDescription, ref scroll);
            Widgets.Label(inRect with { x = 350 }, "<b><size=44>Edit Custom Ammo</size></b>");

            #region Gun & ammo stat bars
            var bars = statsBox;
            bars.width *= 2.3f;
            bars.xMin += statsBox.width + 10;
            bars.xMax -= 100;
            Widgets.DrawBox(bars);
            bars = bars.ExpandedBy(-2);

            var oldAmmo = current.Ammo;
            var oldBullet = current.Bullet;
            current.GenerateDefs(current.AmmoTemplate, current.BulletTemplate, false);

            if (currentAmmoGun == null || !current.Ammo.Users.Contains(currentAmmoGun))
                currentAmmoGun = GetCurrentAmmoGun();

            Patches.Patch_ThingIDMaker_GiveIDTo.Active = true;
            var gunInstance = ThingMaker.MakeThing(currentAmmoGun);
            var gunInstance2 = ThingMaker.MakeThing(currentAmmoGun);
            Patches.Patch_ThingIDMaker_GiveIDTo.Active = false;

            var comp = gunInstance.TryGetComp<CompAmmoUser>();
            comp.ResetAmmoCount(current.Ammo);

            Rect header = bars.ExpandedBy(-6);
            header.height = 28;
            string txt = "When fired out of a ";
            var txtSize = Text.CalcSize(txt);
            Widgets.Label(header, txt);
            header.x += txtSize.x + 5;
            header.width = 300;
            header.height -= 4;
            Widgets.DefLabelWithIcon(header, currentAmmoGun);
            if (Widgets.ButtonInvisible(header))
                SelectNewGun();

            bars.yMin += 30;

            int j = 0;

            float height = current.MergedMods.Sum(m => m?.ID != null ? 1 : 0) * 34f;
            Widgets.BeginScrollView(bars, ref scroll2, new Rect(0, 0, bars.width - 16, height));

            foreach (BulletPartMod.ModData mod in current.MergedMods)
            {
                if (mod?.ID == null)
                    continue;

                float before = current.GetValueBefore(mod.ID, gunInstance2);
                float after = current.GetValueAfter(mod.ID, gunInstance);

                var bar = bars;
                bar.x = 0;
                bar.y = 34 * j;
                bar.height = 36;
                bar.width -= 12;
                bar = bar.ExpandedBy(-5);

                Widgets.DrawBoxSolidWithOutline(bar, Color.grey * 0.7f, Color.white);
                Color red = new Color32(199, 56, 80, 255);
                Color green = new Color32(39, 143, 61, 255);

                float beforePct = Mathf.InverseLerp(mod.Range.min, mod.Range.max, before);
                float afterPct = Mathf.InverseLerp(mod.Range.min, mod.Range.max, after);

                if (afterPct > beforePct)
                    Widgets.DrawBoxSolid(bar.ExpandedBy(-4).LeftPart(afterPct), mod.IsGood(afterPct - beforePct) ? green : red);
                Widgets.DrawBoxSolid(bar.ExpandedBy(-4).LeftPart(beforePct), Color.grey);
                if (afterPct < beforePct)
                    Widgets.DrawBoxSolid(bar.ExpandedBy(-4).LeftPart(beforePct).RightPart(1f - afterPct / beforePct), mod.IsGood(afterPct - beforePct) ? green : red);

                bar.x += 6;
                bar.y += 3;

                Widgets.Label(bar, $"<b><color=white>{mod.Label}: {before.ToString(mod.FormatString)} -> {after.ToString(mod.FormatString)}{mod.OffsetUnit}</color></b>");

                j++;
            }

            Widgets.EndScrollView();

            current.Ammo = oldAmmo;
            current.Bullet = oldBullet;

            // Causes errors when not in-game, doesn't actually clean up anything.
            if (Current.Game != null)
            {
                gunInstance.Destroy();
                gunInstance2.Destroy();
            }
            #endregion

            #region Material cost
            bars.yMin -= 32;
            bars.yMax += 2;
            bars.xMax += 185;
            bars.xMin += 475;
            Widgets.DrawBox(bars);
            bars = bars.ExpandedBy(-2);
            Text.Anchor = TextAnchor.UpperCenter;
            var recipe = current.TemplateRecipe;
            Widgets.Label(bars, $"Cost to make {recipe.products[0].count}:");
            Text.Anchor = TextAnchor.UpperLeft;

            var ingredients = CustomLoad.CloneRecipeIngredients(recipe.ingredients);
            current.MergeRecipeIngredients(ingredients, recipe.products[0].count);

            for (int k = 0; k < ingredients.Count; k++)
            {
                var item = ingredients[k];
                var pos = new Rect(bars.x + 4, bars.y + k * 24 + 30, bars.width - 4, 30);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(pos, $"{item.count*(item.filter.AllowedThingDefs.First().smallVolume ? ThingDef.SmallUnitPerVolume : 1f)}x");
                Text.Anchor = TextAnchor.UpperLeft;
                pos.xMin += 30;
                if (item.filter.AllowedThingDefs.Count() == 1)
                    Widgets.DefLabelWithIcon(pos, item.filter.AllowedThingDefs.First());
                else
                    Widgets.Label(pos, item.filter.Summary);
            }
            #endregion
        }

        // Create new custom ammo button.
        if (Widgets.ButtonText(inRect with { width = 200, height = 40 }, "<b>Create new...</b>"))
            ClickedCreateNew();

        #region Draw inactive bullets
        Rect leftScroll = inRect;
        leftScroll.yMin += 50;
        leftScroll.width = 340;
        Widgets.DrawBox(leftScroll);
        leftScroll = leftScroll.ExpandedBy(-3);
        float magHeight = 150f * CustomLoads.Count + 30;
        Widgets.BeginScrollView(leftScroll, ref scroll3, new Rect(0, 0, leftScroll.width - 16, magHeight));

        int i = -1;
        foreach (var ammo in CustomLoads)
        {
            i++;
            if (ammo == current)
                continue;

            drawing = ammo;
            var idlePos = new Vector2(-345, 30 + i * 150);

            var clickMe = new Rect(idlePos, new Vector2(750f, 138f)).ExpandedBy(6);

            if (ammo.IsErrored)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(clickMe, $"<color=red><b>Ammo '{ammo.Label}' (missing mod?)</b></color>");
                Text.Anchor = TextAnchor.UpperLeft;
                continue;
            }

            Widgets.DrawHighlightIfMouseover(clickMe);
            bool over = false;
            if (Mouse.IsOver(clickMe))
            {
                idlePos.x += 220;
                over = true;
            }
            if (Widgets.ButtonInvisible(clickMe))
            {
                Editing = ammo;
            }

            Animation.Draw(idlePos, 0f, PreDrawPart, over ? null : new Color(0.75f, 0.75f, 0.75f, 0.5f));
        }

        Widgets.EndScrollView();

        #endregion

        if (current == null)
            return;

        #region Draw active ammo
        var idlePos2 = new Vector2(-345, 30 + CustomLoads.IndexOf(current) * 150 - scroll3.y + 50);
        drawing = current;
        var bounds = activeBounds;
        var w = bounds.width;
        var h = bounds.height;

        float x = inRect.width * 0.61f - w * 0.5f;
        float y = inRect.height * 0.37f - h * 0.5f;

        if (time >= 0f)
        {
            float t = Ease(time);

            GUI.color = new Color(1, 1, 1, 0.35f * time);
            var textColor = new Color(1, 1, 1, Mathf.Clamp01((time - 0.5f) / 0.5f));
            var textColorHex = ColorUtility.ToHtmlStringRGBA(textColor);
            GUI.Box(new Rect(x, y - 100, w, h + 100).ExpandedBy(5), $"<color=#{textColorHex}>{drawing.AmmoTemplate?.AmmoSetDefs[0]?.LabelCap ?? "???"} '{drawing.LabelCap}'</color>");

            Animation.Draw(new Vector2(x - bounds.x, y - bounds.y), t, PreDrawPart);
            activeBounds = Animation.Bounds;
        }
        else
        {
            Vector2 pos;
            pos = time > -0.5f ?
                new Vector2(Mathf.Lerp(x - bounds.x, -250, -time * 2f), y - bounds.y) :
                Vector2.Lerp(idlePos2, new Vector2(-250, y - bounds.y), 1f - (time + 0.5f) * -2f);

            Color tint = Color.Lerp(Color.white, new Color(0.75f, 0.75f, 0.75f, 0.5f), -time);
            Animation.Draw(pos, 0f, PreDrawPart, tint);
        }

        drawing = null;
        #endregion
    }

    public override void PreClose()
    {
        base.PreClose();
        Core.Instance.WriteSettings();
    }
}