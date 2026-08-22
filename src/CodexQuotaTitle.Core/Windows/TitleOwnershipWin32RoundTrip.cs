using System.Runtime.Versioning;
using CodexQuotaTitle.Titles;

namespace CodexQuotaTitle.Windows;

public sealed record TitleOwnershipWin32RoundTripResult(
    string BaseTitle,
    string WrittenTitle,
    string ReadTitle,
    string RecoveredBaseTitle,
    bool SentinelsSurvived);

[SupportedOSPlatform("windows")]
public static class TitleOwnershipWin32RoundTrip
{
    public static TitleOwnershipWin32RoundTripResult Run()
    {
        const string baseTitle = "CodexQuotaTitle controlled Win32 test";
        var handle = NativeWindowApi.CreateWindowExW(
            0,
            "STATIC",
            baseTitle,
            0,
            0,
            0,
            0,
            0,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("Unable to create the controlled Win32 test window.");
        }

        try
        {
            var ownership = new TitleOwnership();
            var writtenTitle = ownership.Apply(NativeWindowApi.GetWindowTitle(handle), "quota ", " 63%");
            if (!NativeWindowApi.SetWindowTextW(handle, writtenTitle))
            {
                throw new InvalidOperationException("Unable to set the controlled Win32 test window title.");
            }

            var readTitle = NativeWindowApi.GetWindowTitle(handle);
            var recoveredBaseTitle = ownership.GetBaseTitle(readTitle);
            return new TitleOwnershipWin32RoundTripResult(
                baseTitle,
                writtenTitle,
                readTitle,
                recoveredBaseTitle,
                string.Equals(writtenTitle, readTitle, StringComparison.Ordinal));
        }
        finally
        {
            NativeWindowApi.DestroyWindow(handle);
        }
    }
}
