using System;using System.IO;using System.Runtime.InteropServices;
namespace TerrariaTmlToolkit{public static class EntryPoint{[UnmanagedCallersOnly]public static int Start(IntPtr p,int n){File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location)!,"tml_ok.txt"),"ok");return 0;}}}
