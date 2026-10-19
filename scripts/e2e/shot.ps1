[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Name,
    [int]$H = 320
)
Add-Type -AssemblyName System.Windows.Forms
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$h2 = [Math]::Min($H, $b.Height)
$bmp = New-Object System.Drawing.Bitmap($b.Width, $h2)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$out = "C:\Users\dk900\WorkBuddy AI\win11-tendedero\scripts\e2e\shots\$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $out"
