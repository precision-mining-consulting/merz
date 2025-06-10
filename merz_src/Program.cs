using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using PrecisionMining.Spry.Util.OptionsForm;
using Progress = PrecisionMining.Spry.Util.UI;
using PrecisionMining.Spry;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
namespace merz
{
    public class Program
    {
        static LastRun LAST_RUN;
        private static Assembly _currentAssembly;

        private static readonly string MerzBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "merz"
        );

        [STAThread]
        static void Main(string[] args)
        {

            if (args.Select(a => a == "run-last").FirstOrDefault())
                RunLast();
            else
                RunOpen();
        }

        static string GetLatestMerzAssemblyPath()
        {
            if (Directory.Exists(MerzBasePath))
            {
                string[] versionDirs = Directory.GetDirectories(MerzBasePath, "v*");
                var sortedDirs = versionDirs
                    .Select(dir => {
                        string dirName = Path.GetFileName(dir);
                        string[] versionParts = dirName.TrimStart('v').Split('.');
                        int major = 0, minor = 0;
                        // Ensure robust parsing for versions like "v1" or "v1.0" or "v1.0.0"
                        if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int parsedMajor))
                            major = parsedMajor;
                        if (versionParts.Length > 1 && int.TryParse(versionParts[1], out int parsedMinor))
                            minor = parsedMinor;
                        // Create a System.Version object for proper sorting
                        Version version = new Version(major, minor);
                        return new { Path = dir, Version = version };
                    })
                    .OrderByDescending(x => x.Version)
                    .Select(x => x.Path)
                    .ToArray();

                if (sortedDirs.Length > 0)
                {
                    string assemblyPath = Path.Combine(sortedDirs[0], "merz.exe");
                    if (File.Exists(assemblyPath))
                        return assemblyPath;
                }
            }
            // Fallback to non-versioned path if no versioned directories found or merz.exe is missing
            return Path.Combine(MerzBasePath, "merz.exe");
        }

        static void LoadMerzAssembly(string path)
        {
            if (_currentAssembly != null)
            {
                try
                {
                    // Assuming Project is available via PrecisionMining.Spry using
                    if (Project.ActiveProject != null && Project.ActiveProject.Scripting != null)
                    {
                        Project.ActiveProject.Scripting.ReferencedAssemblies.Remove(_currentAssembly.FullName);
                    }
                    _currentAssembly = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: Could not unload previous assembly: " + ex.Message);
                    // Potentially log more details or decide if this is critical
                }
            }

            try
            {
                _currentAssembly = Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load assembly from {path}: {ex.Message}");
            }
        }

        static void LoadLatestAssembly()
        {
            string assemblyPath = GetLatestMerzAssemblyPath();
            Out.WriteLine("Loading assembly from: " + assemblyPath);
            if (!File.Exists(assemblyPath))
            {
                throw new Exception("Could not find merz assembly in any location");
            }

            LoadMerzAssembly(assemblyPath);
        }
        static void RunLast()
        {
            LoadLatestAssembly();

            if (LAST_RUN == null || LAST_RUN.ep == null || LAST_RUN.name == null)
                RunOpen();
            else
            {
                BringInSpry();

                Console.WriteLine("Running the last MERZ tool: " + LAST_RUN.name);

                InvokeEntryPoint(LAST_RUN.ep);
            }
        }
        static string GetCurrentVersion()
        {
            Assembly assembly = _currentAssembly ?? Assembly.GetExecutingAssembly();

            string path = assembly.Location;

            // Check if we're running from a version directory
            if (path.Contains(@"\v"))
            {
                // Extract version from path
                string[] parts = path.Split(new string[] { @"\v" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    string versionPart = parts[1];
                    int endIndex = versionPart.IndexOf('\\');
                    if (endIndex > 0)
                        return versionPart.Substring(0, endIndex);
                    return versionPart;
                }
            }

            return path;
        }
        static void RunOpen()
        {
            LoadLatestAssembly();
            BringInSpry();

            var entryPoints = CollectEntryPmSpryToolsEntryPoints();
            EntryPointForm(entryPoints);
        }

        static void BringInSpry(string version = null)
        {

            var spry = Assembly.GetEntryAssembly().Location;
            //Console.WriteLine("Spry location: " + spry);
            if (!File.Exists(spry) || !(spry.EndsWith("Spry.exe") || spry.EndsWith("SpryBeta.exe") || spry.EndsWith("SpryAlpha.exe")))
            {
                var msg = "[ERROR]: Could not find a Spry installation, is this being run through Spry?";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }

            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                //Console.WriteLine("Linking assembly: " + e.Name);
                if (new AssemblyName(e.Name).Name == "Spry")
                    return Assembly.LoadFrom(spry);

                return null;
            };
        }

        static List<EntryPoint> CollectEntryPmSpryToolsEntryPoints()
        {
            Assembly assembly = _currentAssembly ?? Assembly.GetExecutingAssembly();
            return assembly
                .GetTypes()
                .SelectMany(x => x.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(x => x.GetCustomAttribute(typeof(PackAttribute), false) != null)
                .Select(m => EntryPoint.FromAttrMethod(m))
                .ToList();
        }

        static void EntryPointForm(List<EntryPoint> entryPoints)
        {
            //create a start a new thread for the form
            System.Threading.Thread formThread = new System.Threading.Thread(() =>
            {
                try
                {
                    var grps = entryPoints.GroupBy(x => x.group).OrderBy(x => x.Key).ToList();
                    var version = GetCurrentVersion();

                    // Create the form
                    var form = OptionsForm.Create("Merz " + "V: " + version);

                    string latestGitHubTag = GetLatestGitHubVersionAsync().GetAwaiter().GetResult();
                    Out.WriteLine("Latest GitHub version: " + latestGitHubTag);
                    if (!string.IsNullOrEmpty(latestGitHubTag))
                    {
                        string latestVersionStr = latestGitHubTag.TrimStart('v'); // "1.0.1"
                                                                                  //print the latest version and current version
                        Out.WriteLine("Current version: " + version);
                        Out.WriteLine("Latest version from GitHub: " + latestVersionStr);
                        if (Version.TryParse(version, out Version currentV) &&
                            Version.TryParse(latestVersionStr, out Version latestV))
                        {
                            if (latestV > currentV)
                            {
                                Out.WriteLine("New version available within loop: " + latestVersionStr);
                                form.Options.AddTextLabel($"New version available: {latestVersionStr}");
                                string downloadLinkHtml = $"Download from GitHub: <a href=\"https://github.com/precision-mining-consulting/merz/releases/latest\">https://github.com/precision-mining-consulting/merz/releases/latest</a>";

                                form.Options.AddTextLabel(downloadLinkHtml, true);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Could not parse versions for comparison. Current: '{version}', Latest from GitHub: '{latestVersionStr}'");
                        }
                    }

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
                                    // Launch SubForm in a new thread to avoid blocking
                                    LaunchSubFormInThread(items);
                                });
                            }
                            else
                            {
                                var i = items.First();
                                form.Options.AddButtonEdit(i.name).SetClickAction(x =>
                                {
                                    form.Dispose();
                                    // Call InvokeEntryPoint directly since it's not a UI operation
                                    InvokeEntryPoint(i);
                                });
                            }
                        }

                        form.Options.EndGroup();
                    }

                    // Optional: Re-enable update check functionality
                    // SpawnCheckForUpdates(updateLabel);

                    // Show the form - this is blocking but only for this thread
                    form.ShowDialog();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Form thread error: " + ex.Message);
                    Console.WriteLine(ex.StackTrace);
                }
            });
            formThread.SetApartmentState(System.Threading.ApartmentState.STA);
            formThread.IsBackground = true;
            formThread.Start();

        }

        //static checkNewWebVersion()
        private static async Task<string> GetLatestGitHubVersionAsync()
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MerzVersionChecker/1.0"); // GitHub API requires a User-Agent
                var response = await httpClient.GetAsync("https://api.github.com/repos/precision-mining-consulting/merz/releases/latest");
                response.EnsureSuccessStatusCode(); // Throws if not successful
                var responseBody = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(responseBody))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("tag_name", out JsonElement tagNameElement))
                    {
                        return tagNameElement.GetString();
                    }
                }
                throw new Exception("Could not find 'tag_name' in GitHub API response.");
            }
        }
        static void LaunchSubFormInThread(List<EntryPoint> items)
        {
            System.Threading.Thread subFormThread = new System.Threading.Thread(() =>
            {
                try
                {
                    SubForm(items);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SubForm thread error: " + ex.Message);
                }
            });

            subFormThread.SetApartmentState(System.Threading.ApartmentState.STA);
            subFormThread.Start();
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

            if (ep.runInSeparateThread)
            {
                // Create and run a new thread for this entry point
                System.Threading.Thread thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        RunEntryPointAction(ep, n);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Thread error in {n}: {ex.Message}");
                        Console.WriteLine("Stack Trace: " + ex.StackTrace);
                    }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();

            }
            else
            {
                // Original behavior - run directly in current thread
                RunEntryPointAction(ep, n);
            }
        }

        static void RunEntryPointAction(EntryPoint ep, string n)
        {
            try
            {
                ep.invoke();
                PrecisionMining.Spry.Progress.Label = "Finished " + n;
            }
            catch (Exception e)
            {
                // Your existing error handling code
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
}
