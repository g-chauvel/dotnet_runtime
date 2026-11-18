// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
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
}
