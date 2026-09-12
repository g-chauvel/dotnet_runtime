// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
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
        bool supportsPosixRename = OperatingSystem.IsWindows() && ProbePosixRename();

        // Start a second recording with a different method so the new profile differs.
        if (supportsPosixRename)
        {
            // The native player signals after opening its stream, then waits for
            // the replacement before closing it. No scheduling delay is assumed.
            const int TimeoutMs = 30000;
            const string EventsVariable = "DOTNET_MultiCoreJitProfileReadEvents";
            string eventPrefix = @"Local\MCJ." + Guid.NewGuid().ToString("N");
            using EventWaitHandle ready = new(false, EventResetMode.AutoReset, eventPrefix + ".ready");
            using EventWaitHandle resume = new(false, EventResetMode.AutoReset, eventPrefix + ".continue");
            using EventWaitHandle completed = new(false, EventResetMode.AutoReset, eventPrefix + ".completed");
            string previousEvents = Environment.GetEnvironmentVariable(EventsVariable);
            string replacementPath = profilePath + ".replacement";
            File.Delete(replacementPath);
            File.Copy(profilePath, replacementPath);

            Exception replacementError = null;
            Thread replacer = new(() =>
            {
                try
                {
                    Assert.True(ready.WaitOne(TimeoutMs), "Native profile reader did not signal that its stream was open");
                    Assert.True(TryReplaceProfileWithPosixSemantics(replacementPath, profilePath));
                }
                catch (Exception ex)
                {
                    replacementError = ex;
                }
                finally
                {
                    resume.Set();
                }
            }) { IsBackground = true };

            replacer.Start();
            try
            {
                Environment.SetEnvironmentVariable(EventsVariable, eventPrefix);
                ProfileOptimization.StartProfile("profile.mcj");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EventsVariable, previousEvents);
                resume.Set();
                Assert.True(replacer.Join(TimeoutMs * 2), "Profile replacement thread did not finish");
            }
            Assert.Null(replacementError);
            Assert.True(completed.WaitOne(0), "Native reader did not acknowledge the replacement before closing its stream");
        }
        else
        {
            ProfileOptimization.StartProfile("profile.mcj");
        }

        Bar();

        if (supportsPosixRename)
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

            // The old handle must still expose the old immutable profile after the
            // directory entry has been replaced with the new recording.
            byte[] heldBytes = new byte[firstProfile.Length];
            heldProfile.ReadExactly(heldBytes);
            Assert.True(firstProfile.SequenceEqual(heldBytes), "Published profile changed the old reader's bytes");
            Assert.True(heldProfile.Length == firstProfile.Length, "Published profile changed the old reader's length");
        }
        else if (OperatingSystem.IsWindows())
        {
            // Legacy same-volume rename still publishes once readers have closed.
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
        Assert.Empty(Directory.GetFiles(Environment.CurrentDirectory, "mcj.*.tmp"));

        VerifyLongProfileNames();
    }

    private static void VerifyLongProfileNames()
    {
        // Exercise a nested destination as well as names close to NAME_MAX. The
        // temporary must stay in that directory without extending the final basename.
        string directory = Path.Combine(Environment.CurrentDirectory, "mcj-long-names");
        Directory.CreateDirectory(directory);
        int[] lengths = { 230, 240, 250, 255 };
        for (int i = 0; i < lengths.Length; i++)
        {
            string name = Path.Combine("mcj-long-names", new string('p', lengths[i]));
            string path = Path.Combine(Environment.CurrentDirectory, name);
            File.Delete(path);
            ProfileOptimization.StartProfile(name);
            // Each recording needs a newly jitted method to produce a profile.
            switch (i)
            {
                case 0: RecordLongProfile<byte>(); break;
                case 1: RecordLongProfile<short>(); break;
                case 2: RecordLongProfile<int>(); break;
                case 3: RecordLongProfile<long>(); break;
            }
            ProfileOptimization.StartProfile(null);
            Assert.True(File.Exists(path), $"MCJ profile with basename length {lengths[i]} was not published");
            Assert.True(new FileInfo(path).Length >= 64, "Long-name profile has no complete header");
            Assert.Empty(Directory.GetFiles(directory, "mcj.*.tmp"));
            File.Delete(path);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordLongProfile<T>() where T : struct
    {
    }

    private static bool ProbePosixRename()
    {
        string source = Path.Combine(Environment.CurrentDirectory, "mcj-posix-source");
        string destination = Path.Combine(Environment.CurrentDirectory, "mcj-posix-destination");
        try
        {
            File.WriteAllBytes(source, new byte[] { 2 });
            File.WriteAllBytes(destination, new byte[] { 1 });
            using FileStream reader = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (!TryReplaceProfileWithPosixSemantics(source, destination))
            {
                Console.WriteLine("POSIX rename unavailable; verifying publication with readers closed.");
                return false;
            }
            Assert.Equal(1, reader.ReadByte());
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(destination));
            return true;
        }
        finally
        {
            File.Delete(source);
            File.Delete(destination);
        }
    }

    // File.Move uses MoveFileExW, whose replacement mode rejects an open destination.
    // Exercise the reader's share mode using the same POSIX contract as MCJ publication.
    private static unsafe bool TryReplaceProfileWithPosixSemantics(string source, string destination)
    {
        const uint DeleteAccess = 0x00010000;
        const uint ShareReadWriteDelete = 7;
        const uint OpenExisting = 3;
        const uint NormalAttributes = 0x80;
        const int FileRenameInfoEx = 22;
        const uint ReplaceExistingWithPosixSemantics = 3;

        using SafeFileHandle handle = CreateFileW(source, DeleteAccess, ShareReadWriteDelete,
            IntPtr.Zero, OpenExisting, NormalAttributes, IntPtr.Zero);
        Assert.False(handle.IsInvalid, $"Opening rename source failed: {Marshal.GetLastWin32Error()}");

        int fileNameBytes = checked(destination.Length * sizeof(char));
        int bufferBytes = checked(sizeof(FileRenameInfo) + fileNameBytes);
        byte* buffer = stackalloc byte[bufferBytes];
        FileRenameInfo* info = (FileRenameInfo*)buffer;
        info->Flags = ReplaceExistingWithPosixSemantics;
        info->RootDirectory = IntPtr.Zero;
        info->FileNameLength = (uint)fileNameBytes;
        destination.AsSpan().CopyTo(new Span<char>(&info->FileName, destination.Length));
        (&info->FileName)[destination.Length] = '\0';
        if (SetFileInformationByHandle(handle, FileRenameInfoEx, buffer, (uint)bufferBytes))
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        // Match the runtime fallback, without treating access/sharing failures as
        // missing OS/filesystem support or silently skipping their assertions.
        if (error == 87 || error == 50 || error == 1 || error == 120)
        {
            return false;
        }
        Assert.True(false, $"POSIX profile replacement failed: {error}");
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileRenameInfo
    {
        public uint Flags;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern unsafe bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
        void* information, uint bufferSize);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Foo()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Bar()
    {
    }
}
