# 生成 src/WowCapture.App/app.ico：圆角蓝底 + 白色取景角标 + 红色对角箭头。
# 16/24px 只画角标（小尺寸下箭头会糊成一团），32px 起加箭头。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-icon.ps1
# 想改配色：调下面的 $blue/$border/$white/$red 四个色值后重跑。
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\WowCapture.App\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blue = [System.Drawing.Color]::FromArgb(0xFF, 0x2F, 0x6F, 0xEB)
$border = [System.Drawing.Color]::FromArgb(0xFF, 0x1B, 0x4F, 0xC0)
$white = [System.Drawing.Color]::White
$red = [System.Drawing.Color]::FromArgb(0xFF, 0xFF, 0x3B, 0x30)

function New-RoundedRectPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $radius, $radius, 180, 90)
    $path.AddArc($rect.Right - $radius, $rect.Y, $radius, $radius, 270, 90)
    $path.AddArc($rect.Right - $radius, $rect.Bottom - $radius, $radius, $radius, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    try {
        # 1. 圆角蓝底（48px 起加一圈更深的描边，立体感只在大尺寸才有意义）。
        $pad = [Math]::Max(1.0, $s * 0.05)
        $radius = [Math]::Max(2.0, $s * 0.22)
        $rect = New-Object System.Drawing.RectangleF ($pad), ($pad), ($s - (2 * $pad)), ($s - (2 * $pad))
        $path = New-RoundedRectPath $rect $radius
        $g.FillPath((New-Object System.Drawing.SolidBrush $blue), $path)
        if ($s -ge 48) {
            $borderPen = New-Object System.Drawing.Pen $border, ([Math]::Max(1.0, $s * 0.015))
            $g.DrawPath($borderPen, $path)
            $borderPen.Dispose()
        }

        # 2. 白色取景角标：内框四角各一个 L。臂长只到 0.37s，别越过中线。
        $m = [Math]::Max(2.0, $s * 0.22)               # 内框外沿到图标边
        $a = [Math]::Max(3.0, $s * 0.15)               # 角标臂长
        $t = [Math]::Max(2.0, [Math]::Round($s * 0.10)) # 角标线宽
        $brush = New-Object System.Drawing.SolidBrush $white

        $corners = @(
            @{ X = $m;             Y = $m;             DX = 1;  DY = 1  },
            @{ X = $s - $m - $t;   Y = $m;             DX = -1; DY = 1  },
            @{ X = $m;             Y = $s - $m - $t;   DX = 1;  DY = -1 },
            @{ X = $s - $m - $t;   Y = $s - $m - $t;   DX = -1; DY = -1 }
        )

        foreach ($c in $corners) {
            # 横臂
            $g.FillRectangle($brush, [float]($c.X), [float]($c.Y), [float]($a), [float]($t))
            # 竖臂
            $g.FillRectangle($brush, [float]($c.X), [float]($c.Y), [float]($t), [float]($a))
        }

        # 3. 红色对角箭头（小尺寸不画）：穿过中心的主对角线，起止点落在角标围出的自由区内。
        if ($s -ge 32) {
            $half = $s * 0.145
            $cx = $s / 2.0
            $penWidth = [Math]::Max(2.0, [Math]::Round($s * 0.10))
            $pen = New-Object System.Drawing.Pen $red, $penWidth
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $cap = New-Object System.Drawing.Drawing2D.AdjustableArrowCap (3.0), (2.6), $true
            $pen.CustomEndCap = $cap
            $g.DrawLine($pen, [float]($cx - $half), [float]($cx + $half), [float]($cx + $half), [float]($cx - $half))
            $pen.Dispose()
        }

        $brush.Dispose()
        $path.Dispose()
    }
    finally {
        $g.Dispose()
    }

    return $bmp
}

# 逐尺寸渲染为 PNG，打包成 ICO（PNG 内嵌格式，Vista+ 全系支持）。
$streams = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $streams += ,([byte[]]$ms.ToArray())
    $ms.Dispose()
}

$fs = [System.IO.File]::Create($Output)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    $bw.Write([UInt16]0)      # 保留位
    $bw.Write([UInt16]1)      # 类型 = icon
    $bw.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $bytes = $streams[$i]
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))  # 宽（0 表示 256）
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))  # 高
        $bw.Write([byte]0)      # 调色板
        $bw.Write([byte]0)      # 保留
        $bw.Write([UInt16]1)    # 平面
        $bw.Write([UInt16]32)   # 位深
        $bw.Write([UInt32]$bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $bytes.Length
    }

    foreach ($bytes in $streams) {
        $bw.Write($bytes)
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host "已生成: $Output（$($sizes -join ' / ') px）"
