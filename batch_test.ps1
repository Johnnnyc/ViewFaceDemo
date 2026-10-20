$exe = "bin\Debug\net9.0\ViewFaceDemo.exe"
$imageDir = "test-images"
$outFile = "result.txt"

$files = Get-ChildItem $imageDir -File | Sort-Object { [int]([System.IO.Path]::GetFileNameWithoutExtension($_.Name)) }
$total = $files.Count

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("===== Face Similarity Matrix (${total}x${total}) =====")
[void]$sb.AppendLine("")

# Header
$h = "     "
foreach ($f in $files) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $h += "  {0,3}" -f $name
}
[void]$sb.AppendLine($h)
[void]$sb.AppendLine(("     " + "-" * (4 * $total)))

$pairCount = 0
$totalPairs = $total * ($total - 1) / 2

for ($i = 0; $i -lt $total; $i++) {
    $nameI = [System.IO.Path]::GetFileNameWithoutExtension($files[$i].Name)
    $row = " {0,3}|" -f $nameI
    
    for ($j = 0; $j -lt $total; $j++) {
        if ($i -eq $j) {
            $row += "  ---"
        }
        elseif ($j -lt $i) {
            $row += "     "
        }
        else {
            $pathA = Join-Path $imageDir $files[$i].Name
            $pathB = Join-Path $imageDir $files[$j].Name
            
            $pairCount++
            Write-Progress -Activity "Comparing faces" -Status "$pairCount / $totalPairs" -PercentComplete ($pairCount * 100 / $totalPairs)
            
            try {
                $proc = Start-Process -FilePath $exe -ArgumentList "`"$pathA`"", "`"$pathB`"" -NoNewWindow -Wait -RedirectStandardOutput "stdout.txt" -RedirectStandardError "stderr.txt" -PassThru
                $stdout = Get-Content stdout.txt -Raw -ErrorAction SilentlyContinue
                
                if ($stdout -match "相似度:\s*([\d.]+)") {
                    $sim = [double]$Matches[1]
                    $row += " {0,4:F2}" -f $sim
                }
                elseif ($stdout -match "出错") {
                    $row += "  ERR"
                }
                else {
                    $row += "  N/A"
                }
            }
            catch {
                $row += "  ERR"
            }
        }
    }
    [void]$sb.AppendLine($row)
}

[void]$sb.AppendLine("")
[void]$sb.AppendLine("Threshold: >= 0.6 = same person")

$sb.ToString() | Out-File -FilePath $outFile -Encoding UTF8
Remove-Item stdout.txt, stderr.txt -ErrorAction SilentlyContinue
Write-Output "Done! Results saved to $outFile"
Write-Output $sb.ToString()