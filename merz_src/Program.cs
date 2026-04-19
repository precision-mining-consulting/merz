using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using PrecisionMining.Spry.Util.OptionsForm;
using Progress = PrecisionMining.Spry.Util.UI;
using PrecisionMining.Spry;
using Newtonsoft.Json;

namespace merz
{
    public class Program
    {
        static LastRun LAST_RUN;
        private static bool IsDebugMode = false;

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Contains("--debug"))
            {
                IsDebugMode = true;
            }

            try 
            {
                BringInSpry();

                if (args.Contains("run-last"))
                    RunLast();
                else
                    RunOpen();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal Initialization Error: " + ex.Message);
            }
        }

        static void LogDebug(string message)
        {
            if (IsDebugMode)
            {
                Console.WriteLine(message);
            }
        }

        static void SetCustomProgressDebug(bool enabled)
        {
            try
            {
                // This remains a reflection hack for compatibility with Spry's CustomProgress
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = asm.GetType("PrecisionMining.Spry.Util.UI.CustomProgress");
                    if (type != null)
                    {
                        var field = type.GetField("DEBUG", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            field.SetValue(null, enabled);
                            LogDebug($"Set CustomProgress.DEBUG to {enabled}");
                            return; 
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("Failed to set CustomProgress.DEBUG: " + ex.Message);
            }
        }

        static void RunLast()
        {
            if (LAST_RUN == null || LAST_RUN.ep == null || LAST_RUN.name == null)
            {
                RunOpen();
            }
            else
            {
                SetCustomProgressDebug(IsDebugMode);
                LogDebug("Running the last MERZ tool: " + LAST_RUN.name);
                InvokeEntryPoint(LAST_RUN.ep);
            }
        }

        static string GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string assemblyPath = assembly.Location;

            string version = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)).Name;

            if (version.StartsWith("v"))
            {
                return version.Substring(1);
            }

            return version;
        }

        static void RunOpen()
        {
            var entryPoints = ToolRegistry.DiscoverAndFilterTools();
            SetCustomProgressDebug(IsDebugMode);
            EntryPointForm(entryPoints);
        }

        static void BringInSpry(string version = null)
        {
            var spry = Assembly.GetEntryAssembly()?.Location;
            if (spry == null || !File.Exists(spry) || !(spry.EndsWith("Spry.exe") || spry.EndsWith("SpryBeta.exe") || spry.EndsWith("SpryAlpha.exe")))
            {
                var msg = "[ERROR]: Could not find a Spry installation, is this being run through Spry?";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }

            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                if (new AssemblyName(e.Name).Name == "Spry")
                    return Assembly.LoadFrom(spry);

                return null;
            };
        }

        static void EntryPointForm(List<EntryPoint> entryPoints)
        {
            LogDebug("EntryPointForm called. Running on main thread...");

            try
            {
                var grps = entryPoints.GroupBy(x => x.group).OrderBy(x => x.Key).ToList();
                var version = GetCurrentVersion();

                var form = OptionsForm.Create("Merz V: " + version);

                foreach (var grp in grps)
                {
                    form.Options.BeginGroup(grp.Key).SetExpandable(true, true);
                    var names = grp.GroupBy(x => x.name).OrderBy(x => x.Key).ToList();

                    foreach (var name in names)
                    {
                        var items = name.OrderBy(x => x.subname).ToList();
                        if (items.Count > 1)
                        {
                            form.Options.AddButtonEdit(name.Key).SetClickAction(x =>
                            {
                                form.Dispose();
                                SubForm(items);
                            });
                        }
                        else
                        {
                            var i = items.First();
                            form.Options.AddButtonEdit(i.name).SetClickAction(x =>
                            {
                                form.Dispose();
                                InvokeEntryPoint(i);
                            });
                        }
                    }

                    form.Options.EndGroup();
                }

                form.ShowDialog();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Form error: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void SubForm(List<EntryPoint> subEntryPoints)
        {
            if (subEntryPoints.Count == 0)
                return;

            var f = subEntryPoints.First();

            var form = OptionsForm.Create("Merz -> " + f.group + " -> " + f.name);
            foreach (var ep in subEntryPoints)
            {
                form.Options.AddButtonEdit(string.IsNullOrEmpty(ep.subname) ? ep.name : ep.subname)
                .SetClickAction(x => {
                    form.Dispose();
                    InvokeEntryPoint(ep);
                });
            }

            form.ShowDialog();
        }

        static void InvokeEntryPoint(EntryPoint ep)
        {
            string n = "Merz -> " + ep.group + " -> " + ep.name;
            if (!string.IsNullOrEmpty(ep.subname))
                n += " -> " + ep.subname;

            LAST_RUN = new LastRun()
            {
                ep = ep,
                name = n
            };

            // Streamlined Execution: Run synchronously on the main thread.
            // Spry UI remains frozen during execution to prevent concurrent data modification.
            try
            {
                LogDebug($"Executing {n} synchronously...");
                ep.invoke();
                PrecisionMining.Spry.Progress.Label = "Finished " + n;
            }
            catch (Exception e)
            {
                PrecisionMining.Spry.Progress.Label = "FAILED " + n;
                System.Windows.Forms.MessageBox.Show(
                    text: "Routine threw an error. Please review Spry's output window for the cause of the error.",
                    caption: "ERROR",
                    buttons: System.Windows.Forms.MessageBoxButtons.OK,
                    icon: System.Windows.Forms.MessageBoxIcon.Error
                );
                Console.WriteLine("Error encountered when running " + n);
                if (e.InnerException != null)
                    OutputException(e.InnerException);
            }
        }

        static void OutputException(Exception e)
        {
            OutputException("", e);
            Console.WriteLine("");
            Console.WriteLine("If you believe this is a bug, please file an issue at https://matrix.to/#/#merz-chat:matrix.org");
        }

        static void OutputException(string pad, Exception e)
        {
            Console.WriteLine(PadLines(pad, "Message:"));
            Console.WriteLine(PadLines(pad, e.Message));

            Console.WriteLine("");
            Console.WriteLine(PadLines(pad, "Stack Trace:"));
            Console.WriteLine(PadLines(pad, e.StackTrace));

            if (e.InnerException != null)
            {
                Console.WriteLine("");
                OutputException(pad + "    ", e.InnerException);
            }
        }

        static string PadLines(string pad, string s)
        {
            string x = "";
            bool writeNewline = false;
            using (var r = new StringReader(s))
            {
                var l = r.ReadLine();
                while (l != null)
                {
                    if (writeNewline)
                        x += Environment.NewLine;

                    x += pad + l;
                    l = r.ReadLine();
                    writeNewline = true;
                }
            }

            return x;
        }
    }

    public static class ToolRegistry
    {
        private static readonly string ConfigFileName = "merz-config.json";

        public static List<EntryPoint> DiscoverAndFilterTools()
        {
            var allEntryPoints = new List<EntryPoint>();
            Assembly mainAssembly = Assembly.GetExecutingAssembly();

            // 1. Scan Main Assembly
            allEntryPoints.AddRange(ScanAssemblyForEntryPoints(mainAssembly));

            // 2. Scan Scripts.dll
            string baseDirectory = Path.GetDirectoryName(mainAssembly.Location);
            string scriptsDllPath = Path.Combine(baseDirectory, "Scripts.dll");

            if (File.Exists(scriptsDllPath))
            {
                try
                {
                    Assembly scriptsAssembly = Assembly.LoadFrom(scriptsDllPath);
                    allEntryPoints.AddRange(ScanAssemblyForEntryPoints(scriptsAssembly));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load Scripts.dll from {scriptsDllPath}. Error: {ex.Message}");
                }
            }

            // Remove duplicates
            var uniqueEntryPoints = allEntryPoints
                .GroupBy(ep => $"{ep.group}_{ep.name}_{ep.subname}")
                .Select(g => g.First())
                .ToList();

            // 3. Load CI/CD generated config as a read-only manifest
            var config = LoadManifest(baseDirectory);
            var enabledTools = config.Tools.Where(t => t.Enabled).ToList();

            // If no config exists, everything is enabled by default.
            if (!enabledTools.Any() && config.Tools.Count == 0)
            {
                return uniqueEntryPoints;
            }

            // 4. Filter
            var filteredEntryPoints = uniqueEntryPoints.Where(ep =>
            {
                return enabledTools.Any(tool => 
                    tool.Group == ep.group && 
                    tool.Name == ep.name && 
                    (tool.Sub ?? "") == (ep.subname ?? ""));
            }).ToList();

            return filteredEntryPoints;
        }

        private static IEnumerable<EntryPoint> ScanAssemblyForEntryPoints(Assembly assembly)
        {
            if (assembly == null) return Enumerable.Empty<EntryPoint>();

            try
            {
                var types = assembly.GetTypes();
                return types
                    .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    .Where(m => m.GetCustomAttribute(typeof(PackAttribute), false) != null)
                    .Select(m => EntryPoint.FromAttrMethod(m));
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"Error: Could not load one or more types from assembly ({assembly.FullName}).");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception loaderEx in ex.LoaderExceptions)
                    {
                        Console.WriteLine($"- LoaderException: {loaderEx?.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred while processing types from assembly ({assembly.FullName}): {ex.Message}");
            }
            return Enumerable.Empty<EntryPoint>();
        }

        private static MerzConfig LoadManifest(string baseDirectory)
        {
            string configPath = Path.Combine(baseDirectory, ConfigFileName);
            
            if (!File.Exists(configPath))
            {
                return new MerzConfig();
            }

            try
            {
                string jsonContent = File.ReadAllText(configPath);
                return JsonConvert.DeserializeObject<MerzConfig>(jsonContent) ?? new MerzConfig();
            }
            catch (Exception)
            {
                return new MerzConfig();
            }
        }
    }

    public class EntryPoint
    {
        public string group;
        public string name;
        public string subname;
        public Action invoke;
        public bool runInSeparateThread;
        
        private EntryPoint()
        {
            group = "";
            name = "";
            subname = "";
        }

        public static EntryPoint FromAttrMethod(MethodInfo m)
        {
            var a = m.GetCustomAttribute(typeof(PackAttribute), false) as PackAttribute;
            return new EntryPoint()
            {
                group = a.Group,
                name = a.Name,
                subname = a.Sub,
                runInSeparateThread = a.RunInSeparateThread,
                invoke = () => m.Invoke(null, null)
            };
        }
    }

    class LastRun
    {
        public EntryPoint ep;
        public string name;
    }

    public class ToolConfig
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public string Sub { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class MerzConfig
    {
        public List<ToolConfig> Tools { get; set; } = new List<ToolConfig>();
    }
}
