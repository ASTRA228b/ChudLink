# Convert source branding PNGs to platform launcher/icon sizes; no generative changes.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'assets/branding/chudlink-logo-v2.png'))
function Save-IconSize([int]$size, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap $size,$size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($source,0,0,$size,$size)
    $graphics.Dispose()
    $bitmap.Save((Join-Path $root $path),[System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
try {
    $android = 'src/ChudLink.Android/Resources'
    foreach ($item in @(@('mdpi',48),@('hdpi',72),@('xhdpi',96),@('xxhdpi',144),@('xxxhdpi',192))) {
        $folder = "$android/mipmap-$($item[0])"
        New-Item -ItemType Directory -Force $folder | Out-Null
        Save-IconSize $item[1] "$folder/ic_launcher.png"
    }
    New-Item -ItemType Directory -Force "$android/drawable" | Out-Null
    Save-IconSize 512 "$android/drawable/chudlink_mark.png"
    $gpt = [System.Drawing.Image]::FromFile((Join-Path $root 'assets/branding/chudgpt-logo-original.png'))
    $thumb = New-Object System.Drawing.Bitmap 128,128
    $draw = [System.Drawing.Graphics]::FromImage($thumb); $draw.DrawImage($gpt,0,0,128,128); $draw.Dispose(); $gpt.Dispose()
    $thumb.Save((Join-Path $root "$android/drawable/chudgpt_mark.png"),[System.Drawing.Imaging.ImageFormat]::Png); $thumb.Dispose()
    $sizes = @(16,32,48,64,128,256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap $size,$size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap); $graphics.InterpolationMode = 'HighQualityBicubic'; $graphics.DrawImage($source,0,0,$size,$size); $graphics.Dispose()
        $stream = New-Object System.IO.MemoryStream; $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png); $frames += ,$stream.ToArray(); $stream.Dispose(); $bitmap.Dispose()
    }
    $output = [System.IO.File]::Create((Join-Path $root 'src/ChudLink.Desktop/Assets/ChudLink.ico'))
    $writer = New-Object System.IO.BinaryWriter $output
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }; $writer.Dispose(); $output.Dispose()
} finally { $source.Dispose() }
