[CmdletBinding()]
param([string]$OutputPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\artwork-v2.jpg'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$bitmap=New-Object Drawing.Bitmap 1600,900
$graphics=[Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
try {
 $rectangle=New-Object Drawing.Rectangle 0,0,1600,900
 $background=New-Object Drawing.Drawing2D.LinearGradientBrush $rectangle,([Drawing.Color]::FromArgb(5,12,28)),([Drawing.Color]::FromArgb(19,37,63)),([single]0)
 try {$graphics.FillRectangle($background,$rectangle)} finally {$background.Dispose()}
 $random=New-Object Random 413
 foreach($spec in @(@(1040,205,630,14,95,180),@(1160,540,570,175,28,65))) {
  $path=New-Object Drawing.Drawing2D.GraphicsPath
  try {
   $path.AddEllipse([int]($spec[0]-$spec[2]/2),[int]($spec[1]-$spec[2]/2),[int]$spec[2],[int]$spec[2])
   $glow=New-Object Drawing.Drawing2D.PathGradientBrush $path
   try {
    $glow.CenterColor=[Drawing.Color]::FromArgb(145,$spec[3],$spec[4],$spec[5])
    $glow.SurroundColors=@([Drawing.Color]::FromArgb(0,$spec[3],$spec[4],$spec[5]))
    $graphics.FillPath($glow,$path)
   } finally {$glow.Dispose()}
  } finally {$path.Dispose()}
 }
 $blue=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(110,75,185,245)),([single]2)
 $red=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(100,243,69,98)),([single]2)
 try {
  for($i=0;$i -lt 9;$i++) {
   $graphics.DrawBezier($blue,[single]600,[single](200+$i*12),[single]980,[single](-80+$i*20),[single]1210,[single](920-$i*15),[single]1740,[single](500+$i*16))
   $graphics.DrawBezier($red,[single]480,[single](860+$i*8),[single]1080,[single](350+$i*12),[single]1230,[single](720-$i*20),[single]1750,[single](90+$i*24))
  }
 } finally {$blue.Dispose();$red.Dispose()}
 for($i=0;$i -lt 100;$i++) {
  $brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb($random.Next(35,140),152,192,224))
  try {$size=[single]($random.NextDouble()*2+1);$graphics.FillEllipse($brush,[single]$random.Next(650,1600),[single]$random.Next(0,900),$size,$size)} finally {$brush.Dispose()}
 }
 $parent=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
 $null=New-Item -ItemType Directory -Path $parent -Force
 $bitmap.Save([IO.Path]::GetFullPath($OutputPath),[Drawing.Imaging.ImageFormat]::Jpeg)
} finally {$graphics.Dispose();$bitmap.Dispose()}
Write-Output ('Generated abstract artwork: '+[IO.Path]::GetFullPath($OutputPath))