using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace merz
{
    /// <summary>
    /// Loads one merz install (merz.dll, Scripts.dll and any other DLL in that folder) into its own collectible
    /// AssemblyLoadContext.
    ///
    /// Why: Spry runs merz with AppDomain.ExecuteAssembly. Without this, Scripts.dll lands in Spry's Default context,
    /// which holds only one assembly per name. After installing a newer merz, the old Scripts.dll stays loaded, the new
    /// one fails with "Assembly with same name is already loaded", and the menu shows no tools until Spry restarts.
    ///
    /// With this, every install folder gets its own context. A run from a different folder unloads the previous
    /// context and loads the new version. Spry, the .NET runtime and every DLL Spry ships (DevExpress,
    /// Newtonsoft.Json, ...) are still shared from Spry's Default context.
    /// </summary>
    sealed class MerzLoadContext : AssemblyLoadContext
    {
        const string NamePrefix = "merz:";
        // Process-wide slot for the active context. Each merz version is a different copy of this type, so the
        // context is stored as a plain AssemblyLoadContext in AppDomain data and not in a static field.
        const string CurrentKey = "merz.CurrentLoadContext";
        static readonly object s_lock = new object();
        static readonly string[] SpryNames = { "Spry", "SpryBeta", "SpryAlpha" };
        static HashSet<string> s_hostAssemblies;

        readonly string m_dir;
        readonly bool m_debug;

        MerzLoadContext(string dir, bool debug) : base(NamePrefix + dir, isCollectible: true)
        {
            m_dir = dir;
            m_debug = debug;
        }

        /// <summary>True when this copy of merz is already running inside a merz context.</summary>
        public static bool IsHosted(Assembly merz) =>
            (GetLoadContext(merz)?.Name ?? "").StartsWith(NamePrefix, StringComparison.Ordinal);

        /// <summary>The running Spry product assembly (Spry, SpryBeta or SpryAlpha) in the Default context.</summary>
        public static Assembly Host =>
            Default.Assemblies.FirstOrDefault(a => SpryNames.Contains(a.GetName().Name, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Loads merz from <paramref name="outer"/>'s folder into that folder's context and runs its entry point.
        /// Must not touch Spry or Scripts types: the outer copy can't resolve them.
        /// </summary>
        public static void Run(Assembly outer, string[] args)
        {
            bool debug = args.Contains("--debug") || Environment.GetEnvironmentVariable("MERZ_DEBUG") == "1";
            var dir = Path.GetDirectoryName(outer.Location);
            var ctx = GetOrCreate(dir, debug);

            var inner = ctx.LoadFromAssemblyPath(outer.Location);

            // Code that looks types up by name at run time (Assembly.Load, Type.GetType, serializers) resolves
            // through this context while merz runs.
            using (ctx.EnterContextualReflection())
            {
                try
                {
                    inner.EntryPoint.Invoke(null, new object[] { args });
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    Console.WriteLine("merz failed: " + e.InnerException);
                }
            }
        }

        static AssemblyLoadContext GetOrCreate(string dir, bool debug)
        {
            lock (s_lock)
            {
                var name = NamePrefix + dir;
                var current = AppDomain.CurrentDomain.GetData(CurrentKey) as AssemblyLoadContext;

                if (current != null && string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                    return current; // same install: reuse it, so state such as the last-run tool survives

                if (current != null)
                {
                    if (debug)
                        Console.WriteLine("merz: unloading " + current.Name);
                    // Cooperative: memory is freed once nothing references the old version any more.
                    current.Unload();
                }

                if (debug)
                    Console.WriteLine("merz: loading " + dir);

                var created = new MerzLoadContext(dir, debug);
                AppDomain.CurrentDomain.SetData(CurrentKey, created);
                return created;
            }
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            // merz is compiled against "Spry"; SpryBeta/SpryAlpha have different names. Always use the running host.
            if (SpryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Host;

            // Runtime and everything Spry ships: share Spry's copy (returning null falls back to the Default context).
            if (IsHostAssembly(name))
                return null;

            // merz's own files (Scripts.dll, ...): only from this install folder.
            var path = Path.Combine(m_dir, name + ".dll");
            if (File.Exists(path))
            {
                if (m_debug)
                    Console.WriteLine("merz: loading " + name + " from " + path);
                return LoadFromAssemblyPath(path);
            }

            return null;
        }

        static bool IsHostAssembly(string name)
        {
            if (s_hostAssemblies == null)
            {
                // TRUSTED_PLATFORM_ASSEMBLIES = every assembly listed in Spry's deps.json (runtime + Spry's own DLLs).
                var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileNameWithoutExtension);
                s_hostAssemblies = new HashSet<string>(tpa, StringComparer.OrdinalIgnoreCase);
            }
            return s_hostAssemblies.Contains(name);
        }
    }
}
