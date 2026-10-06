using System;
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
        private static Texture2D _signalMain;
        private static Texture2D _signalAci;

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

                SignalTextures.Build(out _signalMain, out _signalAci);
                Signal = new Material(shader) { name = "GantrySignal" };
                Signal.SetTexture("_MainTex", _signalMain);
                Signal.SetTexture("_XYSMap", _xysTex);
                Signal.SetTexture("_ACIMap", _signalAci);
                Signal.SetColor("_Color", Color.white);

                Ready = true;
                Log.Info("Materials ready: shader='" + shader.name + "' from " + source + ", layer=" + Layer);
            }
            catch (Exception e)
            {
                Log.Error("Material init failed", e);
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
            DestroySafe(Signal);
            DestroySafe(_signalMain);
            DestroySafe(_signalAci);
            Signal = null;
            _signalMain = _signalAci = null;
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
