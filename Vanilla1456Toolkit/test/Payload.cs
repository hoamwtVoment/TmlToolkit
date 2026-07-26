using System;using System.IO;
namespace Terraria1456Toolkit { public static class EntryPoint { public static int Start(string x){File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location),"bootstrap_ok.txt"),"ok");return 0;} } }
