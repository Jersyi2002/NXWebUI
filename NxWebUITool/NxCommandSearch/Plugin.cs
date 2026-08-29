namespace NxWebUITool
{
    /// <summary>
    /// 命令搜索入口。startup 禁止放本 DLL；点菜单或 Alt+Q 才加载 UI。
    /// </summary>
    public static class Plugin
    {
        public static void Main()
        {
            EntryLoader.Run("Run");
        }

        public static void Main(string[] args)
        {
            Main();
        }

        public static int GetUnloadOption(string unused)
        {
            return EntryLoader.UnloadAtTermination();
        }
    }
}
