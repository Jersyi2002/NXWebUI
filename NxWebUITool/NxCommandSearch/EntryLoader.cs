using System.Reflection;
using NXOpen;

namespace NxWebUITool
{
    /// <summary>
    /// 入口 DLL 共用加载器。不得引用 WinForms / WebView2。
    /// </summary>
    internal static class EntryLoader
    {
        static EntryLoader()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                try
                {
                    var name = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(name) ||
                        name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                        return null;

                    foreach (var dir in ProbeDirectories())
                    {
                        var probe = Path.Combine(dir, name + ".dll");
                        if (File.Exists(probe)) return Assembly.LoadFrom(probe);
                    }
                    return null;
                }
                catch
                {
                    return null;
                }
            };
        }

        public static void Run(string hostMethod)
        {
            try
            {
                string uiPath = null;
                foreach (var dir in ProbeDirectories())
                {
                    var probe = Path.Combine(dir, "NxCommandSearch.UI.dll");
                    if (!File.Exists(probe)) continue;
                    uiPath = probe;
                    break;
                }
                if (uiPath == null)
                    throw new FileNotFoundException("未找到界面程序集 NxCommandSearch.UI.dll");

                var ui = Assembly.LoadFrom(uiPath);
                var type = ui.GetType("NxWebUITool.SearchHost", throwOnError: true);
                var run = type.GetMethod(hostMethod, BindingFlags.Public | BindingFlags.Static);
                if (run == null)
                    throw new MissingMethodException("NxWebUITool.SearchHost." + hostMethod);
                run.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                WriteListing(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                WriteListing(ex);
            }
        }

        static IEnumerable<string> ProbeDirectories()
        {
            var ownDir = Path.GetDirectoryName(typeof(EntryLoader).Assembly.Location) ?? ".";
            yield return ownDir;
            var applicationDir = Path.GetFullPath(Path.Combine(ownDir, "..", "application"));
            if (!string.Equals(applicationDir, ownDir, StringComparison.OrdinalIgnoreCase))
                yield return applicationDir;
        }

        public static int UnloadAtTermination()
        {
            return (int)Session.LibraryUnloadOption.AtTermination;
        }

        static void WriteListing(Exception ex)
        {
            try
            {
                var session = Session.GetSession();
                session.ListingWindow.Open();
                session.ListingWindow.WriteLine(ex.ToString());
            }
            catch
            {
                /* NX 尚未就绪时无法写信息窗口 */
            }
        }
    }
}
