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
    // MulticoreJIT silently disables itself below DOTNET_MultiCoreJitMinNumCpus (default 2):
    // no profile is written and there is nothing to assert.
    public static bool IsMulticoreJitEnabled => Environment.ProcessorCount >= 2;

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
}
