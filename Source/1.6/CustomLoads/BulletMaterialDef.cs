using CustomLoads.Bullet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CustomLoads
{
    public class BulletMaterialDef : Def
    {
        private static readonly BulletPart[] allBulletParts = Enum.GetValues(typeof(BulletPart)).Cast<BulletPart>().ToArray();

        public string TechLabel => string.IsNullOrWhiteSpace(label) ? material.label : label;

        public Color Tint => tint ?? material.stuffProps?.color ?? Color.white;
        public Texture2D Icon => material.uiIcon;

        /// <summary>
        /// The material that it's made out of.
        /// </summary>
        public ThingDef material;

        /// <summary>
        /// Additional text description.
        /// </summary>
        public string extraDesc;

        public float costPerBullet = 0.01f;

        public Color? tint;

        public List<BulletPartMod> mods = new List<BulletPartMod>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var error in base.ConfigErrors())
                yield return error;

            if (material == null)
                yield return "Missing material in this BulletMaterialDef";

            if (mods.Count == 0)
                yield return "This BulletMaterialDef has no mods defined.";

            var hs = new HashSet<BulletPart>(8);
            foreach (var mod in mods)
            {
                foreach (var part in allBulletParts)
                {
                    if (!mod.parts.HasFlag(part))
                        continue;

                    if (!hs.Add(part))
                        yield return $"This BulletMaterialDef has multiple mods that are supposed to be applied to {part}! There should be at most 1 mod per bullet part.";
                }
            }
        }

        public BulletPartMod TryGetModFor(BulletPart part)
        {
            foreach (var mod in mods)
            {
                if (mod.parts.HasFlag(part))
                    return mod;
            }

            return null;
        }

        public bool CanApplyTo(BulletPart part)
        {
            foreach (var mod in mods)
                if (mod.parts.HasFlag(part))
                    return true;

            return false;
        }
    }
}
