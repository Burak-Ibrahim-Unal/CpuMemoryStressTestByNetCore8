# 🔥 CpuMemoryStressTest - Advanced RAM Stress Tool (v3.0)

![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/License-MIT-green)

**CpuMemoryStressTest** is a powerful, multithreaded command-line tool designed to stress test your system's RAM and CPU stability. It dynamically allocates available memory and runs multiple rigorous test patterns to detect hardware faults, instability, or overclocking issues.

## 🚀 Features

- **Dynamic Allocation**: Automatically detects available RAM and allocates it safely (leaving ~500MB safety margin).
- **Multithreaded Architecture**: Utilizes all CPU cores to maximize stress on the memory controller.
- **Real-time Dashboard**: Live monitoring of throughput, memory usage, error counts, and per-thread status.
- **Advanced Patterns**: Implements various memory test algorithms including walking bits and march tests.
- **Detailed Reporting**: Generates a comprehensive error report text file on the start of failures.

## 🛠️ Usage

### Prerequisites
- .NET 8.0 Runtime
- Windows OS (uses `GlobalMemoryStatusEx` API)

### Running the Tool
1. Clone the repository:
   ```powershell
   git clone https://github.com/BurakGG/CpuMemoryStressTestDDR5.git
   ```
2. Navigate to the project directory:
   ```powershell
   cd CpuMemoryStressTestDDR5
   ```
3. Run the application:
   ```powershell
   dotnet run
   ```
   *Note: Run as Administrator for best results to ensure memory pages can be locked/pinned effectively.*

4. To stop the test, press `CTRL+C`.

---

## 🔍 Test Patterns

The tool cycles through 9 different test patterns on every allocated memory block:

| ID | Test Name | Description |
|:--:|:---|:---|
| **T1** | **Pattern Write/Verify** | Writes a static pattern (e.g., 0xAA) and validates it immediately. |
| **T2** | **Inverse Pattern** | Writes the bitwise inverse of the pattern and validates. Tests 0->1 and 1->0 transitions. |
| **T3** | **Walking Bit** | Walks a single 1 bit across a field of 0s (0x01, 0x02, ... 0x80) to detect crosstalk. |
| **T4** | **March Test (Simp.)** | A rigorous march algorithm testing for Stuck-At Faults (SAF) and Transition Faults (TF). |
| **T5** | **Random Page Access** | Writes random values to random pages to stress the memory controller and TLB. |
| **T6** | **Bandwidth Stress** | Performs large block copies (`Buffer.BlockCopy`) to saturate memory bandwidth. |
| **T7** | **Walking Ones** | Similar to T3 but walks a single 0 bit (inverse walking bit). |
| **T8** | **Address Test** | Writes the address itself as data to detect addressing errors (aliasing). |
| **T9** | **Solid Bits Decay** | Writes all 1s or all 0s, waits 100ms, and reads back to check for cell charge leakage. |

---

## 📊 Dashboard Preview

```text
╔════════════════════════════════════════════════════════════════════════╗
║ 🔥 RAM STRESS TEST DASHBOARD v3.0                                     ║
╚════════════════════════════════════════════════════════════════════════╝
⏱️  ELAPSED TIME      : 00:45:12
💾 ACTIVE MEMORY     : 30.50 GB
🔄 TOTAL CYCLES      : 1,250,432
⚡ THROUGHPUT        : 45.20 GB/s
❌ TOTAL ERRORS      : 0

📌 THREAD STATUS:
──────────────────────────────────────────────────────────────────────────
✓ T 0 | [T4] March C-                       |   1250 |   45.2GB |    0 err
✓ T 1 | [T5] Random Page                    |   1248 |   44.8GB |    0 err
...
```

## ⚠️ Disclaimer

**Use at your own risk.** Stress testing hardware puts a heavy load on your components. Ensure your cooling system is adequate. The authors are not responsible for any hardware damage or data loss resulting from the use of this tool.
