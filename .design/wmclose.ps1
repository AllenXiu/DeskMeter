Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinC {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
}
'@
$targets = (Get-Process DeskMeter -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
$script:list = New-Object System.Collections.ArrayList
$cb = {
  param($h, $l)
  $pid2 = 0
  [WinC]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
  $vis = [WinC]::IsWindowVisible($h)
  $null = $script:list.Add(@{ h = $h; pid = $pid2; vis = $vis })
  return $true
}
[WinC]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
$sent = 0
foreach ($t in $targets) {
  $wins = $script:list | Where-Object { $_.pid -eq $t -and $_.vis }
  foreach ($w in $wins) {
    $ok = [WinC]::PostMessage($w.h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Write-Output ("pid {0} hwnd {1} WM_CLOSE sent={2}" -f $t, $w.h, $ok)
    $sent++
  }
}
Start-Sleep -Seconds 2
if (Get-Process DeskMeter -ErrorAction SilentlyContinue) { Write-Output "STILL ALIVE" } else { Write-Output "EXITED" }
