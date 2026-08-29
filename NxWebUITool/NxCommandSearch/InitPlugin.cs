using NXOpen;

namespace NxWebUITool
{
    /// <summary>
    /// 初始化项目入口。单独编成 NxProjectInit.dll，不加载 WinForms/WebView2。
    /// </summary>
    public static class Plugin
    {
        public static void Main()
        {
            ProjectInit.Run();
        }

        public static void Main(string[] args)
        {
            Main();
        }

        public static int GetUnloadOption(string unused)
        {
            return (int)Session.LibraryUnloadOption.AtTermination;
        }
    }
}
