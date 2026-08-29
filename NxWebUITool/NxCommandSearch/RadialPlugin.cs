namespace NxWebUITool
{
    /// <summary>
    /// 环形命令菜单入口。单独编成 NxRadialMenu.dll，避免与搜索共用 Main。
    /// </summary>
    public static class Plugin
    {
        public static void Main()
        {
            EntryLoader.Run("RunRadial");
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
