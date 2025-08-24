using System.Runtime.InteropServices;
using System.Text;
using WpfTaskBar;

namespace ConsoleApp2;

class Program
{
    private static List<IntPtr> windowHandles = new List<IntPtr>();


    static Task Main(string[] args)
    {
        Console.WriteLine("=== 仮想デスクトップ プロセス判定ツール ===\n");
        
        // 現在のプロセスのデスクトップ情報を表示
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        Console.WriteLine($"現在のプロセスID: {currentProcessId}");
        
        // 全てのウィンドウを列挙してデスクトップ情報を表示
        NativeMethods.EnumWindows(EnumerateWindows, 0);
        
        Console.WriteLine("\n=== タスクバーに表示されるウィンドウの仮想デスクトップ情報 ===");
        
        StringBuilder titleBuffer = new StringBuilder(256);
        
        foreach (var windowHandle in windowHandles)
        {
            if (!NativeMethodUtility.IsTaskBarWindow(windowHandle))
            {
                continue;
            }

            titleBuffer.Clear();
            NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
            string windowTitle = titleBuffer.ToString();
            
            if (string.IsNullOrWhiteSpace(windowTitle))
            {
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            
            // ClassLibraryのVirtualDesktopUtilityを使用
            var desktopId = VirtualDesktopUtility.GetWindowDesktop(windowHandle);
            var isOnCurrentDesktop = VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(windowHandle);
            
            Console.WriteLine($"ウィンドウハンドル: {windowHandle}");
            Console.WriteLine($"ウィンドウタイトル: {windowTitle}");
            Console.WriteLine($"プロセスID: {processId}");
            Console.WriteLine($"現在のデスクトップにある: {(isOnCurrentDesktop ? "はい" : "いいえ")}");
            Console.WriteLine($"デスクトップGUID: {desktopId}");
            Console.WriteLine("---");
        }
        
        return Task.CompletedTask;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private static bool EnumerateWindows(IntPtr hwnd, int lParam)
    {
        windowHandles.Add(hwnd);
        return true;
    }

}