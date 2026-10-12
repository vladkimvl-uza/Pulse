using System;

static class IconGen
{
    static void Main(string[] args)
    {
        Pulse.IconFactory.WriteIco(args[0], new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
        using (var b = Pulse.IconFactory.AppIcon(512)) b.Save(args[0].Replace(".ico", ".png"));
        Console.WriteLine("icon written");
    }
}
