using System.Runtime.InteropServices;

namespace WpfTaskBar;

public class VirtualDesktopUtility
{
    // 仮想デスクトップAPI用の定数とGUID
    private static readonly Guid CLSID_VirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    private static readonly Guid IID_IVirtualDesktopManager = new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    public static bool IsWindowOnCurrentVirtualDesktop(IntPtr windowHandle)
    {
        try
        {
            var virtualDesktopManager = CreateVirtualDesktopManager();
            if (virtualDesktopManager == null)
            {
                return true; // API利用不可の場合は現在のデスクトップとして扱う
            }

            var hr = virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop);
            Marshal.ReleaseComObject(virtualDesktopManager);
            
            return hr >= 0 && isOnCurrentDesktop;
        }
        catch (Exception)
        {
            return true; // エラー時は現在のデスクトップとして扱う
        }
    }

    public static Guid GetWindowDesktop(IntPtr windowHandle)
    {
        try
        {
            var virtualDesktopManager = CreateVirtualDesktopManager();
            if (virtualDesktopManager == null)
            {
                return Guid.Empty;
            }

            var hr = virtualDesktopManager.GetWindowDesktopId(windowHandle, out var desktopId);
            Marshal.ReleaseComObject(virtualDesktopManager);
            
            return hr >= 0 ? desktopId : Guid.Empty;
        }
        catch (Exception)
        {
            return Guid.Empty;
        }
    }

    public static bool MoveWindowToDesktop(IntPtr windowHandle, Guid desktopId)
    {
        try
        {
            var virtualDesktopManager = CreateVirtualDesktopManager();
            if (virtualDesktopManager == null)
            {
                return false;
            }

            var hr = virtualDesktopManager.MoveWindowToDesktop(windowHandle, ref desktopId);
            Marshal.ReleaseComObject(virtualDesktopManager);
            
            return hr >= 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IVirtualDesktopManager? CreateVirtualDesktopManager()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_VirtualDesktopManager);
            if (type == null)
            {
                return null;
            }

            var obj = Activator.CreateInstance(type);
            return obj as IVirtualDesktopManager;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// 仮想デスクトップマネージャー用のCOMインターフェース
[ComImport]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);
    
    [PreserveSig]
    int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
    
    [PreserveSig]
    int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}