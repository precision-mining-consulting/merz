/* merz - Precision Mining Consulting Tooling
 *
Copyright 2023 Precision Mining Consulting. All rights reserved.

Permission must be explicitly granted from the author, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software for
explicit use. The Software must not be copied, modified, merged, or altered in any way,
without express constent from the author.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
*/

#region Preamble

#region Using Directives
using System.IO;
using System;
using Merz_ = System.Threading.Thread;
using System.Linq;
#endregion

public class Merz
{
    #endregion

    // .NET 8 Spry: merz is built as a managed merz.dll (no native apphost).
    // merz.exe is kept as a fallback for older .NET Framework builds.
    static readonly string[] BinaryNames = { "merz.dll", "merz.exe" };

    static string location()
    {
        string basePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + @"\Programs\merz";

        if (Directory.Exists(basePath))
        {
            string[] versionDirs = Directory.GetDirectories(basePath, "v*");

            // Custom sorting for version directories
            var sortedDirs = versionDirs
                .Select(dir => {
                    string dirName = Path.GetFileName(dir);
                    string versionStr = dirName.TrimStart('v');

                    // Handle suffixes like -beta by only parsing the numeric part for comparison
                    string parsable = versionStr.Split('-')[0];
                    Version version;
                    Version.TryParse(parsable, out version);

                    return new { Path = dir, Version = version, Raw = dirName };
                })
                .OrderByDescending(x => x.Version)  // Sort by numeric version
                .ThenByDescending(x => x.Raw)        // Then by raw name to handle sub-ordering
                .Select(x => x.Path)
                .ToArray();

            if (sortedDirs.Length > 0)
            {
                return sortedDirs[0]; // Use highest version number
            }
        }

        return basePath;
    }


    #region Entry Points
    public static void Open() { @do(""); }

    public static void RunLast() { @do("run-last"); }

    #region Wiring

    static void @do(params string[] a)
    {
        Merz_.GetDomain().ExecuteAssembly(findPath(), a);
    }

    static string findInDir(string dir)
    {
        foreach (var name in BinaryNames)
        {
            string bin = Path.Combine(dir, name);
            if (File.Exists(bin))
                return bin;
        }
        return null;
    }

    static string findPath()
    {
        string bin = findInDir(location());

        if (bin == null)
        {
            string basePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + @"\Programs\merz";
            bin = findInDir(basePath);

            if (bin == null)
                throw new System.Exception("Could not find the merz program (merz.dll) in '" + basePath + "'. If merz was installed elsewhere, please update the location variable.");
        }
        return bin;
    }
}

#endregion
#endregion
