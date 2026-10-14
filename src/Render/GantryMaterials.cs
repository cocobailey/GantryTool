using System;
using System.Collections.Generic;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Runtime materials. The shader and render layer are borrowed from a loaded vanilla prop that uses
    /// "Custom/Props/Prop/Default", so we never depend on Shader.Find succeeding for a stripped shader.
    ///
    /// Prop shader map packing (AssetImporterTextureLoader.cs:387-417, defaults at :48):
    ///   _XYSMap: R,G = normal X/Y (0.5 = flat), B = 1 - specular.
    ///   _ACIMap: R = 1 - alpha, G = 1 - colour-variation mask, B = illumination.
    /// </summary>
    public static class GantryMaterials
    {
        private const string PropShaderName = "Custom/Props/Prop/Default";

        public static Material Structure;
        public static Material Signal;
        public static int Layer;
        public static bool Ready;

        private static Texture2D _mainTex;
        private static Texture2D _xysTex;
        private static Texture2D _aciTex;
        private static Shader _shader;

        /// <summary>Sign textures, material and picker previews of one style; built when first used.</summary>
        private sealed class StyleAssets
        {
            public SignAtlas Atlas;
            public Material Signal;
            public Texture2D Main;
            public Texture2D Aci;
            public readonly Dictionary<int, Texture2D> Previews = new Dictionary<int, Texture2D>();
        }

        private static readonly Dictionary<SignStyle, StyleAssets> Styles = new Dictionary<SignStyle, StyleAssets>();

        public static void Init()
        {
            if (Ready)
            {
                return;
            }

            try
            {
                Shader shader = null;
                Layer = 0;
                string source = "none";

                int count = PrefabCollection<PropInfo>.LoadedCount();
                for (uint i = 0; i < count; i++)
                {
                    PropInfo info = PrefabCollection<PropInfo>.GetLoaded(i);
                    if (info != null && info.m_material != null && info.m_material.shader != null &&
                        info.m_material.shader.name == PropShaderName)
                    {
                        shader = info.m_material.shader;
                        Layer = info.m_prefabDataLayer;
                        source = "prop '" + info.name + "'";
                        break;
                    }
                }

                if (shader == null)
                {
                    shader = Shader.Find(PropShaderName);
                    source = "Shader.Find";
                }

                if (shader == null)
                {
                    shader = Shader.Find("Diffuse");
                    source = "fallback Diffuse";
                }

                if (shader == null)
                {
                    Log.Error("No usable shader found; gantries will not render");
                    return;
                }

                _mainTex = SolidTexture("GantryMetal", TrussColor(Settings.Current.TrussBrightness));
                _xysTex = SolidTexture("GantryXYS", new Color(0.5f, 0.5f, 0.6f, 1f)); // flat normal, a little specular
                _aciTex = SolidTexture("GantryACI", new Color(0f, 0f, 0f, 1f));      // opaque, tintable, no glow

                Structure = new Material(shader) { name = "GantryStructure" };
                Structure.SetTexture("_MainTex", _mainTex);
                Structure.SetTexture("_XYSMap", _xysTex);
                Structure.SetTexture("_ACIMap", _aciTex);
                Structure.SetColor("_Color", Color.white);

                _shader = shader;
                Signal = BuildStyle(SignStyle.Europe).Signal;

                Ready = true;
                Log.Info("Materials ready: shader='" + shader.name + "' from " + source + ", layer=" + Layer);
            }
            catch (Exception e)
            {
                Log.Error("Material init failed", e);
            }
        }

        /// <summary>The sign material of a style (built on first use; Europe if that fails).</summary>
        public static Material SignalFor(SignStyle style)
        {
            StyleAssets a = Assets(style);
            return a != null ? a.Signal : Signal;
        }

        /// <summary>The atlas layout of a style (always available, even before materials are ready).</summary>
        public static SignAtlas AtlasFor(SignStyle style)
        {
            StyleAssets a = Assets(style);
            return a != null ? a.Atlas : SignAtlas.For(style);
        }

        /// <summary>Picker preview of a resolved sign in a style, or null.</summary>
        public static Texture2D Preview(SignStyle style, FaceSign sign)
        {
            StyleAssets a = Assets(style);
            Texture2D tex;
            return a != null && a.Previews.TryGetValue(a.Atlas.TileOf(sign), out tex) ? tex : null;
        }

        public static Texture2D Preview(SignStyle style, SignalSymbol symbol)
        {
            return Preview(style, new FaceSign(symbol, 0));
        }

        private static StyleAssets Assets(SignStyle style)
        {
            if (!Ready)
            {
                return null;
            }

            StyleAssets a;
            if (Styles.TryGetValue(style, out a))
            {
                if (a.Atlas.CustomRevision == CustomSigns.Revision)
                {
                    return a;
                }

                // The custom sign library changed: rebuild this style's textures and every gantry's signal mesh
                // (the atlas may have grown, which moves every tile's UVs).
                Release(a);
                Styles.Remove(style);
                GantryManager.MarkAllSignalsDirty();
            }

            try
            {
                return BuildStyle(style);
            }
            catch (Exception e)
            {
                Log.Error("Building sign style " + style + " failed; using Europe", e);
                Styles[style] = Styles[SignStyle.Europe];
                return Styles[SignStyle.Europe];
            }
        }

        private static StyleAssets BuildStyle(SignStyle style)
        {
            var a = new StyleAssets { Atlas = SignAtlas.For(style) };
            SignalTextures.Build(a.Atlas, out a.Main, out a.Aci, a.Previews);
            a.Signal = new Material(_shader) { name = "GantrySignal" + style };
            a.Signal.SetTexture("_MainTex", a.Main);
            a.Signal.SetTexture("_XYSMap", _xysTex);
            a.Signal.SetTexture("_ACIMap", a.Aci);
            a.Signal.SetColor("_Color", Color.white);
            Styles[style] = a;
            if (style == SignStyle.Europe)
            {
                Signal = a.Signal;
            }
            Log.Debug("Built sign style " + style);
            return a;
        }

        private static void Release(StyleAssets a)
        {
            foreach (var kv in Styles)
            {
                if (kv.Value == a && kv.Key != a.Atlas.Style)
                {
                    return; // shared fallback entry; released with its owner
                }
            }

            UnityEngine.Object.Destroy(a.Main);
            UnityEngine.Object.Destroy(a.Aci);
            UnityEngine.Object.Destroy(a.Signal);
            foreach (Texture2D t in a.Previews.Values)
            {
                UnityEngine.Object.Destroy(t);
            }
        }

        /// <summary>Slightly cool grey, like galvanised steel.</summary>
        private static Color TrussColor(float brightness)
        {
            return new Color(brightness, brightness * 1.03f, brightness * 1.06f, 1f);
        }

        /// <summary>Recolours the truss texture in place (options panel). Main thread.</summary>
        public static void SetTrussBrightness(float brightness)
        {
            try
            {
                if (_mainTex == null)
                {
                    return;
                }

                Color c = TrussColor(brightness);
                var pixels = new Color[_mainTex.width * _mainTex.height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = c;
                }

                _mainTex.SetPixels(pixels);
                _mainTex.Apply();
            }
            catch (Exception e)
            {
                Log.Error("SetTrussBrightness failed", e);
            }
        }

        public static void Release()
        {
            Ready = false;
            DestroySafe(Structure);
            DestroySafe(_mainTex);
            DestroySafe(_xysTex);
            DestroySafe(_aciTex);
            var destroyed = new HashSet<StyleAssets>();
            foreach (StyleAssets a in Styles.Values)
            {
                if (!destroyed.Add(a))
                {
                    continue; // a failed style shares Europe's assets
                }

                DestroySafe(a.Signal);
                DestroySafe(a.Main);
                DestroySafe(a.Aci);
                foreach (Texture2D t in a.Previews.Values)
                {
                    DestroySafe(t);
                }
            }

            Styles.Clear();
            Signal = null;
            _shader = null;
            Structure = null;
            _mainTex = _xysTex = _aciTex = null;
        }

        private static Texture2D SolidTexture(string name, Color color)
        {
            var tex = new Texture2D(4, 4, TextureFormat.ARGB32, false, true) { name = name };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            try
            {
                if (o != null)
                {
                    UnityEngine.Object.Destroy(o);
                }
            }
            catch
            {
            }
        }
    }
}
