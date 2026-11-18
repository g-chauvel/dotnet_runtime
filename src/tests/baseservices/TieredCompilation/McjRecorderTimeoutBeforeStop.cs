// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using TestLibrary;

public static class BasicTest
{
    // Offsets in HeaderRecord (src/coreclr/vm/multicorejitimpl.h).
    private const int VersionOffset = 4;
    private const int MethodCountOffset = 16;
    // shortCounters[0]: the number of method records the player handled during the session
    // that wrote the profile.
    private const int ReplayedMethodCountOffset = 24;

    // MulticoreJIT silently disables itself below DOTNET_MultiCoreJitMinNumCpus (default 2):
    // no profile is read or written and there is nothing to assert.
    public static bool IsMulticoreJitEnabled => Environment.ProcessorCount >= 2;

    // A privileged process can write a read-only file on Unix.
    public static bool IsReadOnlyFileProtected => OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess;

    // Giving a symbolic link to another user takes privileges.
    public static bool IsPrivilegedProcess => Environment.IsPrivilegedProcess;

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled))]
    public static void TestEntryPoint()
    {
        // A profile left over by a previous run would make the assertions below meaningless.
        string profilePath = Path.Combine(Environment.CurrentDirectory, "profile.mcj");
        File.Delete(profilePath);

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile("profile.mcj");

        // Record a method
        Foo();

        // Let the multi-core JIT recorder time out. The timeout is set to 1 s in the test project.
        Thread.Sleep(2000);

        // Stop the profile again after timeout (just verifying that it works)
        ProfileOptimization.StartProfile(null);

        Assert.True(File.Exists(profilePath), $"MCJ profile was not published at {profilePath}");
        Assert.True(new FileInfo(profilePath).Length > 0, "MCJ profile is empty");

        // The atomic publish must not leave temp files behind.
        Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, "profile.mcj.*.tmp"));
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled))]
    public static void PublishedProfileLeavesPreviousFileIntact()
    {
        const string ProfileName = "published.mcj";
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        string previousPath = profilePath + ".previous";
        File.Delete(profilePath);
        File.Delete(previousPath);

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile(ProfileName);
        RecordedByFirstSession();
        ProfileOptimization.StartProfile(null);

        byte[] firstProfile = File.ReadAllBytes(profilePath);

        // A second name for the file that holds the first profile. A profile written in place
        // goes through that file, where a process starting at the same time reads it half-written.
        File.CreateHardLink(previousPath, profilePath);
        try
        {
            ProfileOptimization.StartProfile(ProfileName);
            RecordedBySecondSession();
            ProfileOptimization.StartProfile(null);

            Assert.NotEqual(firstProfile, File.ReadAllBytes(profilePath));
            Assert.Equal(firstProfile, File.ReadAllBytes(previousPath));
        }
        finally
        {
            File.Delete(previousPath);
        }
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled))]
    public static void RecordedProfileIsReplayed()
    {
        const string ProfileName = "replayed.mcj";
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        File.Delete(profilePath);

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile(ProfileName);
        RecordedForReplay();
        ProfileOptimization.StartProfile(null);

        byte[] recorded = File.ReadAllBytes(profilePath);
        Assert.True(BitConverter.ToUInt32(recorded, MethodCountOffset) > 0, "No method was recorded in the MCJ profile");

        WaitForReplay(ProfileName, recorded);
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled))]
    public static void DamagedProfileIsNotReplayed()
    {
        const string ProfileName = "damaged.mcj";
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        File.Delete(profilePath);

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile(ProfileName);
        RecordedForDamage();
        ProfileOptimization.StartProfile(null);

        byte[] recorded = File.ReadAllBytes(profilePath);
        Assert.True(BitConverter.ToUInt32(recorded, MethodCountOffset) > 0, "No method was recorded in the MCJ profile");

        // A damaged profile left unreplayed proves nothing unless the intact one is replayed
        // within the same wait.
        int waitMs = WaitForReplay(ProfileName, recorded);

        byte[] damagedRecords = (byte[])recorded.Clone();
        damagedRecords[^1] ^= 0xFF;
        byte[] truncatedRecords = recorded[..^8];
        byte[] previousVersion = (byte[])recorded.Clone();
        BitConverter.TryWriteBytes(previousVersion.AsSpan(VersionOffset), 102u);

        foreach (byte[] damaged in new[] { damagedRecords, truncatedRecords, previousVersion })
        {
            File.WriteAllBytes(profilePath, damaged);
            ProfileOptimization.StartProfile(ProfileName);
            Thread.Sleep(waitMs);
            ProfileOptimization.StartProfile(null);

            byte[] published = File.ReadAllBytes(profilePath);
            Assert.NotEqual(damaged, published);
            Assert.Equal(0, ReplayedMethodCount(published));
        }
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled), nameof(IsReadOnlyFileProtected))]
    public static void ReadOnlyProfileIsNotReplaced()
    {
        const string ProfileName = "readonly.mcj";
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        if (File.Exists(profilePath))
        {
            File.SetAttributes(profilePath, FileAttributes.Normal);
            File.Delete(profilePath);
        }

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile(ProfileName);
        RecordedInReadOnlyProfile();
        ProfileOptimization.StartProfile(null);

        byte[] recorded = File.ReadAllBytes(profilePath);
        File.SetAttributes(profilePath, FileAttributes.ReadOnly);
        try
        {
            ProfileOptimization.StartProfile(ProfileName);
            ProfileOptimization.StartProfile(null);

            Assert.Equal(recorded, File.ReadAllBytes(profilePath));
            Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, ProfileName + ".*.tmp"));
        }
        finally
        {
            File.SetAttributes(profilePath, FileAttributes.Normal);
        }
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [PlatformSpecific(TestPlatforms.AnyUnix)] // Publishing through a symbolic link is only implemented on Unix.
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled))]
    public static void SymbolicLinkToProfileIsKept()
    {
        const string ProfileName = "linked.mcj";
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        string targetDirectory = Path.Combine(Environment.CurrentDirectory, "linktarget");
        string targetPath = Path.Combine(targetDirectory, "target.mcj");
        string linkTarget = Path.Combine("linktarget", "target.mcj");

        File.Delete(profilePath);
        Directory.CreateDirectory(targetDirectory);
        File.Delete(targetPath);
        File.CreateSymbolicLink(profilePath, linkTarget);

        // The first session creates the target of the link, the second one replaces it.
        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        for (int session = 0; session < 2; session++)
        {
            ProfileOptimization.StartProfile(ProfileName);
            RecordedThroughSymbolicLink();
            ProfileOptimization.StartProfile(null);

            Assert.Equal(linkTarget, new FileInfo(profilePath).LinkTarget);

            byte[] published = File.ReadAllBytes(targetPath);
            Assert.True(published.Length > MethodCountOffset + sizeof(uint), "The MCJ profile was not published to the target of the symbolic link");
            if (session == 0)
            {
                Assert.True(BitConverter.ToUInt32(published, MethodCountOffset) > 0, "No method was recorded in the MCJ profile");
            }

            Assert.Empty(Directory.GetFiles(targetDirectory, "*.tmp"));
            Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, ProfileName + ".*.tmp"));

            File.WriteAllBytes(targetPath, Array.Empty<byte>());
        }
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [PlatformSpecific(TestPlatforms.AnyUnix)]
    [ConditionalFact(typeof(BasicTest), nameof(IsMulticoreJitEnabled), nameof(IsPrivilegedProcess))]
    public static void SymbolicLinkOfAnotherUserInStickyDirectoryIsNotFollowed()
    {
        const string ProfileName = "planted.mcj";
        const uint OtherUserId = 65534;
        string stickyDirectory = Path.Combine(Environment.CurrentDirectory, "sticky");
        string profilePath = Path.Combine(stickyDirectory, ProfileName);
        string victimPath = Path.Combine(Environment.CurrentDirectory, "victim.mcj");
        byte[] victim = { 1, 2, 3, 4 };

        Directory.CreateDirectory(stickyDirectory);
        File.SetUnixFileMode(stickyDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute |
            UnixFileMode.StickyBit);
        File.Delete(profilePath);
        File.WriteAllBytes(victimPath, victim);
        File.CreateSymbolicLink(profilePath, victimPath);

        // Only the first profile root of a process is used: reach the sticky directory through
        // the profile name.
        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        string profileName = Path.Combine("sticky", ProfileName);

        // A link that belongs to the process is followed, as the kernel does.
        ProfileOptimization.StartProfile(profileName);
        RecordedThroughOwnLink();
        ProfileOptimization.StartProfile(null);

        Assert.NotEqual(victim, File.ReadAllBytes(victimPath));
        Assert.Equal(victimPath, new FileInfo(profilePath).LinkTarget);

        // The same link given to another user, as one planted in a shared directory such as /tmp.
        // The kernel does not follow it on open when fs.protected_symlinks is set: the runtime
        // must not either.
        File.WriteAllBytes(victimPath, victim);
        Assert.True(lchown(profilePath, OtherUserId, OtherUserId) == 0, $"lchown failed: {Marshal.GetLastPInvokeError()}");

        ProfileOptimization.StartProfile(profileName);
        RecordedThroughPlantedLink();
        ProfileOptimization.StartProfile(null);

        Assert.Equal(victim, File.ReadAllBytes(victimPath));
        Assert.Equal(victimPath, new FileInfo(profilePath).LinkTarget);
        Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, "victim.mcj.*.tmp"));
        Assert.Empty(Directory.GetFiles(stickyDirectory, "*.tmp"));
    }

    // Replays the recorded profile and returns the wait after which the player was seen
    // handling its records. The player runs on a background thread and stops with the session,
    // so each attempt waits twice as long.
    private static int WaitForReplay(string profileName, byte[] recorded)
    {
        string profilePath = Path.Combine(Environment.CurrentDirectory, profileName);
        for (int waitMs = 100; ; waitMs *= 2)
        {
            // Stopping a session replaces the profile: put the recorded one back.
            File.WriteAllBytes(profilePath, recorded);
            ProfileOptimization.StartProfile(profileName);
            Thread.Sleep(waitMs);
            ProfileOptimization.StartProfile(null);

            if (ReplayedMethodCount(File.ReadAllBytes(profilePath)) > 0)
            {
                return waitMs;
            }

            Assert.True(waitMs < 6400, "The recorded MCJ profile was not replayed");
        }
    }

    private static int ReplayedMethodCount(byte[] profile) => BitConverter.ToUInt16(profile, ReplayedMethodCountOffset);

    [DllImport("libc", SetLastError = true)]
    private static extern int lchown(string path, uint owner, uint group);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Foo()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedByFirstSession()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedBySecondSession()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedForReplay()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedForDamage()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedInReadOnlyProfile()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedThroughSymbolicLink()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedThroughOwnLink()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedThroughPlantedLink()
    {
    }
}
