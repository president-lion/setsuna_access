# Captures the SETSUNA window to a PNG, for dev self-tests. Usage: shot.ps1 out.png
param([string]$Out)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
Add-Type -AssemblyName System.Drawing
$p = Get-Process SETSUNA -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "game gone"; exit 1 }
[W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object W+R
[W]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.Ri - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out)
Write-Output "saved $Out $($bmp.Width)x$($bmp.Height) title='$($p.MainWindowTitle)'"
