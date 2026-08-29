namespace NxWebUITool
{
    /// <summary>
    /// 环形命令槽位管理入口。单独编成 NxRadialSlots.dll。
    /// </summary>
    public static class Plugin
    {
        public static void Main()
        {
            EntryLoader.Run("RunSlots");
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
