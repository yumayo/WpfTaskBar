using System.Runtime.InteropServices;
using System.Text;
using WpfTaskBar;

namespace ConsoleApp;

class Program
{
    private static List<IntPtr> windowHandles = new List<IntPtr>();
    
    static void Main(string[] args)
    {
       NativeMethods.EnumWindows(EnumerationWindows, 0);

       foreach (var windowHandle in windowHandles)
       {
          if (!NativeMethodUtility.IsTaskBarWindow(windowHandle))
          {
             continue;
          }
          
          // タイトルを取得
          StringBuilder titleBuffer = new StringBuilder(256);
          NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
          string windowTitle = titleBuffer.ToString();
          
          var processName = UwpUtility.GetProcessName(windowHandle);
          var processId = UwpUtility.GetProcessId(windowHandle);
          var appxPackage = AppxPackageUtility.AppxPackage.FromWindow(windowHandle);
          
          // 情報を表示
          Console.WriteLine("===== Window Information =====");
          Console.WriteLine($"Window Handle: {windowHandle}");
          Console.WriteLine($"Window Title: {windowTitle}");
          Console.WriteLine($"Process Name: {processName}");
          Console.WriteLine($"Process ID: {processId}");
          
          // AppxPackage情報を表示（nullでない場合）
          if (appxPackage != null)
          {
              Console.WriteLine($"Appx Display Name: {appxPackage.DisplayName}");
              Console.WriteLine($"Appx Publisher: {appxPackage.Publisher}");
              Console.WriteLine($"Appx Version: {appxPackage.Version}");
          }
          else
          {
              Console.WriteLine("Appx Package: Not Available (Win32 application)");
          }
          
          Console.WriteLine();
       }
    }
    
    [return: MarshalAs(UnmanagedType.Bool)]
    private static bool EnumerationWindows(IntPtr hwnd, int lParam)
    {
       // EnumWindowsで列挙されたWindowを全て記録する。
       windowHandles.Add(hwnd);
       return true;
    }
}