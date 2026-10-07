# Sends a key script to the running SETSUNA window, for dev self-tests.
# Usage: keys.ps1 "wait:25 Return wait:3 W S F12"   (wait:N = seconds; other tokens = Keys names)
param([string]$Script)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class K {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
}
"@
Add-Type -AssemblyName System.Windows.Forms
foreach ($tok in $Script.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
  if ($tok.StartsWith('wait:')) { Start-Sleep -Milliseconds ([double]$tok.Substring(5) * 1000); continue }
  $p = Get-Process SETSUNA -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $p) { Write-Output "game gone"; exit 1 }
  [K]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  $vk = [byte][System.Windows.Forms.Keys]$tok
  $sc = [byte][K]::MapVirtualKey($vk, 0)
  [K]::keybd_event($vk, $sc, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 120
  [K]::keybd_event($vk, $sc, 2, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 400
  Write-Output "sent $tok"
}
