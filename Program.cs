using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// ============================================================================
// GLOBAL DEĞIŞKENLER (Şimdi yerel ama metodlar erişebilir)
// ============================================================================
string[] threadStates;
long[] threadCycles;
long[] threadBytesProcessed;
long[] threadErrors;
long[] threadMatrixOps; // New Counter for CPU Stress
ConcurrentBag<ErrorDetail> errorLog = new();
long totalErrors = 0;
long totalCycles = 0;
CancellationTokenSource cts = new();
Stopwatch sw = Stopwatch.StartNew();
long allocated = 0;

// ============================================================================
// TEST DESENLERİ
// ============================================================================
byte[] basicPatterns = { 0x00, 0xFF, 0xAA, 0x55, 0xCC, 0x33 };
byte[] walkingBits = { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80 };
byte[] walkingOnes = { 0xFE, 0xFD, 0xFB, 0xF7, 0xEF, 0xDF, 0xBF, 0x7F };

Console.Clear();
Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║      🔥 RAM & CPU STRESS TEST TOOL v3.1 - Dynamic Allocation          ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝\n");

// ============================================================================
// MEASURE SYSTEM MEMORY
// ============================================================================
var memStatus = new NativeMethods.MEMORYSTATUSEX();
if (!NativeMethods.GlobalMemoryStatusEx(memStatus))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("❌ ERROR: Could not retrieve system memory info!");
    Console.ResetColor();
    return;
}

ulong totalRAM = memStatus.ullTotalPhys;
ulong availableRAM = memStatus.ullAvailPhys;
ulong usedRAM = totalRAM - availableRAM;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("📊 SYSTEM MEMORY STATUS:");
Console.WriteLine(new string('─', 74));
Console.WriteLine($"💾 Total RAM         : {totalRAM / (1024.0 * 1024 * 1024):F2} GB");
Console.WriteLine($"✅ Available RAM     : {availableRAM / (1024.0 * 1024 * 1024):F2} GB");
Console.WriteLine($"📈 Used RAM          : {usedRAM / (1024.0 * 1024 * 1024):F2} GB ({memStatus.dwMemoryLoad}%)");
Console.ResetColor();

const long safetyMargin = 500L * 1024 * 1024; // 500 MB
long targetBytes = (long)availableRAM - safetyMargin;

if (targetBytes <= 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("\n❌ ERROR: Not enough free RAM! At least 1 GB is required.");
    Console.ResetColor();
    return;
}

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"\n🎯 TARGET ALLOCATION : {targetBytes / (1024.0 * 1024 * 1024):F2} GB");
Console.WriteLine($"🛡️  SAFETY MARGIN     : {safetyMargin / (1024.0 * 1024):F0} MB");
Console.ResetColor();

Console.WriteLine("\n⏳ Allocating memory...\n");

// ============================================================================
// MEMORY ALLOCATION
// ============================================================================
const int blockSize = 256 * 1024 * 1024; // 256 MB
int threadCount = Environment.ProcessorCount;

var threadBlocks = new List<byte[]>[threadCount];
for (int i = 0; i < threadCount; i++)
    threadBlocks[i] = new List<byte[]>();

allocated = 0;
int currentThread = 0;
var allocationSw = Stopwatch.StartNew();

try
{
    while (allocated < targetBytes)
    {
        long remaining = targetBytes - allocated;
        int thisBlockSize = (int)Math.Min(remaining, blockSize);

        byte[] block = GC.AllocateUninitializedArray<byte>(thisBlockSize, pinned: true);
        threadBlocks[currentThread].Add(block);
        allocated += thisBlockSize;

        if (allocated % (5L * 1024 * 1024 * 1024) == 0 || allocated >= targetBytes)
        {
            Console.WriteLine($"✓ {allocated / (1024.0 * 1024 * 1024):F2} GB / {targetBytes / (1024.0 * 1024 * 1024):F2} GB allocated");
        }

        currentThread = (currentThread + 1) % threadCount;
    }
}
catch (OutOfMemoryException)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n⚠️  WARNING: Could not allocate full {targetBytes / (1024.0 * 1024 * 1024):F2} GB.");
    Console.WriteLine($"✓ {allocated / (1024.0 * 1024 * 1024):F2} GB allocated. Testing will continue with this amount.");
    Console.ResetColor();
}

allocationSw.Stop();

Console.Clear();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                    ✅ ALLOCATION COMPLETE                              ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine($"💾 Allocated         : {allocated / (1024.0 * 1024 * 1024):F2} GB");
Console.WriteLine($"⏱️  Allocation Time   : {allocationSw.Elapsed:mm\\:ss}");
Console.WriteLine($"⚙️  Thread Count      : {threadCount}");
Console.WriteLine("\n🚀 Starting test... (Press CTRL+C to stop)\n");

Thread.Sleep(2000);

// ============================================================================
// INITIALIZE GLOBAL ARRAYS
// ============================================================================
threadStates = new string[threadCount];
threadCycles = new long[threadCount];
threadBytesProcessed = new long[threadCount];
threadErrors = new long[threadCount];
threadMatrixOps = new long[threadCount];
Array.Fill(threadStates, "Preparing...");

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ============================================================================
// START WORKER THREADS
// ============================================================================
var tasks = new Task[threadCount];
for (int i = 0; i < threadCount; i++)
{
    int id = i;
    tasks[i] = Task.Run(() => Worker(id, threadBlocks[id]), cts.Token);
}

// ============================================================================
// START DASHBOARD THREAD
// ============================================================================
_ = Task.Run(async () => await DashboardLoop(), cts.Token);

// ============================================================================
// WAIT FOR COMPLETION
// ============================================================================
await Task.WhenAll(tasks);

// ============================================================================
// GENERATE FINAL REPORT
// ============================================================================
GenerateFinalReport();

// ============================================================================
// LOCAL HELPER METHODS (LOCAL FUNCTIONS - capturing local scope)
// ============================================================================

void LogError(int threadId, string testName, long address, byte expected, byte actual, string info = "")
{
    Interlocked.Increment(ref totalErrors);
    Interlocked.Increment(ref threadErrors[threadId]);

    byte xor = (byte)(expected ^ actual);
    string binaryDiff = Convert.ToString(xor, 2).PadLeft(8, '0');

    var error = new ErrorDetail(
        DateTime.Now,
        threadId,
        testName,
        address,
        expected,
        actual,
        binaryDiff,
        info
    );

    errorLog.Add(error);
}

void Test1_PatternWriteVerify(int threadId, byte[] block, byte pattern)
{
    threadStates[threadId] = $"[T1] Pattern: 0x{pattern:X2}";
    Array.Fill(block, pattern);

    for (int i = 0; i < block.Length; i++)
    {
        if (block[i] != pattern)
            LogError(threadId, "Pattern Test", i, pattern, block[i]);
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * 2);
}

void Test2_InversePattern(int threadId, byte[] block, byte pattern)
{
    threadStates[threadId] = $"[T2] Inverse: 0x{pattern:X2}";

    Array.Fill(block, pattern);
    for (int i = 0; i < block.Length; i++)
        if (block[i] != pattern)
            LogError(threadId, "Inverse-1", i, pattern, block[i]);

    byte inverse = (byte)~pattern;
    Array.Fill(block, inverse);
    for (int i = 0; i < block.Length; i++)
        if (block[i] != inverse)
            LogError(threadId, "Inverse-2", i, inverse, block[i]);

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * 4);
}

void Test3_WalkingBit(int threadId, byte[] block)
{
    threadStates[threadId] = "[T3] Walking Bit";

    foreach (var bit in walkingBits)
    {
        Array.Fill(block, bit);
        for (int i = 0; i < block.Length; i++)
            if (block[i] != bit)
                LogError(threadId, "Walking Bit", i, bit, block[i], $"Bit: 0x{bit:X2}");
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * walkingBits.Length * 2);
}

void Test4_MarchC(int threadId, byte[] block)
{
    threadStates[threadId] = "[T4] March C- (Full)";

    // 1. Write 0 (Up)
    Array.Fill(block, (byte)0x00);

    // 2. Read 0, Write 1 (Up)
    for (int i = 0; i < block.Length; i++)
    {
        if (block[i] != 0x00) LogError(threadId, "March C- S2", i, 0x00, block[i]);
        block[i] = 0xFF;
    }

    // 3. Read 1, Write 0 (Up)
    for (int i = 0; i < block.Length; i++)
    {
        if (block[i] != 0xFF) LogError(threadId, "March C- S3", i, 0xFF, block[i]);
        block[i] = 0x00;
    }

    // 4. Read 0, Write 1 (Down)
    for (int i = block.Length - 1; i >= 0; i--)
    {
        if (block[i] != 0x00) LogError(threadId, "March C- S4", i, 0x00, block[i]);
        block[i] = 0xFF;
    }

    // 5. Read 1, Write 0 (Down)
    for (int i = block.Length - 1; i >= 0; i--)
    {
        if (block[i] != 0xFF) LogError(threadId, "March C- S5", i, 0xFF, block[i]);
        block[i] = 0x00;
    }

    // 6. Read 0 (Down)
    for (int i = block.Length - 1; i >= 0; i--)
    {
        if (block[i] != 0x00) LogError(threadId, "March C- S6", i, 0x00, block[i]);
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * 6);
}

void Test5_RandomPageAccess(int threadId, byte[] block, Random rnd)
{
    threadStates[threadId] = "[T5] Random Page";

    for (int i = 0; i < block.Length; i += 4096)
    {
        byte value = (byte)rnd.Next(0, 256);
        block[i] = value;

        if (block[i] != value)
            LogError(threadId, "Random Page", i, value, block[i], $"Page: {i / 4096}");
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length / 2048);
}

void Test6_BandwidthStress(int threadId, byte[] block)
{
    threadStates[threadId] = "[T6] Bandwidth";
    int half = block.Length / 2;
    Buffer.BlockCopy(block, 0, block, half, half);
    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length);
}

void Test7_WalkingOnes(int threadId, byte[] block)
{
    threadStates[threadId] = "[T7] Walking Ones";

    foreach (var pattern in walkingOnes)
    {
        Array.Fill(block, pattern);
        for (int i = 0; i < block.Length; i++)
            if (block[i] != pattern)
                LogError(threadId, "Walking Ones", i, pattern, block[i]);
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * walkingOnes.Length * 2);
}

void Test8_AddressTest(int threadId, byte[] block)
{
    threadStates[threadId] = "[T8] Address Test";

    for (int i = 0; i < block.Length; i++)
        block[i] = (byte)(i & 0xFF);

    for (int i = 0; i < block.Length; i++)
    {
        byte expected = (byte)(i & 0xFF);
        if (block[i] != expected)
            LogError(threadId, "Address Test", i, expected, block[i], $"Addr: 0x{i:X8}");
    }

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * 2);
}

void Test9_SolidBits(int threadId, byte[] block)
{
    threadStates[threadId] = "[T9] Solid Bits";

    Array.Fill(block, (byte)0xFF);
    Thread.Sleep(100);

    for (int i = 0; i < block.Length; i++)
        if (block[i] != 0xFF)
            LogError(threadId, "Solid-1 Decay", i, 0xFF, block[i], "REFRESH ERROR!");

    Array.Fill(block, (byte)0x00);
    Thread.Sleep(100);

    for (int i = 0; i < block.Length; i++)
        if (block[i] != 0x00)
            LogError(threadId, "Solid-0 Decay", i, 0x00, block[i], "REFRESH ERROR!");

    Interlocked.Add(ref threadBytesProcessed[threadId], block.Length * 4);
}

void Test10_CPUMatrixStress(int threadId)
{
    threadStates[threadId] = "[T10] CPU Matrix Stress";
    
    // 64x64 Matrix Multiplication (Floating Point Heavy)
    const int N = 64;
    double[,] A = new double[N, N];
    double[,] B = new double[N, N];
    double[,] C = new double[N, N];

    // Initialize massive random values using math functions to burn CPU
    for(int i=0; i<N; i++)
    {
        for(int j=0; j<N; j++)
        {
            A[i, j] = Math.Sin(i) * Math.Cos(j);
            B[i, j] = Math.Tan(i + j);
        }
    }

    // Multiply
    for (int i = 0; i < N; i++)
    {
        for (int j = 0; j < N; j++)
        {
            double sum = 0;
            for (int k = 0; k < N; k++)
            {
                sum += A[i, k] * B[k, j];
            }
            C[i, j] = sum;
        }
    }

    Interlocked.Increment(ref threadMatrixOps[threadId]);
}


void Worker(int id, List<byte[]> myBlocks)
{
    // Better seeding strategy
    var rnd = new Random(Guid.NewGuid().GetHashCode());
    long localCycles = 0;

    while (!cts.Token.IsCancellationRequested)
    {
        foreach (var block in myBlocks)
        {
            foreach (var pattern in basicPatterns)
            {
                Test1_PatternWriteVerify(id, block, pattern);
                Test2_InversePattern(id, block, pattern);
            }

            Test3_WalkingBit(id, block);
            Test4_MarchC(id, block);
            Test5_RandomPageAccess(id, block, rnd);
            Test6_BandwidthStress(id, block);
            Test7_WalkingOnes(id, block);
            Test8_AddressTest(id, block);
            Test9_SolidBits(id, block);
            
            // Run CPU Stress after memory operations
            for(int k=0; k<5; k++) Test10_CPUMatrixStress(id); 
        }

        localCycles++;
        threadCycles[id] = localCycles;
        Interlocked.Increment(ref totalCycles);
    }

    threadStates[id] = "✓ Stopped";
}

async Task DashboardLoop()
{
    int startTop = Console.CursorTop;

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            Console.SetCursorPosition(0, startTop);

            var elapsed = sw.Elapsed;
            long totalBytes = threadBytesProcessed.Sum();
            long totalMatrixOps = threadMatrixOps.Sum();
            double gbProcessed = totalBytes / (1024.0 * 1024 * 1024);
            double throughputGBps = gbProcessed / elapsed.TotalSeconds;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║ 🔥 RAM & CPU STRESS TEST DASHBOARD v3.1                               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⏱️  ELAPSED TIME      : {elapsed:hh\\:mm\\:ss}");
            Console.WriteLine($"💾 ACTIVE MEMORY     : {allocated / (1024.0 * 1024 * 1024):F2} GB");
            Console.WriteLine($"🔄 TOTAL CYCLES      : {Interlocked.Read(ref totalCycles):N0}");
            Console.WriteLine($"📊 DATA PROCESSED    : {gbProcessed:F2} GB");
            Console.WriteLine($"⚡ THROUGHPUT        : {throughputGBps:F2} GB/s");
            Console.WriteLine($"🧮 MATRIX OPS        : {totalMatrixOps:N0}");
            Console.WriteLine($"❌ TOTAL ERRORS      : {Interlocked.Read(ref totalErrors):N0}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n" + new string('─', 74));
            Console.WriteLine("📌 THREAD STATUS:");
            Console.WriteLine(new string('─', 74));
            Console.ResetColor();

            for (int i = 0; i < threadStates.Length; i++)
            {
                double threadGB = threadBytesProcessed[i] / (1024.0 * 1024 * 1024);
                long errors = threadErrors[i];
                string status = errors == 0 ? "✓" : "⚠";
                Console.WriteLine($"{status} T{i,2} | {threadStates[i],-35} | {threadCycles[i],6} | {threadGB,6:F1}GB | {errors,4} err".PadRight(74));
            }

            if (totalErrors > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n" + new string('─', 74));
                Console.WriteLine("⚠️  LAST 5 ERRORS:");
                Console.WriteLine(new string('─', 74));

                var recentErrors = errorLog.OrderByDescending(e => e.Timestamp).Take(5);
                foreach (var err in recentErrors)
                {
                    Console.WriteLine($"{err.Timestamp:HH:mm:ss} T{err.ThreadId} [{err.TestName}] @ 0x{err.Address:X8}".PadRight(74));
                    Console.WriteLine($"  Expected: 0x{err.Expected:X2}  Actual: 0x{err.Actual:X2}  Diff: {err.BitDiff}".PadRight(74));
                }
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n" + new string('═', 74));
            Console.WriteLine("💡 Run for at least 24-48 hours | Press CTRL+C to stop".PadRight(74));
            Console.ResetColor();

            await Task.Delay(250);
        }
        catch { }
    }
}

void GenerateFinalReport()
{
    Console.Clear();
    var finalElapsed = sw.Elapsed;

    if (totalErrors > 0)
    {
        string reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"RAM_Error_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              🔴 RAM ERROR REPORT - DETAILED ANALYSIS                   ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"📅 Report Date         : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"⏱️  Duration            : {finalElapsed:hh\\:mm\\:ss}");
        sb.AppendLine($"💾 Tested Memory       : {allocated / (1024.0 * 1024 * 1024):F2} GB");
        sb.AppendLine($"❌ Total Errors        : {totalErrors:N0}");
        sb.AppendLine();
        sb.AppendLine(new string('═', 74));
        sb.AppendLine("📊 THREAD STATISTICS");
        sb.AppendLine(new string('═', 74));

        for (int i = 0; i < threadStates.Length; i++)
        {
            if (threadErrors[i] > 0)
            {
                sb.AppendLine($"Thread {i}: {threadErrors[i]:N0} errors | {threadCycles[i]:N0} cycles | " +
                                $"{threadBytesProcessed[i] / (1024.0 * 1024 * 1024):F2} GB processed");
            }
        }

        sb.AppendLine();
        sb.AppendLine(new string('═', 74));
        sb.AppendLine("🔍 ERRORS BY TEST TYPE");
        sb.AppendLine(new string('═', 74));

        var errorsByTest = errorLog.GroupBy(e => e.TestName)
                                    .OrderByDescending(g => g.Count());

        foreach (var group in errorsByTest)
        {
            sb.AppendLine($"{group.Key,-30}: {group.Count(),8:N0} errors");
        }

        sb.AppendLine();
        sb.AppendLine(new string('═', 74));
        sb.AppendLine($"📝 ALL ERRORS ({totalErrors:N0})");
        sb.AppendLine(new string('═', 74));
        sb.AppendLine();

        int errorNum = 1;
        foreach (var err in errorLog.OrderBy(e => e.Timestamp))
        {
            sb.AppendLine($"[{errorNum++}] {err.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"    Thread       : {err.ThreadId}");
            sb.AppendLine($"    Test         : {err.TestName}");
            sb.AppendLine($"    Address      : 0x{err.Address:X16}");
            sb.AppendLine($"    Expected     : 0x{err.Expected:X2} (Binary: {Convert.ToString(err.Expected, 2).PadLeft(8, '0')})");
            sb.AppendLine($"    Actual       : 0x{err.Actual:X2} (Binary: {Convert.ToString(err.Actual, 2).PadLeft(8, '0')})");
            sb.AppendLine($"    Bad Bits     : {err.BitDiff}");
            if (!string.IsNullOrEmpty(err.AdditionalInfo))
                sb.AppendLine($"    Info         : {err.AdditionalInfo}");
            sb.AppendLine();
        }

        sb.AppendLine(new string('═', 74));
        sb.AppendLine("⚠️  RECOMMENDATIONS:");
        sb.AppendLine(new string('═', 74));
        sb.AppendLine("• Check your RAM modules (Try Memtest86+)");
        sb.AppendLine("• Test modules in different slots");
        sb.AppendLine("• Check RAM voltage and timings in BIOS");
        sb.AppendLine("• If errors persist, replace the defective RAM");

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     ⚠️  ERRORS DETECTED!                               ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine($"\n❌ Total {totalErrors:N0} memory errors detected!");
        Console.WriteLine($"📄 Detailed report saved: {reportPath}");
        Console.WriteLine($"⏱️  Duration: {finalElapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"💾 Tested: {allocated / (1024.0 * 1024 * 1024):F2} GB");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n⚠️  ADVICE: Check RAM modules or contact service!");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      ✅ TEST SUCCESSFUL!                               ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        long totalBytes = threadBytesProcessed.Sum();
        double finalGB = totalBytes / (1024.0 * 1024 * 1024);

        Console.WriteLine($"\n✅ No errors detected!");
        Console.WriteLine($"⏱️  Duration        : {finalElapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"💾 Tested           : {allocated / (1024.0 * 1024 * 1024):F2} GB");
        Console.WriteLine($"🔄 Total Cycles     : {totalCycles:N0}");
        Console.WriteLine($"📊 Data Processed   : {finalGB:F2} GB");
        Console.WriteLine($"⚡ Avg Speed        : {finalGB / finalElapsed.TotalSeconds:F2} GB/s");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🎉 Your RAM and CPU passed the stress test successfully!");
        Console.ResetColor();
    }

    Console.WriteLine("\n🛑 Test stopped.");
}

// ============================================================================
// HATA KAYIT YAPISI (RECORD)
// ============================================================================
record ErrorDetail(
    DateTime Timestamp,
    int ThreadId,
    string TestName,
    long Address,
    byte Expected,
    byte Actual,
    string BitDiff,
    string AdditionalInfo
);

// ============================================================================
// WINDOWS API - BELLEK BİLGİSİ (HELPER CLASS)
// ============================================================================
static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}