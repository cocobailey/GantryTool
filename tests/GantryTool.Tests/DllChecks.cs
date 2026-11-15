using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace GantryTool.Tests
{
    /// <summary>
    /// Checks on the built mod DLL itself, with Mono.Cecil (the library FPS Booster rewrites mods with):
    /// - it can be read and written back with nothing else to hand, as FPS Booster does at startup;
    /// - no panel overrides or calls the UI's Update or LateUpdate, which FPS Booster renames;
    /// - everything it uses from the game exists in both the Windows and the Mac build of the game.
    /// </summary>
    internal static class DllChecks
    {
        private static readonly string[] GameAssemblies = { "Assembly-CSharp", "ColossalManaged", "ICities", "UnityEngine", "UnityEngine.UI" };

        private static string ModPath => typeof(GantryTool.Mod).Assembly.Location;

        /// <summary>
        /// FPS Booster reads each mod, changes it and writes it back to memory. Writing fails if that needs a DLL it can't
        /// find, such as TMPE.API for a constant of a TM:PE type; the mod is then loaded unpatched against a patched game,
        /// and its panels stop updating. Returns what went wrong, or null.
        /// </summary>
        public static string RewriteAlone()
        {
            string dir = Path.Combine(Path.GetTempPath(), "GantryToolRewrite" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string copy = Path.Combine(dir, "GantryTool.dll");
                File.Copy(ModPath, copy);
                using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(copy, new ReaderParameters { AssemblyResolver = Only(dir), InMemory = true }))
                using (var ms = new MemoryStream())
                {
                    mod.Write(ms);
                    return ms.Length > 0 ? null : "nothing written";
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>Update and LateUpdate on the game's UI classes: overridden, or called, anywhere in the mod.</summary>
        public static List<string> UiUpdateUses()
        {
            var found = new List<string>();
            using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(ModPath, new ReaderParameters { InMemory = true }))
            {
                foreach (MemberReference m in mod.MainModule.GetMemberReferences())
                {
                    if ((m.Name == "Update" || m.Name == "LateUpdate") && m.DeclaringType != null && m.DeclaringType.Namespace == "ColossalFramework.UI")
                    {
                        found.Add("calls " + m.FullName);
                    }
                }

                foreach (TypeDefinition t in mod.MainModule.GetTypes())
                {
                    if (t.BaseType == null || t.BaseType.Namespace != "ColossalFramework.UI")
                    {
                        continue;
                    }

                    foreach (MethodDefinition m in t.Methods)
                    {
                        if (m.Name == "Update" || m.Name == "LateUpdate")
                        {
                            found.Add(t.FullName + " has its own " + m.Name);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Every type, method and field the mod uses from the game, looked up in one platform's copy of the game's DLLs.
        /// Returns those that aren't there; null if that folder has no DLLs to check against.
        /// </summary>
        public static List<string> MissingFromGame(string managedDir, out int looked)
        {
            looked = 0;
            if (!Directory.Exists(managedDir) || !File.Exists(Path.Combine(managedDir, "Assembly-CSharp.dll")))
            {
                return null;
            }

            var missing = new List<string>();
            using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(ModPath, new ReaderParameters { AssemblyResolver = Only(managedDir), InMemory = true }))
            {
                foreach (TypeReference t in mod.MainModule.GetTypeReferences())
                {
                    if (!FromGame(t, managedDir))
                    {
                        continue;
                    }

                    looked++;
                    if (!Resolves(() => t.Resolve() != null))
                    {
                        missing.Add("type " + t.FullName);
                    }
                }

                foreach (MemberReference m in mod.MainModule.GetMemberReferences())
                {
                    // An array's own methods (a Color[,]'s Get and Set) are the runtime's, not in any DLL.
                    if (m.DeclaringType == null || m.DeclaringType.IsArray || !FromGame(m.DeclaringType, managedDir))
                    {
                        continue;
                    }

                    looked++;
                    var method = m as MethodReference;
                    var field = m as FieldReference;
                    bool there = method != null ? Resolves(() => method.Resolve() != null) : field != null && Resolves(() => field.Resolve() != null);
                    if (!there)
                    {
                        missing.Add(m.FullName);
                    }
                }
            }

            return missing;
        }

        private static bool FromGame(TypeReference type, string managedDir)
        {
            TypeReference element = type.GetElementType();
            string scope = element.Scope != null ? element.Scope.Name : null;
            return scope != null && Array.IndexOf(GameAssemblies, scope) >= 0 && File.Exists(Path.Combine(managedDir, scope + ".dll"));
        }

        private static bool Resolves(Func<bool> resolve)
        {
            try
            {
                return resolve();
            }
            catch (AssemblyResolutionException)
            {
                return false; // the search went past the game's own classes without finding it
            }
        }

        private static DefaultAssemblyResolver Only(string dir)
        {
            var resolver = new DefaultAssemblyResolver();
            foreach (string d in resolver.GetSearchDirectories())
            {
                resolver.RemoveSearchDirectory(d);
            }

            resolver.AddSearchDirectory(dir);
            return resolver;
        }
    }
}
