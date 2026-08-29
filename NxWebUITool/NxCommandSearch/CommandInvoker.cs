using System.Runtime.InteropServices;
using NXOpen;
using NXOpen.UF;

namespace NxWebUITool
{
    static class CommandInvoker
    {
        [DllImport("libugui.dll", EntryPoint = "?MB_main_activate_button_from_event_loop@@YAXH@Z",
            CallingConvention = CallingConvention.Cdecl)]
        static extern void MbActivateFromEventLoop(int buttonId);

        public static void Run(string buttonName, string buttonType)
        {
            if (string.IsNullOrWhiteSpace(buttonName)) return;

            var uf = UFSession.GetUFSession();
            int id = 0;
            try
            {
                uf.Mb.AskButtonId(buttonName, out id);
            }
            catch (Exception ex)
            {
                WriteListing("找不到命令 " + buttonName, ex);
                return;
            }

            if (id == 0)
            {
                WriteListing("命令未注册：" + buttonName, null);
                return;
            }

            MbActivateFromEventLoop(id);
        }

        static void WriteListing(string title, Exception ex)
        {
            try
            {
                var session = Session.GetSession();
                session.ListingWindow.Open();
                session.ListingWindow.WriteLine(title);
                if (ex != null)
                    session.ListingWindow.WriteLine(ex.ToString());
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
