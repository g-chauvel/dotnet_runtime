// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
using Xunit;
using TestLibrary;

// The MulticoreJIT profile is written to a temp file and renamed over the previous one.
public static class McjProfilePublication
{
    // MulticoreJIT silently disables itself below DOTNET_MultiCoreJitMinNumCpus (default 2):
    // no profile is read or written and there is nothing to assert.
    public static bool IsMulticoreJitEnabled => Environment.ProcessorCount >= 2;

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [ConditionalFact(typeof(McjProfilePublication), nameof(IsMulticoreJitEnabled))]
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
            Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, ProfileName + ".*.tmp"));
        }
        finally
        {
            File.Delete(previousPath);
        }
    }

    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [PlatformSpecific(TestPlatforms.AnyUnix)] // The mode of the replaced file is kept on Unix only.
    [ConditionalFact(typeof(McjProfilePublication), nameof(IsMulticoreJitEnabled))]
    public static void PublishedProfileKeepsFileMode()
    {
        const string ProfileName = "mode.mcj";
        // fopen never creates a file with an execute bit: whatever the umask, only a copy of the
        // mode of the replaced profile gives the published one this mode.
        const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        string profilePath = Path.Combine(Environment.CurrentDirectory, ProfileName);
        File.Delete(profilePath);

        ProfileOptimization.SetProfileRoot(Environment.CurrentDirectory);
        ProfileOptimization.StartProfile(ProfileName);
        RecordedBeforeModeChange();
        ProfileOptimization.StartProfile(null);

        byte[] firstProfile = File.ReadAllBytes(profilePath);
        File.SetUnixFileMode(profilePath, Mode);

        ProfileOptimization.StartProfile(ProfileName);
        RecordedAfterModeChange();
        ProfileOptimization.StartProfile(null);

        Assert.NotEqual(firstProfile, File.ReadAllBytes(profilePath));
        Assert.Equal(Mode, File.GetUnixFileMode(profilePath));
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
    private static void RecordedBeforeModeChange()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordedAfterModeChange()
    {
    }
}
