#if NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace merz
{
    /// <summary>
    /// Hosts merz and Scripts.dll inside .NET 8 Spry. Both were compiled against the net48 "Spry" assembly, which had
    /// PrecisionMining.Common merged into it; here "Spry" resolves to a generated facade that forwards every type to
    /// the host product assembly and the separate PrecisionMining.Common.* DLLs.
    /// </summary>
    sealed class MerzLoadContext : AssemblyLoadContext
    {
        static MerzLoadContext m_instance;

        readonly string[] m_probeDirs;
        readonly Dictionary<Version, Assembly> m_facades = new Dictionary<Version, Assembly>();
        HashSet<string> m_hostAssemblyNames;

        MerzLoadContext(string merzDir) : base(ContextName)
        {
            m_probeDirs = new[] { merzDir, Path.GetDirectoryName(merzDir) }.Where(d => d != null).ToArray();
        }

        /// <summary>The Spry product assembly in the default context (Spry, SpryBeta, SpryAlpha, ...).</summary>
        public static Assembly Host => Default.Assemblies.FirstOrDefault(a =>
            (a.GetName().Name ?? "").StartsWith("Spry", StringComparison.OrdinalIgnoreCase)
            && a.GetType("PrecisionMining.Spry.Progress", false) != null);

        const string ContextName = "merz";

        // By name, not type: the inner copy's MerzLoadContext is a different type from the outer one that made the context.
        public static bool IsHosted(Assembly merz) => GetLoadContext(merz)?.Name == ContextName;

        /// <summary>Re-runs merz's entry point from a copy loaded into this context. Must not touch Spry types.</summary>
        public static void Run(Assembly outer, string[] args)
        {
            // ExecuteAssembly's LoadFile caches the outer assembly, so this static (and the inner copy's LAST_RUN)
            // survive between script runs.
            m_instance ??= new MerzLoadContext(Path.GetDirectoryName(outer.Location));
            var inner = m_instance.LoadFromAssemblyPath(outer.Location);
            try
            {
                inner.EntryPoint.Invoke(null, new object[] { args });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                Console.WriteLine("merz failed: " + e.InnerException);
            }
        }

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name == "Spry")
            {
                // A request carrying the host's own version comes from the facade's forwarders (Spry release, whose
                // assembly is really named "Spry"); let the default context bind it.
                var host = Host;
                if (host == null || host.GetName().Version == name.Version)
                    return null;
                return GetFacade(name.Version ?? new Version(0, 0, 0, 0), host);
            }

            if (IsProvidedByHost(name.Name))
                return null;

            foreach (var dir in m_probeDirs)
            {
                var path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path))
                    return LoadFromAssemblyPath(path);
            }
            return null;
        }

        Assembly GetFacade(Version version, Assembly host)
        {
            lock (m_facades)
            {
                if (!m_facades.TryGetValue(version, out var facade))
                {
                    var targets = new List<string> { host.Location };
                    // Shipped builds merge Common into the host; dev builds ship it as separate DLLs beside it.
                    targets.AddRange(Directory.GetFiles(Path.GetDirectoryName(host.Location), "PrecisionMining.Common*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                    using var image = new MemoryStream(SpryFacade.Build(version, targets, Debug));
                    facade = LoadFromStream(image);
                    m_facades[version] = facade;
                }
                return facade;
            }
        }

        bool IsProvidedByHost(string name)
        {
            if (m_hostAssemblyNames == null)
            {
                var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileNameWithoutExtension);
                m_hostAssemblyNames = new HashSet<string>(tpa, StringComparer.OrdinalIgnoreCase);
            }
            return m_hostAssemblyNames.Contains(name)
                || Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static void Debug(string message)
        {
            if (Environment.GetEnvironmentVariable("MERZ_DEBUG") == "1")
                Console.WriteLine(message);
        }

        /// <summary>
        /// Resolves every type and member reference of the given assemblies the way the JIT would, reporting what
        /// this Spry no longer provides. Exact signatures, unlike a name scan.
        /// </summary>
        public static void ResolveAllReferences(params Assembly[] assemblies)
        {
            foreach (var asm in assemblies.Distinct())
            {
                int ok = 0, skipped = 0;
                var failures = new List<string>();
                var module = asm.ManifestModule;
                using (var pe = new PEReader(File.OpenRead(asm.Location)))
                {
                    var reader = pe.GetMetadataReader();
                    foreach (var h in reader.TypeReferences)
                        Try(() => module.ResolveType(MetadataTokens.GetToken(h)), () => Describe(reader, h));
                    foreach (var h in reader.MemberReferences)
                        Try(() => module.ResolveMember(MetadataTokens.GetToken(h)), () => Describe(reader, h));
                }

                Console.WriteLine($"merz reference check {asm.GetName().Name}: {ok} resolved, {skipped} skipped (generic context), {failures.Count} failed");
                foreach (var f in failures)
                    Console.WriteLine("  " + f);

                void Try(Func<object> resolve, Func<string> describe)
                {
                    try { resolve(); ok++; }
                    catch (ArgumentException) { skipped++; }
                    catch (Exception e) { failures.Add(describe() + " -> " + e.GetType().Name + ": " + e.Message); }
                }
            }
        }

        static string Describe(MetadataReader r, TypeReferenceHandle h)
        {
            var t = r.GetTypeReference(h);
            var outer = t.ResolutionScope.Kind == HandleKind.TypeReference ? Describe(r, (TypeReferenceHandle)t.ResolutionScope) + "/" : "";
            var ns = r.GetString(t.Namespace);
            return outer + (ns.Length > 0 ? ns + "." : "") + r.GetString(t.Name);
        }

        static string Describe(MetadataReader r, MemberReferenceHandle h)
        {
            var m = r.GetMemberReference(h);
            var parent = m.Parent.Kind == HandleKind.TypeReference ? Describe(r, (TypeReferenceHandle)m.Parent) : m.Parent.Kind.ToString();
            return parent + "::" + r.GetString(m.Name);
        }
    }
}
#endif
