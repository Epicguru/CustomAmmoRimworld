using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;
using Verse;

namespace CustomLoads;

public class UIAnimationDef : Def
{
    [XmlIgnore]
    public Rect Bounds;

    public List<AnimatedPart> parts = new List<AnimatedPart>();

    public void Draw(Vector2 pos, float time, Action<AnimatedPart, Keyframe> preDraw = null, Color? tint = null)
    {
        Bounds = default;

        foreach (var item in parts)
        {
            var tex = item.Texture;
            if (tex == null)
                continue;

            var frame = item.Sample(time);
            frame.pos += pos + item.offset;

            preDraw?.Invoke(item, frame);
            tex = frame.OverrideTexture ?? tex;
            frame.OverrideTexture = null;
            if (frame.PreventDraw)
            {
                frame.PreventDraw = false;
                continue;
            }

            Rect rect = new Rect(frame.pos, new Vector2(tex.width, tex.height));
            GUI.color = frame.color;
            if (tint != null)
                GUI.color *= tint.Value;
            GUI.DrawTexture(rect, tex);

            if (frame.PostDraw != null)
            {
                frame.PostDraw();
                frame.PostDraw = null;
            }

            rect.position -= pos;

            if (Bounds.x > rect.x)
                Bounds.x = rect.x;
            if (Bounds.y > rect.y)
                Bounds.y = rect.y;
            if (Bounds.xMax < rect.xMax)
                Bounds.xMax = rect.xMax;
            if (Bounds.yMax < rect.yMax)
                Bounds.yMax = rect.yMax;

        }
        GUI.color = Color.white;
    }

    public class AnimatedPart
    {
        public Texture2D Texture => textureCached ??= ContentFinder<Texture2D>.Get(texture);

        public string ID;
        public string texture;
        public Vector2 offset;
        public List<Keyframe> keyframes = new List<Keyframe>();


        [XmlIgnore] private readonly Keyframe sample = new Keyframe();
        [XmlIgnore] private Texture2D textureCached;

        public Keyframe Sample(float time)
        {
            if (keyframes.Count == 1)
            {
                sample.CopyFrom(keyframes[0]);
                sample.time = time;
                return sample;
            }

            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                var current = keyframes[i];
                var next = keyframes[i + 1];

                // Check if time is before first keyframe.
                if (i == 0 && time < current.time)
                {
                    float t = (time - current.time) / (next.time - current.time);
                    Keyframe.Lerp(current, next, t, sample);
                    sample.time = time;
                    return sample;
                }

                // Check if time is after last keyframe.
                if (i == keyframes.Count - 2 && time > next.time)
                {
                    float t = (time - current.time) / (next.time - current.time);
                    Keyframe.Lerp(current, next, t, sample);
                    sample.time = time;
                    return sample;
                }

                // Do interpolation between two keyframes.
                if (time >= current.time && time <= next.time)
                {
                    float t = Mathf.InverseLerp(current.time, next.time, time);
                    Keyframe.Lerp(current, next, t, sample);
                    sample.time = time;
                    return sample;
                }
            }

            Core.Error("Animation interpolation error!");
            return null;
        }
    }

    public class Keyframe
    {
        public static void Lerp(Keyframe a, Keyframe b, float t, Keyframe result)
        {
            result.time = Mathf.LerpUnclamped(a.time, b.time, t);
            result.pos = Vector2.LerpUnclamped(a.pos, b.pos, t);
            result.color = Color.Lerp(a.color, b.color, t);
        }

        public Texture2D OverrideTexture { get; set; }
        public bool PreventDraw { get; set; }
        public Action PostDraw { get; set; }

        public float time;
        public Vector2 pos;
        public Color color = Color.white;

        public Keyframe CopyFrom(in Keyframe other)
        {
            time = other.time;
            pos = other.pos;
            color = other.color;
            return this;
        }
    }
}
