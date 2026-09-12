// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

[UnsupportedOSPlatform("windows")]
public static class McjProfilePermissions
{
    [Fact]
    public static void TestEntryPoint()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "mcj-permissions-" + Guid.NewGuid().ToString("N"));
        uint previousMask = Umask(0x12); // 0022: an ordinary fopen-created file would be 0644.
        try
        {
            Directory.CreateDirectory(directory);
            ProfileOptimization.SetProfileRoot(directory);

            Record<NewProfile>("new.mcj");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(directory, "new.mcj")));

            VerifyReplacement<Seed600, Replace600>(directory, "owner.mcj",
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            VerifyReplacement<Seed640, Replace640>(directory, "group.mcj",
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            VerifyReplacement<Seed400, Replace400>(directory, "readonly.mcj",
                UnixFileMode.UserRead, UnixFileMode.UserRead);
            VerifyReplacement<Seed200, Replace200>(directory, "writeonly.mcj",
                UnixFileMode.UserWrite, UnixFileMode.UserWrite);
            VerifyReplacement<Seed000, Replace000>(directory, "inaccessible.mcj",
                UnixFileMode.None, UnixFileMode.None);

            Assert.Empty(Directory.GetFiles(directory, "mcj.*.tmp"));
        }
        finally
        {
            ProfileOptimization.StartProfile(null);
            Umask(previousMask);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void VerifyReplacement<TSeed, TReplacement>(string directory, string name,
        UnixFileMode originalMode, UnixFileMode expectedMode)
        where TSeed : struct where TReplacement : struct
    {
        string path = Path.Combine(directory, name);
        Record<TSeed>(name);
        byte[] original = File.ReadAllBytes(path);
        File.SetUnixFileMode(path, originalMode);

        Record<TReplacement>(name);

        Assert.Equal(expectedMode, File.GetUnixFileMode(path));
        // The 0200/0000 cases are intentionally unreadable until the mode assertion above.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.False(original.SequenceEqual(File.ReadAllBytes(path)), "The profile was not republished");
        Assert.Empty(Directory.GetFiles(directory, "mcj.*.tmp"));
    }

    private static void Record<T>(string name) where T : struct
    {
        ProfileOptimization.StartProfile(name);
        RecordMethod<T>();
        ProfileOptimization.StartProfile(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordMethod<T>() where T : struct
    {
        GC.KeepAlive(typeof(T));
    }

    [DllImport("libc", EntryPoint = "umask")]
    private static extern uint Umask(uint mask);

    private struct NewProfile { }
    private struct Seed600 { }
    private struct Replace600 { }
    private struct Seed640 { }
    private struct Replace640 { }
    private struct Seed400 { }
    private struct Replace400 { }
    private struct Seed200 { }
    private struct Replace200 { }
    private struct Seed000 { }
    private struct Replace000 { }
}
