using System;
#if UNITY_EDITOR
using UnityEngine;

// Dummy class that is supposed to be Sleepy-Log logging but added here to keep the code the same for Sleepy-Net
public class Log
{
    public static void WriteNow(object o)
    {
        Debug.Log(o);
    }

    public void Write(object o)
    {
        Debug.Log(o);
    }
}

#else

// Dummy class that is supposed to be Sleepy-Log logging but added here to keep the code the same for Sleepy-Net
public class Log
{
    public static void WriteNow(object o)
    {
        Console.WriteLine(o.ToString());
    }

    public void Write(object o)
    {
        Console.WriteLine(o.ToString());
    }
}
#endif