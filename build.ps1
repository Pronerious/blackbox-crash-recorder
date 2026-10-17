# Compila dist\BlackBox.exe con il compilatore C# incluso in Windows (.NET Framework 4.x).
# Nessun download e nessuna dipendenza: usa solo file gia' presenti in Windows 10/11.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src  = Join-Path $root 'src'
$obj  = Join-Path $root 'obj'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null

# ---- icona (disegnata qui, salvata come .ico con immagini PNG a piu' dimensioni)
Add-Type -AssemblyName System.Drawing
function New-IconPng([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = [Math]::Max(2, $s * 0.22)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90); $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 22, 25, 32))), $path)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 94, 224, 138)), ([Math]::Max(1.6, $s * 0.085))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $pts = @(@(0.12, 0.56), @(0.32, 0.56), @(0.42, 0.30), @(0.54, 0.78), @(0.64, 0.46), @(0.70, 0.56), @(0.88, 0.56)) |
        ForEach-Object { New-Object Drawing.PointF ([float]($_[0] * $s)), ([float]($_[1] * $s)) }
    $g.DrawLines($pen, [Drawing.PointF[]]$pts)
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 255, 92, 108))), [float]($s * 0.70), [float]($s * 0.10), [float]($s * 0.20), [float]($s * 0.20))
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 256
$pngs = $sizes | ForEach-Object { , (New-IconPng $_) }
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$icoPath = Join-Path $obj 'app.ico'
[IO.File]::WriteAllBytes($icoPath, $ico.ToArray())

# ---- compilazione
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$refs = @("$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
          "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\System.Core.dll", "$fw\System.Xml.dll") | ForEach-Object { "/r:$_" }
$exe = Join-Path $dist 'BlackBox.exe'
& "$fw\csc.exe" /nologo /nowarn:0414,0168,0219 /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$icoPath" `
    "/resource:$src\MainWindow.xaml,MainWindow.xaml" "/resource:$icoPath,app.ico" "/resource:$root\GUIDA.md,GUIDA.md" @refs "$src\App.cs" "$src\Core.cs" "$src\Sessions.cs"
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita" }
"OK -> $exe"
