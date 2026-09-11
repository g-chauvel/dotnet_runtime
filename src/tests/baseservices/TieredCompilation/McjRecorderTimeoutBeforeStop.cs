// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;
using TestLibrary;

public static class BasicTest
{
    [ActiveIssue("No crossgen folder under Core_Root", typeof(Utilities), nameof(Utilities.IsNativeAot))]
    [ActiveIssue("No crossgen folder under Core_Root", TestPlatforms.Android)]
    [Fact]
    public static void TestEntryPoint()
    {
        // MulticoreJIT silently disables itself below DOTNET_MultiCoreJitMinNumCpus
        // (default 2): no profile is written and there is nothing to assert.
        if (Environment.ProcessorCount < 2)
        {
            return;
        }

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

        byte[] firstProfile = File.ReadAllBytes(profilePath);

        // Start a second recording with a different method so the new profile differs.
        if (OperatingSystem.IsWindows())
        {
            // MultiCoreJitProfileReadDelay keeps the native player stream open inside
            // StartProfile. Replacing the directory entry from another thread succeeds
            // only when that native stream was opened with FILE_SHARE_DELETE.
            string replacementPath = profilePath + ".replacement";
            File.Copy(profilePath, replacementPath);

            Exception replacementError = null;
            Thread replacer = new(() =>
            {
                Thread.Sleep(250);
                try
                {
                    File.Move(replacementPath, profilePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    replacementError = ex;
                }
            });

            replacer.Start();
            ProfileOptimization.StartProfile("profile.mcj");
            bool replacementFinishedWhileReaderWasOpen = replacer.Join(0);
            replacer.Join();

            Assert.True(replacementFinishedWhileReaderWasOpen, "Profile replacement did not run while the native reader was open");
            Assert.Null(replacementError);
        }
        else
        {
            ProfileOptimization.StartProfile("profile.mcj");
        }

        Bar();

        if (OperatingSystem.IsWindows())
        {
            // Permit replacement of the directory entry, but deny an in-place writer.
            // The old fopen("wb") implementation cannot publish while this handle is
            // open; the atomic temp-file + rename implementation can.
            using FileStream heldProfile = new(
                profilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            ProfileOptimization.StartProfile(null);
        }
        else
        {
            // Unix does not enforce FileShare, so make the final path a symlink to a
            // directory. An in-place fopen("wb") follows the symlink and fails, while
            // publishing a private temp file with rename replaces the symlink itself.
            File.Delete(profilePath);
            Directory.CreateSymbolicLink(profilePath, Environment.CurrentDirectory);
            ProfileOptimization.StartProfile(null);
        }

        byte[] secondProfile = File.ReadAllBytes(profilePath);
        Assert.False(firstProfile.SequenceEqual(secondProfile), "MCJ profile was not atomically replaced");

        // The atomic publish must not leave temp files behind.
        Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, "profile.mcj.*.tmp"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Foo()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Bar()
    {
    }
}
