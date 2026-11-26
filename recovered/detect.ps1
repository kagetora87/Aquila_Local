# Faithful re-implementation of Aquila Local 2.0.1 hardware detection
# (GpuMonitor.GetAdapters + HardwareInventory.DetectAsync + HardwareInventory.SelectGpu),
# reconstructed from the shipped IL. Read-only: enumerates DXGI adapters and runs
# `llama-server.exe --list-devices` exactly like the app does.
param([string]$Exe = 'C:\Aquila\Aquila Local\engine\llama-server.exe', [int]$TimeoutSec = 6)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class AqDxgi {
  [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct AdapterDesc {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    public uint VendorId, DeviceId, SubSysId, Revision;
    public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
    public Luid Luid; public uint Flags;
  }
  [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapterDelegate(IntPtr self, uint index, out IntPtr adapter);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescDelegate(IntPtr self, out AdapterDesc desc);
  public class Adapter { public string Name; public string Key; public ulong DedicatedBytes; public ulong DedicatedSystem; public ulong Shared; public uint Flags; public uint Vendor; public uint Index; public bool Kept; }
  public static List<Adapter> GetAdapters() {
    var list = new List<Adapter>();
    Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
    IntPtr f;
    int hr = CreateDXGIFactory1(ref iid, out f);
    if (hr != 0) throw new Exception("CreateDXGIFactory1 hr=0x" + hr.ToString("X8"));
    try {
      var enumA = (EnumAdapterDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(f), 12 * IntPtr.Size), typeof(EnumAdapterDelegate));
      for (uint i = 0; i < 16; i++) {
        IntPtr a;
        if (enumA(f, i, out a) != 0) break;
        try {
          var gd = (GetDescDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(a), 10 * IntPtr.Size), typeof(GetDescDelegate));
          AdapterDesc d;
          if (gd(a, out d) != 0) continue;
          list.Add(new Adapter {
            Name = d.Description.TrimEnd('\0'),
            Key = ("0x" + d.Luid.High.ToString("X8") + "_0x" + d.Luid.Low.ToString("X8")).ToLowerInvariant(),
            DedicatedBytes = d.DedicatedVideoMemory.ToUInt64(), DedicatedSystem = d.DedicatedSystemMemory.ToUInt64(),
            Shared = d.SharedSystemMemory.ToUInt64(), Flags = d.Flags, Vendor = d.VendorId, Index = i,
            Kept = (d.Flags & 2) == 0 });
        } finally { Marshal.Release(a); }
      }
    } finally { Marshal.Release(f); }
    return list;
  }
}
'@

$GiB = 1GB
"=== 1. DXGI adapters (GpuMonitor.GetAdapters; software adapters (Flags&2) are dropped)"
$adapters = [AqDxgi]::GetAdapters()
$adapters | ForEach-Object {
  "  [{0}] '{1}' vendor=0x{2:X4} dedicatedVRAM={3:N2} GiB dedicatedSys={4:N2} GiB shared={5:N2} GiB flags={6} kept={7} luid={8}" -f $_.Index, $_.Name, $_.Vendor, ($_.DedicatedBytes/$GiB), ($_.DedicatedSystem/$GiB), ($_.Shared/$GiB), $_.Flags, $_.Kept, $_.Key
}
$kept = @($adapters | Where-Object Kept)

"`n=== 2. llama-server --list-devices (HardwareInventory.DetectAsync, ${TimeoutSec}s timeout)"
$psi = New-Object System.Diagnostics.ProcessStartInfo($Exe, '--list-devices')
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.WorkingDirectory = [IO.Path]::GetDirectoryName($Exe)
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = [Diagnostics.Process]::Start($psi)
$outT = $p.StandardOutput.ReadToEndAsync(); $errT = $p.StandardError.ReadToEndAsync()
$exited = $p.WaitForExit($TimeoutSec * 1000)
$sw.Stop()
$listing = ''
if (-not $exited) {
  "  TIMED OUT after $($sw.ElapsedMilliseconds) ms -> app kills it and listing stays EMPTY"
  try { $p.Kill() } catch {}
} else {
  $p.WaitForExit()
  "  exit code $($p.ExitCode) after $($sw.ElapsedMilliseconds) ms"
  if ($p.ExitCode -eq 0) { $listing = $outT.Result + "`n" + $errT.Result } else { "  non-zero exit -> listing stays EMPTY" }
  "  stdout: " + ($outT.Result -replace "`r", '\r' -replace "`n", '\n')
  "  stderr: " + ($errT.Result -replace "`r", '\r' -replace "`n", '\n')
}

"`n=== 3. SelectGpu (regex Multiline|CultureInvariant, name match OrdinalIgnoreCase, DedicatedBytes >= 1 GiB)"
$rx = New-Object Text.RegularExpressions.Regex('^\s*(Vulkan\d+):\s+(.+?)\s+\((\d+) MiB, (\d+) MiB free\)\s*$', ([Text.RegularExpressions.RegexOptions]::Multiline -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant))
$cands = @()
foreach ($m in $rx.Matches($listing)) {
  $name = $m.Groups[2].Value.Trim()
  "  device line: $($m.Groups[1].Value) name='$name' total=$($m.Groups[3].Value) MiB free=$($m.Groups[4].Value) MiB"
  foreach ($a in $kept) {
    $nameOk = $a.Name.Trim().Equals($name, [StringComparison]::OrdinalIgnoreCase)
    $memOk = $a.DedicatedBytes -ge 1GB
    "     vs DXGI '$($a.Name)': nameMatch=$nameOk dedicated>=1GiB=$memOk"
    if ($nameOk -and $memOk) {
      $free = [int64]$m.Groups[4].Value * 1MB
      $cands += [pscustomobject]@{ Device = $m.Groups[1].Value; Name = $name; Key = $a.Key; DedicatedBytes = $a.DedicatedBytes; FreeBytes = [Math]::Min([double]$free, [double]$a.DedicatedBytes) }
    }
  }
}
$gpu = $cands | Sort-Object @{e = 'DedicatedBytes'; Descending = $true}, @{e = 'FreeBytes'; Descending = $true} | Select-Object -First 1
"`n=== RESULT"
if ($gpu) {
  "  Gpu = $($gpu.Device) '$($gpu.Name)' $([Math]::Round($gpu.DedicatedBytes/$GiB,1)) GiB dedicated, $([Math]::Round($gpu.FreeBytes/$GiB,1)) GiB free"
  $prof = if ($gpu.DedicatedBytes -ge 20GB) { 'High (A)' } elseif ($gpu.DedicatedBytes -gt 0 -and $gpu.DedicatedBytes -le 10GB) { 'Laptop (B) if RAM <= 20 GiB' } else { 'Custom' }
  "  Auto profile would be: $prof"
} else {
  "  Gpu = null  ->  UI shows 'No supported dedicated GPU found'"
}
