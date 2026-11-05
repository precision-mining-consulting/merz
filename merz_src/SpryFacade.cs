#if NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace merz
{
    /// <summary>
    /// Emits an in-memory assembly named "Spry" that contains only type forwarders, one per top-level type of the
    /// target assemblies. Nested types need no rows: they are looked up inside their forwarded enclosing type.
    /// </summary>
    static class SpryFacade
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

        public static byte[] Build(Version version, IEnumerable<string> targetPaths, Action<string> log)
        {
            var mb = new MetadataBuilder();
            mb.AddModule(0, mb.GetOrAddString("Spry.dll"), mb.GetOrAddGuid(Guid.NewGuid()), default, default);
            mb.AddAssembly(mb.GetOrAddString("Spry"), version, default, default, 0, AssemblyHashAlgorithm.Sha1);
            mb.AddTypeDefinition(default, default, mb.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

            var forwarded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in targetPaths)
            {
                using var pe = new PEReader(File.OpenRead(path));
                if (!pe.HasMetadata)
                    continue;
                var r = pe.GetMetadataReader();
                if (!r.IsAssembly)
                    continue;

                var def = r.GetAssemblyDefinition();
                var targetName = r.GetString(def.Name);
                var hasKey = !def.PublicKey.IsNil;
                var asmRef = mb.AddAssemblyReference(
                    mb.GetOrAddString(targetName),
                    def.Version,
                    def.Culture.IsNil ? default : mb.GetOrAddString(r.GetString(def.Culture)),
                    hasKey ? mb.GetOrAddBlob(r.GetBlobBytes(def.PublicKey)) : default,
                    hasKey ? AssemblyFlags.PublicKey : 0,
                    default);

                foreach (var h in r.TypeDefinitions)
                {
                    var t = r.GetTypeDefinition(h);
                    if (t.IsNested)
                        continue;
                    var ns = r.GetString(t.Namespace);
                    var name = r.GetString(t.Name);
                    if (name == "<Module>")
                        continue;

                    if (name.StartsWith("<", StringComparison.Ordinal))
                        continue;

                    var full = ns.Length > 0 ? ns + "." + name : name;
                    if (forwarded.TryGetValue(full, out var owner))
                    {
                        log?.Invoke($"merz facade: {full} is in both {owner} and {targetName}; using {owner}");
                        continue;
                    }
                    forwarded[full] = targetName;
                    var exported = mb.AddExportedType(Forwarder, mb.GetOrAddString(ns), mb.GetOrAddString(name), asmRef, 0);
                    AddNested(mb, r, t, exported);
                }
            }

            log?.Invoke($"merz facade: Spry {version} forwards {forwarded.Count} types");

            var pe2 = new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(mb),
                new BlobBuilder(),
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            pe2.Serialize(image);
            return image.ToArray();
        }

        // A TypeRef Outer/Inner is looked up in the facade's own ExportedType table, so nested types need rows too.
        static void AddNested(MetadataBuilder mb, MetadataReader r, TypeDefinition parent, ExportedTypeHandle parentRow)
        {
            foreach (var h in parent.GetNestedTypes())
            {
                var t = r.GetTypeDefinition(h);
                var row = mb.AddExportedType(t.Attributes & TypeAttributes.VisibilityMask, default,
                    mb.GetOrAddString(r.GetString(t.Name)), parentRow, 0);
                AddNested(mb, r, t, row);
            }
        }
    }
}
#endif
