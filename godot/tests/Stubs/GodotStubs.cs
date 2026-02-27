using System;

namespace Godot;

public class Node
{
    public string Name { get; set; } = string.Empty;

    public void Connect(string signal, Callable callable)
    {
    }

    public void Disconnect(string signal, Callable callable)
    {
    }

    public void CallDeferred(string method, params object[] args)
    {
    }
}

public static class GodotObject
{
    public static bool IsInstanceValid(Node? node) => node != null;
}

public sealed class Callable
{
    private Callable()
    {
    }

    public static Callable From<T>(Action<T> action) => new();
}
