Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$sig = @'
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
'@
$w = Add-Type -MemberDefinition $sig -Name "ME" -Namespace "W" -PassThru
function LClick { $w::mouse_event(0x0002, 0,0,0, [UIntPtr]::Zero); Start-Sleep -m 60; $w::mouse_event(0x0004, 0,0,0, [UIntPtr]::Zero) }
function RClick { $w::mouse_event(0x0008, 0,0,0, [UIntPtr]::Zero); Start-Sleep -m 60; $w::mouse_event(0x0010, 0,0,0, [UIntPtr]::Zero) }
LClick
Start-Sleep -m 120
LClick
Write-Output "double-clicked"
