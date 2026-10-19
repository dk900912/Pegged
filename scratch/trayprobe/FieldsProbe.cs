using System;
using System;
using System;
using System.Reflection;
using System.Windows;

namespace trayprobe;

internal static class FieldsProbe
{
    public static void Dump()
    {
        foreach (var f in typeof(Application).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var attrs = f.GetCustomAttribute<System.ThreadStaticAttribute>() is null ? "" : " [ThreadStatic]";
            Console.WriteLine("FIELD " + f.Name + " | " + f.FieldType.Name + attrs);
        }
    }
}
