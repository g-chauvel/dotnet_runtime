// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "stdafx.h"
#include "windows.h"
#include "longfilepathwrappers.h"
#include "sstring.h"
#include "ex.h"
#include <dn-stdio.h>

#ifdef HOST_WINDOWS
#include <fcntl.h>
#include <io.h>
#endif // HOST_WINDOWS

#ifdef HOST_WINDOWS
class LongFile
{
private:
        static const WCHAR* ExtendedPrefix;
        static const WCHAR* DevicePathPrefix;
        static const WCHAR* UNCPathPrefix;
        static const WCHAR* UNCExtendedPathPrefix;
        static const WCHAR VolumeSeparatorChar;
		#define UNCPATHPREFIX W("\\\\")
        static const WCHAR AltDirectorySeparatorChar;
        static const WCHAR DirectorySeparatorChar;
public:
        static BOOL IsDirectorySeparator(WCHAR c);
        static BOOL IsPathNotFullyQualified(const SString & path);

        static HRESULT NormalizePath(SString& path);

        static BOOL IsExtended(const SString & path);
        static BOOL IsUNCExtended(const SString & path);
        static BOOL IsDevice(const SString & path);
        static void NormalizeDirectorySeparators(SString& path);
};
#endif // HOST_WINDOWS

DWORD
SearchPathWrapper(
        _In_opt_ LPCWSTR lpPath,
        _In_ LPCWSTR lpFileName,
        _In_opt_ LPCWSTR lpExtension,
        _In_ BOOL getPath,
        SString& lpBuffer,
        _Out_opt_ LPWSTR * lpFilePart
        )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr  = S_OK;
    DWORD    ret = 0;
    DWORD lastError = 0;

    EX_TRY
    {
#ifdef HOST_WINDOWS
        LongPathString Existingpath(LongPathString::Literal, lpPath);
        if (lpPath != NULL)
        {
            if (FAILED(LongFile::NormalizePath(Existingpath)))
            {
                ret = FALSE;
            }
            else
            {
                lpPath = Existingpath.GetUnicode();
            }
        }
#endif // HOST_WINDOWS

        if (!getPath)
        {
            ret = SearchPathW(
                    lpPath,
                    lpFileName,
                    lpExtension,
                    0,
                    NULL,
                    NULL
                    );
        }
        else
        {
            COUNT_T size = lpBuffer.GetUnicodeAllocation() + 1;

            ret = SearchPathW(
                    lpPath,
                    lpFileName,
                    lpExtension,
                    size,
                    lpBuffer.OpenUnicodeBuffer(size - 1),
                    lpFilePart
                    );

            if (ret > size)
            {
                lpBuffer.CloseBuffer();
                ret = SearchPathW(
                        lpPath,
                        lpFileName,
                        lpExtension,
                        ret,
                        lpBuffer.OpenUnicodeBuffer(ret - 1),
                        lpFilePart
                        );
            }

            lpBuffer.CloseBuffer(ret);

        }

        lastError = GetLastError();
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if (ret == 0)
    {
        SetLastError(lastError);
    }

    return ret;

}

DWORD
GetModuleFileNameWrapper(
    _In_opt_ HMODULE hModule,
    SString& buffer
    )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr = S_OK;
    DWORD ret = 0;
    DWORD lastError = 0;

    EX_TRY
    {
        COUNT_T size = buffer.GetUnicodeAllocation() + 1;

        ret = GetModuleFileNameW(
            hModule,
            buffer.OpenUnicodeBuffer(size - 1),
            (DWORD)size
            );


        while (ret == size )
        {
            buffer.CloseBuffer();
            size = size * 2;
            ret = GetModuleFileNameW(
                hModule,
                buffer.OpenUnicodeBuffer(size - 1),
                (DWORD)size
                );

        }


        lastError = GetLastError();
        buffer.CloseBuffer(ret);
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if (ret == 0)
    {
        SetLastError(lastError);
    }

    return ret;
}

DWORD WINAPI GetEnvironmentVariableWrapper(
    _In_opt_  LPCTSTR lpName,
    _Out_opt_ SString&  lpBuffer
    )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr = S_OK;
    DWORD ret = 0;
    DWORD lastError = 0;

    EX_TRY
    {

        COUNT_T size = lpBuffer.GetUnicodeAllocation() + 1;

        ret = GetEnvironmentVariableW(
            lpName,
            lpBuffer.OpenUnicodeBuffer(size - 1),
            size
            );

        // We loop round getting the length of the env var and then trying to copy
        // the value into a the allocated buffer. Usually we'll go through this loop
        // precisely once, but the caution is necessary in case the variable mutates
        // beneath us, as the environment variable can be modified by another thread
        //between two calls to GetEnvironmentVariableW

        while (ret > size)
        {
            size = ret;
            lpBuffer.CloseBuffer();
            ret = GetEnvironmentVariableW(
                lpName,
                lpBuffer.OpenUnicodeBuffer(size - 1),
                size);
        }

        lastError = GetLastError();
        lpBuffer.CloseBuffer(ret);
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if (ret == 0)
    {
        SetLastError(lastError);
    }

    return ret;
}

#ifdef HOST_WINDOWS

HMODULE
LoadLibraryExWrapper(
        LPCWSTR lpLibFileName,
        HANDLE hFile,
        DWORD dwFlags
        )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr   = S_OK;
    HMODULE ret = NULL;
    DWORD lastError = 0;

    EX_TRY
    {
        LongPathString path(LongPathString::Literal, lpLibFileName);

        if (LongFile::IsPathNotFullyQualified(path) || SUCCEEDED(LongFile::NormalizePath(path)))
        {
            LongFile::NormalizeDirectorySeparators(path);

            ret = LoadLibraryExW(path.GetUnicode(), hFile, dwFlags);
        }

        lastError = GetLastError();
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if(ret == NULL)
    {
        SetLastError(lastError);
    }

    return ret;
}

HANDLE
CreateFileWrapper(
        _In_ LPCWSTR lpFileName,
        _In_ DWORD dwDesiredAccess,
        _In_ DWORD dwShareMode,
        _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
        _In_ DWORD dwCreationDisposition,
        _In_ DWORD dwFlagsAndAttributes,
        _In_opt_ HANDLE hTemplateFile
        )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr = S_OK;
    DWORD lastError = 0;
    HANDLE ret = INVALID_HANDLE_VALUE;

    EX_TRY
    {
        LongPathString path(LongPathString::Literal, lpFileName);

        if (SUCCEEDED(LongFile::NormalizePath(path)))
        {
            ret = CreateFileW(path.GetUnicode(),
                    dwDesiredAccess,
                    dwShareMode,
                    lpSecurityAttributes,
                    dwCreationDisposition,
                    dwFlagsAndAttributes,
                    hTemplateFile);

        }

        lastError = GetLastError();
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK )
    {
        SetLastError(hr);
    }
    else if(ret == INVALID_HANDLE_VALUE)
    {
        SetLastError(lastError);
    }

    return ret;
}

int u16_fopen_wrapper(FILE** stream, const WCHAR* filename, const WCHAR* mode)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    EX_TRY
    {
        LongPathString path(LongPathString::Literal, filename);

        if (SUCCEEDED(LongFile::NormalizePath(path)))
        {
            return u16_fopen_s(stream, path.GetUnicode(), mode);
        }
    }
    EX_CATCH
    {
        return -1;
    }
    EX_END_CATCH

    return -1;
}

int u16_fopen_read_shared_delete_wrapper(FILE** stream, const WCHAR* filename)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    *stream = nullptr;

    // The MulticoreJIT writer publishes a completed profile by renaming it over
    // the final path. Allow that delete access while readers keep the old file
    // alive, but continue to deny in-place writers.
    HANDLE fileHandle = CreateFileWrapper(
        filename,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (fileHandle == INVALID_HANDLE_VALUE)
    {
        return -1;
    }

    // _open_osfhandle transfers ownership of the Win32 handle to the CRT file
    // descriptor, and _fdopen transfers the descriptor to the FILE stream.
    int fd = _open_osfhandle(reinterpret_cast<intptr_t>(fileHandle), _O_RDONLY | _O_BINARY);
    if (fd == -1)
    {
        CloseHandle(fileHandle);
        return -1;
    }

    FILE* file = _fdopen(fd, "rb");
    if (file == nullptr)
    {
        _close(fd);
        return -1;
    }

    *stream = file;
    return 0;
}

BOOL
CopyFileExWrapper(
        _In_        LPCWSTR lpExistingFileName,
        _In_        LPCWSTR lpNewFileName,
        _In_opt_    LPPROGRESS_ROUTINE lpProgressRoutine,
        _In_opt_    LPVOID lpData,
        _When_(pbCancel != NULL, _Pre_satisfies_(*pbCancel == FALSE))
        _Inout_opt_ LPBOOL pbCancel,
        _In_        DWORD dwCopyFlags
        )
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    HRESULT hr  = S_OK;
    BOOL    ret = FALSE;
    DWORD lastError = 0;

    EX_TRY
    {
        LongPathString Existingpath(LongPathString::Literal, lpExistingFileName);
        LongPathString Newpath(LongPathString::Literal, lpNewFileName);

        if (SUCCEEDED(LongFile::NormalizePath(Existingpath)) && SUCCEEDED(LongFile::NormalizePath(Newpath)))
        {
            ret = CopyFileExW(
                    Existingpath.GetUnicode(),
                    Newpath.GetUnicode(),
                    lpProgressRoutine,
                    lpData,
                    pbCancel,
                    dwCopyFlags
                    );
        }

        lastError = GetLastError();
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK )
    {
        SetLastError(hr);
    }
    else if(ret == FALSE)
    {
        SetLastError(lastError);
    }

    return ret;
}

BOOL
ReplaceFileWithPosixSemanticsWrapper(
        _In_ LPCWSTR lpExistingFileName,
        _In_ LPCWSTR lpNewFileName
        )
{
    CONTRACTL
    {
        NOTHROW;
    }
    CONTRACTL_END;

    HRESULT hr = S_OK;
    BOOL ret = FALSE;
    DWORD lastError = 0;

    EX_TRY
    {
        LongPathString Existingpath(LongPathString::Literal, lpExistingFileName);
        LongPathString Newpath(LongPathString::Literal, lpNewFileName);

        if (SUCCEEDED(LongFile::NormalizePath(Existingpath)) && SUCCEEDED(LongFile::NormalizePath(Newpath)))
        {
            // MoveFileExW cannot replace an open destination even when its readers
            // allow FILE_SHARE_DELETE. POSIX rename preserves those readers' handles
            // while making the new file visible to subsequent opens.
            // CoreCLR compiles with a Windows 8 API baseline. Newer SDKs hide
            // FILE_RENAME_INFO.Flags and FileRenameInfoEx behind newer target macros,
            // so describe their ABI locally without raising the global baseline.
            struct PosixFileRenameInfo
            {
                DWORD Flags;
                HANDLE RootDirectory;
                DWORD FileNameLength;
                WCHAR FileName[1];
            };
            static_assert(sizeof(PosixFileRenameInfo) == sizeof(FILE_RENAME_INFO));
            static_assert(offsetof(PosixFileRenameInfo, FileName) == offsetof(FILE_RENAME_INFO, FileName));
            const DWORD replaceIfExists = 0x1; // FILE_RENAME_FLAG_REPLACE_IF_EXISTS
            const DWORD posixSemantics = 0x2;  // FILE_RENAME_FLAG_POSIX_SEMANTICS
            const FILE_INFO_BY_HANDLE_CLASS renameInfoEx = static_cast<FILE_INFO_BY_HANDLE_CLASS>(22);

            S_UINT32 fileNameBytes = S_UINT32(Newpath.GetCount()) * S_UINT32(sizeof(WCHAR));
            S_UINT32 bufferBytes = fileNameBytes + S_UINT32(sizeof(PosixFileRenameInfo));
            if (bufferBytes.IsOverflow())
            {
                lastError = ERROR_FILENAME_EXCED_RANGE;
            }
            else
            {
                // Allocate before opening the source. The holder closes the handle
                // on all paths after the allocation has succeeded.
                NewArrayHolder<BYTE> buffer = new BYTE[bufferBytes.Value()];
                PosixFileRenameInfo* renameInfo = reinterpret_cast<PosixFileRenameInfo*>(buffer.GetValue());
                renameInfo->Flags = replaceIfExists | posixSemantics;
                renameInfo->RootDirectory = NULL;
                renameInfo->FileNameLength = fileNameBytes.Value();
                memcpy(renameInfo->FileName, Newpath.GetUnicode(), fileNameBytes.Value() + sizeof(WCHAR));

                bool renameInfoExUnsupported = false;
                {
                    HandleHolder source(CreateFileW(Existingpath.GetUnicode(), DELETE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
                        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL));
                    if (source != INVALID_HANDLE_VALUE)
                    {
                        ret = SetFileInformationByHandle(source, renameInfoEx,
                            renameInfo, bufferBytes.Value());
                        lastError = GetLastError();
                        renameInfoExUnsupported = !ret &&
                            (lastError == ERROR_INVALID_PARAMETER || lastError == ERROR_NOT_SUPPORTED ||
                             lastError == ERROR_INVALID_FUNCTION || lastError == ERROR_CALL_NOT_IMPLEMENTED);
                    }
                    else
                    {
                        lastError = GetLastError();
                    }
                }

                if (renameInfoExUnsupported)
                {
                    // Older supported Windows versions and some filesystems do not
                    // implement POSIX rename. Close the source handle before trying
                    // the legacy same-volume rename (never enable COPY_ALLOWED).
                    // This may refuse publication while a reader is open, but must
                    // never fall back to truncating or writing the final file in place.
                    ret = MoveFileExW(Existingpath.GetUnicode(), Newpath.GetUnicode(), MOVEFILE_REPLACE_EXISTING);
                    lastError = GetLastError();
                }
            }
        }
        else
        {
            lastError = ERROR_INVALID_NAME;
        }
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if (!ret)
    {
        SetLastError(lastError);
    }

    return ret;
}

BOOL
CopyFileDaclWrapper(
        _In_ LPCWSTR lpExistingFileName,
        _In_ LPCWSTR lpNewFileName
        )
{
    CONTRACTL
    {
        NOTHROW;
    }
    CONTRACTL_END;

    HRESULT hr = S_OK;
    BOOL ret = FALSE;
    DWORD lastError = ERROR_SUCCESS;

    EX_TRY
    {
        LongPathString existingPath(LongPathString::Literal, lpExistingFileName);
        LongPathString newPath(LongPathString::Literal, lpNewFileName);

        if (!SUCCEEDED(LongFile::NormalizePath(existingPath)) || !SUCCEEDED(LongFile::NormalizePath(newPath)))
        {
            lastError = ERROR_INVALID_NAME;
        }
        else
        {
            DWORD securityDescriptorSize = 0;
            if (GetFileSecurityW(existingPath.GetUnicode(), DACL_SECURITY_INFORMATION, nullptr, 0, &securityDescriptorSize))
            {
                lastError = ERROR_INVALID_DATA;
            }
            else
            {
                lastError = GetLastError();
                if (lastError == ERROR_FILE_NOT_FOUND || lastError == ERROR_PATH_NOT_FOUND)
                {
                    // The first publication has no destination DACL to preserve.
                    ret = TRUE;
                }
                else if (lastError == ERROR_INSUFFICIENT_BUFFER && securityDescriptorSize != 0)
                {
                    NewArrayHolder<BYTE> securityDescriptor = new BYTE[securityDescriptorSize];
                    if (GetFileSecurityW(existingPath.GetUnicode(), DACL_SECURITY_INFORMATION,
                        reinterpret_cast<PSECURITY_DESCRIPTOR>(securityDescriptor.GetValue()), securityDescriptorSize, &securityDescriptorSize))
                    {
                        ret = SetFileSecurityW(newPath.GetUnicode(), DACL_SECURITY_INFORMATION,
                            reinterpret_cast<PSECURITY_DESCRIPTOR>(securityDescriptor.GetValue()));
                        lastError = GetLastError();
                    }
                    else
                    {
                        lastError = GetLastError();
                    }
                }
            }
        }
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK)
    {
        SetLastError(hr);
    }
    else if (!ret)
    {
        SetLastError(lastError);
    }

    return ret;
}

BOOL
DeleteFileWrapper(
        _In_ LPCWSTR lpFileName
        )
{
    CONTRACTL
    {
        NOTHROW;
    }
    CONTRACTL_END;

    HRESULT hr  = S_OK;
    BOOL    ret = FALSE;
    DWORD lastError = 0;

    EX_TRY
    {
        LongPathString path(LongPathString::Literal, lpFileName);

        if (SUCCEEDED(LongFile::NormalizePath(path)))
        {
            ret = DeleteFileW(path.GetUnicode());
        }

        lastError = GetLastError();
    }
    EX_CATCH_HRESULT(hr);

    if (hr != S_OK )
    {
        SetLastError(hr);
    }
    else if(ret == FALSE)
    {
        SetLastError(lastError);
    }

    return ret;
}

//Implementation of LongFile Helpers
const WCHAR LongFile::DirectorySeparatorChar = W('\\');
const WCHAR LongFile::AltDirectorySeparatorChar = W('/');
const WCHAR LongFile::VolumeSeparatorChar = W(':');
const WCHAR* LongFile::ExtendedPrefix = W("\\\\?\\");
const WCHAR* LongFile::DevicePathPrefix = W("\\\\.\\");
const WCHAR* LongFile::UNCExtendedPathPrefix = W("\\\\?\\UNC\\");
const WCHAR* LongFile::UNCPathPrefix = UNCPATHPREFIX;

void LongFile::NormalizeDirectorySeparators(SString& path)
{
    for(SString::Iterator i = path.Begin(); i < path.End(); ++i)
    {
        if (*i == AltDirectorySeparatorChar)
        {
            path.Replace(i, DirectorySeparatorChar);
        }
    }
}

BOOL LongFile::IsExtended(const SString & path)
{
    return path.BeginsWith(SL(ExtendedPrefix));
}

BOOL LongFile::IsUNCExtended(const SString & path)
{
    return path.BeginsWith(SL(UNCExtendedPathPrefix));
}

// Relative here means it could be relative to current directory on the relevant drive
// NOTE: Relative segments ( \..\) are not considered relative
// Returns true if the path specified is relative to the current drive or working directory.
// Returns false if the path is fixed to a specific drive or UNC path.  This method does no
// validation of the path (URIs will be returned as relative as a result).
// Handles paths that use the alternate directory separator.  It is a frequent mistake to
// assume that rooted paths (Path.IsPathRooted) are not relative.  This isn't the case.

BOOL LongFile::IsPathNotFullyQualified(const SString & path)
{
    if (path.GetCount() < 2)
    {
        return TRUE;  // It isn't fixed, it must be relative.  There is no way to specify a fixed path with one character (or less).
    }

    if (IsDirectorySeparator(path[0]))
    {
        return !IsDirectorySeparator(path[1]); // There is no valid way to specify a relative path with two initial slashes
    }

    return !((path.GetCount() >= 3)           //The only way to specify a fixed path that doesn't begin with two slashes is the drive, colon, slash format- i.e. "C:\"
            && (path[1] == VolumeSeparatorChar)
            && IsDirectorySeparator(path[2]));
}

BOOL LongFile::IsDevice(const SString & path)
{
    return path.BeginsWith(SL(DevicePathPrefix));
}

// This function will normalize paths if the path length exceeds MAX_PATH
// The normalization examples are :
//  C:\foo\<long>\bar   => \\?\C:\foo\<long>\bar
//  \\server\<long>\bar => \\?\UNC\server\<long>\bar
HRESULT LongFile::NormalizePath(SString & path)
{
    HRESULT hr        = S_OK;
    DWORD   ret       = 0;
    COUNT_T prefixLen = 0;
    if (path.IsEmpty()|| IsDevice(path) || IsExtended(path) || IsUNCExtended(path))
        return S_OK;

    if (!IsPathNotFullyQualified(path) && path.GetCount() < MAX_LONGPATH)
        return S_OK;

    //Now the path will be normalized

    SString originalPath(path);
    SString prefix(ExtendedPrefix);
    prefixLen = prefix.GetCount();

    if (path.BeginsWith(SL(UNCPathPrefix)))
    {
        prefix.Set(UNCExtendedPathPrefix);
        //In this case if path is \\server the extended syntax should be like  \\?\UNC\server
        //The below logic populates the path from prefixLen offset from the start. This ensures that first 2 characters are overwritten
        //
        prefixLen = prefix.GetCount() - (COUNT_T)u16_strlen(UNCPATHPREFIX);
        _ASSERTE(prefixLen > 0 );
    }


    COUNT_T size  = path.GetUnicodeAllocation() + 1;
    WCHAR* buffer = path.OpenUnicodeBuffer(size - 1);

    ret = GetFullPathNameW(
        originalPath.GetUnicode(),
        size - prefixLen,        //memory avilable for path after reserving for prefix
        (buffer + prefixLen),    //reserve memory for prefix
        NULL
        );

    if (ret == 0)
    {
        return E_FAIL;
    }

    if (ret > size - prefixLen)
    {
        path.CloseBuffer();
        size   = ret + prefixLen;
        buffer = path.OpenUnicodeBuffer(size -1);

        ret = GetFullPathNameW(
            originalPath.GetUnicode(),
            ret,                   // memory required for the path
            (buffer + prefixLen),  //reserve memory for prefix
            NULL
            );

        _ASSERTE(ret < size - prefixLen);

        if (ret == 0)
        {
            return E_FAIL;
        }
    }

	SString fullpath(SString::Literal,buffer + prefixLen);

    //Check if the resolved path is a UNC. By default we assume relative path to resolve to disk
    if (fullpath.BeginsWith(SL(UNCPathPrefix)) && prefixLen != prefix.GetCount() - (COUNT_T)u16_strlen(UNCPATHPREFIX))
    {
        //Remove the leading '\\' from the UNC path to be replaced with UNCExtendedPathPrefix
        fullpath.Replace(fullpath.Begin(), (COUNT_T)u16_strlen(UNCPATHPREFIX), SL(UNCExtendedPathPrefix));
        path.CloseBuffer();
        path.Set(fullpath);
    }
    else
    {
        //wcscpy_s always termintes with NULL, so we are saving the character that will be overwriiten
        WCHAR temp = buffer[prefix.GetCount()];
        wcscpy_s(buffer, prefix.GetCount() + 1, prefix.GetUnicode());
        buffer[prefix.GetCount()] = temp;
        path.CloseBuffer(ret + prefixLen);
    }

    return S_OK;
}

BOOL LongFile::IsDirectorySeparator(WCHAR c)
{
    return c == DirectorySeparatorChar || c == AltDirectorySeparatorChar;;
}

#endif //HOST_WINDOWS
