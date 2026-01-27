// Decompiled with JetBrains decompiler
// Type: <AgileDotNetRTPro>
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;

#nullable disable
[SecuritySafeCritical]
internal class \u003CAgileDotNetRTPro\u003E
{
  private static bool inited;

  [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern IntPtr LoadLibraryA([In] string obj0);

  [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern IntPtr GetProcAddress([In] IntPtr obj0, [In] string obj1);

  [DllImport("AgileDotNetRTPro.dll", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern int _Initialize([In] IntPtr obj0);

  [DllImport("AgileDotNetRT64Pro.dll", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern int _Initialize64([In] IntPtr obj0);

  [DllImport("AgileDotNetRTPro.dll", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern void _AtExit();

  [DllImport("AgileDotNetRT64Pro.dll", EntryPoint = "_AtExit", CharSet = CharSet.Ansi)]
  [MethodImpl(MethodImplOptions.ForwardRef)]
  private static extern void _AtExit64();

  internal static IntPtr Load()
  {
    WindowsImpersonationContext impersonationContext = WindowsIdentity.Impersonate(IntPtr.Zero);
    Type type;
    Monitor.Enter((object) (type = typeof (\u003CAgileDotNetRTPro\u003E)));
    string path2 = IntPtr.Size != 4 ? "AgileDotNetRT64Pro.dll" : "AgileDotNetRTPro.dll";
    string path = Path.Combine(Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath), path2);
    if (!File.Exists(path))
      path = path2;
    IntPtr num = \u003CAgileDotNetRTPro\u003E.LoadLibraryA(path);
    impersonationContext.Undo();
    Monitor.Exit((object) type);
    return num;
  }

  internal static int InitializeThroughDelegate([In] IntPtr obj0)
  {
    return ((InitializeDelegate) Marshal.GetDelegateForFunctionPointer(\u003CAgileDotNetRTPro\u003E.GetProcAddress(\u003CAgileDotNetRTPro\u003E.Load(), "_Initialize"), typeof (InitializeDelegate)))(obj0);
  }

  internal static int InitializeThroughDelegate64([In] IntPtr obj0)
  {
    return ((InitializeDelegate) Marshal.GetDelegateForFunctionPointer(\u003CAgileDotNetRTPro\u003E.GetProcAddress(\u003CAgileDotNetRTPro\u003E.Load(), "_Initialize64"), typeof (InitializeDelegate)))(obj0);
  }

  internal static void ExitThroughDelegate()
  {
    ((ExitDelegate) Marshal.GetDelegateForFunctionPointer(\u003CAgileDotNetRTPro\u003E.GetProcAddress(\u003CAgileDotNetRTPro\u003E.Load(), "_AtExit"), typeof (ExitDelegate)))();
  }

  internal static void ExitThroughDelegate64()
  {
    ((ExitDelegate) Marshal.GetDelegateForFunctionPointer(\u003CAgileDotNetRTPro\u003E.GetProcAddress(\u003CAgileDotNetRTPro\u003E.Load(), "_AtExit64"), typeof (ExitDelegate)))();
  }

  internal static void DomainUnload([In] object obj0, [In] EventArgs obj1)
  {
    if (IntPtr.Size == 4)
      \u003CAgileDotNetRTPro\u003E.ExitThroughDelegate();
    else
      \u003CAgileDotNetRTPro\u003E.ExitThroughDelegate64();
  }

  internal static void Initialize()
  {
    if (\u003CAgileDotNetRTPro\u003E.inited)
      return;
    RuntimeMethodHandle methodHandle = new StackTrace().GetFrame(0).GetMethod().MethodHandle;
    if ((IntPtr.Size != 4 ? \u003CAgileDotNetRTPro\u003E.InitializeThroughDelegate64(methodHandle.Value) : \u003CAgileDotNetRTPro\u003E.InitializeThroughDelegate(methodHandle.Value)) == 1)
      AppDomain.CurrentDomain.DomainUnload += new EventHandler(\u003CAgileDotNetRTPro\u003E.DomainUnload);
    \u003CAgileDotNetRTPro\u003E.inited = true;
  }

  internal static void PostInitialize()
  {
  }
}
