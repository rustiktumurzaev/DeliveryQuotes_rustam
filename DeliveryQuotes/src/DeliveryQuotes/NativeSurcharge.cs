using System.Reflection;
using System.Runtime.InteropServices;

namespace DeliveryQuotes;

// Native C component: native/dq_surcharge.c, exported as dq_weight_surcharge.
// The raw P/Invoke declaration is internal; the rest of the app only sees IWeightSurcharge.
internal static class NativeMethods
{
    public const string LibraryName = "deliveryquotes_native";

    static NativeMethods()
    {
        // Documented loading path: if the default probing next to the app does not find the
        // library, look in $DQ_NATIVE_DIR and in native/out (found by walking up from the app folder).
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);
    }

    // int32_t dq_weight_surcharge(int32_t weight_grams);  returns -1 for an invalid weight
    [DllImport(LibraryName, EntryPoint = "dq_weight_surcharge", CallingConvention = CallingConvention.Cdecl)]
    public static extern int WeightSurcharge(int weightGrams);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        foreach (var directory in CandidateDirectories())
        {
            if (NativeLibrary.TryLoad(Path.Combine(directory, FileName()), out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero; // let the runtime try its default probing
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DQ_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            yield return fromEnvironment;
        }

        yield return AppContext.BaseDirectory;

        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && current is not null; i++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "native", "out");
        }
    }

    private static string FileName() =>
        OperatingSystem.IsWindows() ? "deliveryquotes_native.dll"
        : OperatingSystem.IsMacOS() ? "libdeliveryquotes_native.dylib"
        : "libdeliveryquotes_native.so";
}

// The part of the app that needs a weight surcharge. The workflow depends on this interface,
// so tests can replace it with a failing double while one test still calls the real library.
public interface IWeightSurcharge
{
    Result<int> For(int weightGrams);
}

// Adapter around the native call.
//  - validates the domain input BEFORE calling C,
//  - turns loading and binding problems into Result failures instead of exceptions.
// The native function uses only int arguments and a return value, so no native memory is allocated
// and there is nothing to release.
public sealed class NativeWeightSurcharge : IWeightSurcharge
{
    public const int MinWeightGrams = 1;
    public const int MaxWeightGrams = 30_000;

    private readonly Func<int, int> _native;

    public NativeWeightSurcharge()
        : this(NativeMethods.WeightSurcharge)
    {
    }

    // The delegate is injectable so a test can simulate "library missing" without touching files.
    public NativeWeightSurcharge(Func<int, int> native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    public Result<int> For(int weightGrams)
    {
        if (weightGrams < MinWeightGrams || weightGrams > MaxWeightGrams)
        {
            return Result<int>.Fail(
                ErrorCode.InvalidRequest,
                $"Weight {weightGrams} g is outside {MinWeightGrams}-{MaxWeightGrams} g, the native surcharge was not called.");
        }

        try
        {
            var surcharge = _native(weightGrams);
            return surcharge < 0
                ? Result<int>.Fail(ErrorCode.NativeFailure, $"The native library rejected {weightGrams} g (returned {surcharge}).")
                : Result<int>.Ok(surcharge);
        }
        catch (DllNotFoundException ex)
        {
            return Result<int>.Fail(ErrorCode.NativeUnavailable,
                $"Native library '{NativeMethods.LibraryName}' was not found. Build it first (see native/build.sh or build.cmd). {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result<int>.Fail(ErrorCode.NativeUnavailable,
                $"Native library has no function 'dq_weight_surcharge'. {ex.Message}");
        }
        catch (BadImageFormatException ex)
        {
            return Result<int>.Fail(ErrorCode.NativeUnavailable,
                $"Native library has the wrong architecture for this process (needs a build for the same bitness as .NET). {ex.Message}");
        }
    }
}
