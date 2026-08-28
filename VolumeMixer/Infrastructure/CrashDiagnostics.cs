using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace VolumeMixer.Infrastructure;

/// <summary>
/// Diagnóstico de crash nativo. O app morre em silêncio (sem log de erro e sem
/// "encerrando pelo menu") quando uma exceção nativa (SEH) — ex.: access
/// violation em COM/GDI+ — derruba o processo sem disparar os handlers
/// gerenciados. Este helper:
///  1. Instala um SetUnhandledExceptionFilter que grava um log mínimo do crash
///     nativo (código + endereço) num arquivo separado.
///  2. Configura WER LocalDumps (HKCU) para gerar um minidump do processo no
///     crash, permitindo análise posterior.
///  3. Inicia um heartbeat que registra a cada 30 segundos, para medir quanto
///     tempo o app roda antes de morrer.
/// </summary>
internal static class CrashDiagnostics
{
    private static IntPtr _logHandle = IntPtr.Zero;
    private static UnhandledExceptionFilterDelegate? _filterDelegate;
    private static DispatcherTimer? _heartbeat;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetUnhandledExceptionFilter(IntPtr filter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr file, byte[] buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCreateKeyEx(
        IntPtr hKey, string lpSubKey, int reserved, string? lpClass, int dwOptions,
        int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegSetValueEx(
        IntPtr hKey, string? lpValueName, int reserved, int dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    private const uint GenericWrite = 0x40000000;
    private const uint CreateAlways = 2;
    private const uint FileAttributeNormal = 0x80;
    private const int RegSz = 1;
    private const int RegDword = 4;
    private const int KeyWrite = 0x20006;
    private static readonly IntPtr Hkcu = new(0x80000001);

    public static void Install(string logDirectory)
    {
        // 1) Arquivo de log do crash nativo (aberto uma vez; o filtro usa WriteFile).
        var crashPath = Path.Combine(logDirectory, "native-crash.log");
        try
        {
            _logHandle = CreateFileW(crashPath, GenericWrite, 0, IntPtr.Zero, CreateAlways, FileAttributeNormal, IntPtr.Zero);
        }
        catch { _logHandle = IntPtr.Zero; }

        // 2) Filtro de exceção nativa.
        try
        {
            _filterDelegate = Handler;
            SetUnhandledExceptionFilter(Marshal.GetFunctionPointerForDelegate(_filterDelegate));
        }
        catch { /* best effort */ }

        // 3) WER LocalDumps (HKCU — não exige admin).
        ConfigureWerLocalDumps();

        // 4) Heartbeat: registra a cada 30 segundos para medir a vida do app.
        try
        {
            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _heartbeat.Tick += (_, _) => AppLog.Instance.Info("heartbeat: app rodando");
            _heartbeat.Start();
        }
        catch { /* best effort */ }
    }

    private static void ConfigureWerLocalDumps()
    {
        try
        {
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "VolumeMixer.exe");
            var keyPath = $@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{exeName}";
            if (RegCreateKeyEx(Hkcu, keyPath, 0, null, 0, KeyWrite, IntPtr.Zero, out var key, out _) != 0)
                return;

            var dumpDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VolumeMixer", "crashdumps");
            Directory.CreateDirectory(dumpDir);

            RegSetValueEx(key, "DumpFolder", 0, RegSz, Encoding.Unicode.GetBytes(dumpDir + "\0"), (dumpDir.Length + 1) * 2);
            RegSetValueEx(key, "DumpType", 0, RegDword, BitConverter.GetBytes(2), 4); // 2 = full dump
            RegSetValueEx(key, "DumpCount", 0, RegDword, BitConverter.GetBytes(5), 4);
            RegCloseKey(key);
        }
        catch { /* best effort */ }
    }

    /// <summary>Filtro chamado pelo Windows em exceção nativa não tratada.</summary>
    private static uint Handler(IntPtr exceptionPointers)
    {
        // exceptionPointers → EXCEPTION_POINTERS { ExceptionRecord*, ContextRecord* }
        // ExceptionRecord → { DWORD ExceptionCode; ...; PVOID ExceptionAddress; ... }
        // Lemos o código e o endereço via Marshal (sem alocar gerenciado).
        try
        {
            var code = 0u;
            var address = IntPtr.Zero;
            if (exceptionPointers != IntPtr.Zero)
            {
                var record = Marshal.ReadIntPtr(exceptionPointers);
                if (record != IntPtr.Zero)
                {
                    code = (uint)Marshal.ReadInt32(record);          // +0 ExceptionCode
                    address = Marshal.ReadIntPtr(record, IntPtr.Size); // +8 ExceptionAddress
                }
            }

            var msg = Encoding.UTF8.GetBytes(
                $"[{DateTime.Now:HH:mm:ss.fff}] CRASH NATIVO (SEH) code=0x{code:X8} addr=0x{address.ToInt64():X}\r\n");
            if (_logHandle != IntPtr.Zero)
                WriteFile(_logHandle, msg, (uint)msg.Length, out _, IntPtr.Zero);
        }
        catch { /* nada a fazer */ }

        return 0; // EXCEPTION_CONTINUE_SEARCH → deixa o Windows encerrar o processo
    }
}
