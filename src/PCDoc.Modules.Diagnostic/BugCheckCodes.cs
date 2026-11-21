namespace PCDoc.Modules.Diagnostic;

/// <summary>Codes d'arrêt courants (écrans bleus) : nom officiel et famille.</summary>
public static class BugCheckCodes
{
    private static readonly Dictionary<uint, (string Name, BugCheckCategory Category)> Known = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", BugCheckCategory.Driver),
        [0x19] = ("BAD_POOL_HEADER", BugCheckCategory.Driver),
        [0x1A] = ("MEMORY_MANAGEMENT", BugCheckCategory.Memory),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", BugCheckCategory.Driver),
        [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", BugCheckCategory.Driver),
        [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", BugCheckCategory.Memory),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", BugCheckCategory.Disk),
        [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", BugCheckCategory.Driver),
        [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", BugCheckCategory.Hardware),
        [0x9F] = ("DRIVER_POWER_STATE_FAILURE", BugCheckCategory.Driver),
        [0xC2] = ("BAD_POOL_CALLER", BugCheckCategory.Driver),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", BugCheckCategory.Driver),
        [0xEF] = ("CRITICAL_PROCESS_DIED", BugCheckCategory.System),
        [0xF4] = ("CRITICAL_OBJECT_TERMINATION", BugCheckCategory.Disk),
        [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", BugCheckCategory.Hardware),
        [0x109] = ("CRITICAL_STRUCTURE_CORRUPTION", BugCheckCategory.Memory),
        [0x116] = ("VIDEO_TDR_FAILURE", BugCheckCategory.Driver),
        [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", BugCheckCategory.Driver),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", BugCheckCategory.Hardware),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", BugCheckCategory.Driver),
        [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", BugCheckCategory.Driver),
        [0x154] = ("UNEXPECTED_STORE_EXCEPTION", BugCheckCategory.Disk),
        [0x1E0] = ("ATTEMPTED_SWITCH_FROM_DPC", BugCheckCategory.Driver),
        [0xC000021A] = ("STATUS_SYSTEM_PROCESS_TERMINATED", BugCheckCategory.System),
    };

    public static (string Name, BugCheckCategory Category) Describe(uint code) =>
        Known.TryGetValue(code, out var known) ? known : ($"0x{code:X8}", BugCheckCategory.Unknown);
}
