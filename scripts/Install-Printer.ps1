# Installs the "Virtual Film Printer" Windows printer, or removes it. Run from an ELEVATED PowerShell:
#   powershell -ExecutionPolicy Bypass -File scripts\Install-Printer.ps1            (install)
#   powershell -ExecutionPolicy Bypass -File scripts\Install-Printer.ps1 -Remove    (remove)
#   add -Port 9101 to use another port (then start the app with --port 9101)
#
# It adds, and -Remove takes away again:
#   - the film sizes as Windows paper forms (8INx10IN ... 14INx17IN, 13INx17IN), named as AMSI Printer maps them;
#   - a Standard TCP/IP port "VFP_127.0.0.1_<port>" that sends every print job to this PC on that port;
#   - the printer, on the XPS driver that ships with Windows ("Microsoft XPS Document Writer v4").
# Nothing else on the machine is touched. VirtualFilmPrinter.exe is the other end of the port.
param(
    [switch]$Remove,
    [int]$Port = 9100,
    [string]$Name = 'Virtual Film Printer'
)
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell (Run as administrator).'
}

$portName = "VFP_127.0.0.1_$Port"

# Film sizes in millimetres, as AMSI Printer's Paper tab maps DICOM film sizes.
$forms = @(
    @{ Name = '8INx10IN';  W = 203; H = 254 },
    @{ Name = '8INx12IN';  W = 203; H = 305 },
    @{ Name = '10INx12IN'; W = 254; H = 305 },
    @{ Name = '10INx14IN'; W = 254; H = 356 },
    @{ Name = '11INx14IN'; W = 279; H = 356 },
    @{ Name = '14INx14IN'; W = 356; H = 356 },
    @{ Name = '14INx17IN'; W = 356; H = 432 },
    @{ Name = '13INx17IN'; W = 330; H = 432 }
)

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class VfpForms {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  struct FORM_INFO_1 { public int Flags; public string Name; public int W; public int H; public int L; public int T; public int R; public int B; }
  [StructLayout(LayoutKind.Sequential)] struct PRINTER_DEFAULTS { public IntPtr DataType; public IntPtr DevMode; public int Access; }
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinter(string n, out IntPtr h, ref PRINTER_DEFAULTS d);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool AddForm(IntPtr h, int level, ref FORM_INFO_1 f);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteForm(IntPtr h, string name);
  [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
  static IntPtr Server() {
    var d = new PRINTER_DEFAULTS { Access = 0x1 }; // SERVER_ACCESS_ADMINISTER
    IntPtr h; if (!OpenPrinter(null, out h, ref d)) throw new System.ComponentModel.Win32Exception(); return h; }
  public static string Add(string name, int wMm, int hMm) {
    var h = Server();
    try {
      var f = new FORM_INFO_1 { Flags = 0, Name = name, W = wMm * 1000, H = hMm * 1000, L = 0, T = 0, R = wMm * 1000, B = hMm * 1000 };
      if (AddForm(h, 1, ref f)) return "added";
      var e = Marshal.GetLastWin32Error();
      return e == 80 ? "already there" : "not added (error " + e + ")"; }
    finally { ClosePrinter(h); } }
  public static string Delete(string name) {
    var h = Server();
    try {
      if (DeleteForm(h, name)) return "removed";
      var e = Marshal.GetLastWin32Error();
      return e == 1902 ? "not there" : "not removed (error " + e + ")"; }
    finally { ClosePrinter(h); } }
}
'@

if ($Remove) {
    if (Get-Printer -Name $Name -ErrorAction SilentlyContinue) { Remove-Printer -Name $Name; "printer '$Name' removed" } else { "printer '$Name' not there" }
    if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) {
        if (Get-Printer | Where-Object { $_.PortName -eq $portName }) { "port $portName kept: another printer still uses it" }
        else { Remove-PrinterPort -Name $portName; "port $portName removed" }
    }
    # Forms are shared by every printer; keep them while another printer on one of our ports remains.
    if (Get-Printer | Where-Object { $_.PortName -like 'VFP_*' }) { 'film paper sizes kept: another Virtual Film Printer remains' }
    else { foreach ($f in $forms) { "paper $($f.Name): " + [VfpForms]::Delete($f.Name) } }
    return
}

foreach ($f in $forms) { "paper $($f.Name): " + [VfpForms]::Add($f.Name, $f.W, $f.H) }

if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) { "port $portName already there" }
else { Add-PrinterPort -Name $portName -PrinterHostAddress '127.0.0.1' -PortNumber $Port; "port $portName added" }

if (Get-Printer -Name $Name -ErrorAction SilentlyContinue) { "printer '$Name' already there" }
else { Add-Printer -Name $Name -DriverName 'Microsoft XPS Document Writer v4' -PortName $portName; "printer '$Name' added" }

Get-Printer -Name $Name | Format-List Name, DriverName, PortName
"Start VirtualFilmPrinter.exe" + $(if ($Port -ne 9100) { " --port $Port" } else { '' }) + ", then print to '$Name'."
