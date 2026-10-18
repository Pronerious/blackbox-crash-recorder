# Prepara la cartella per-claude-ai\ con tutto quello che serve al progetto "BlackBox" su claude.ai
# (istruzioni, scheda del progetto, guida, README e tutto il codice raccolto in pochi file).
# Uso: powershell -ExecutionPolicy Bypass -File .\per-claude-ai.ps1
# La cartella resta solo sul PC (esclusa da Git). Si sovrascrive a ogni esecuzione.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'per-claude-ai'
New-Item -ItemType Directory -Force $out | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false

function Bundle([string]$title, [string[]]$files, [string]$dest) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine("# $title")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Raccolta automatica dei file sorgente di BlackBox (creata da per-claude-ai.ps1 il $(Get-Date -Format 'dd/MM/yyyy HH:mm')).")
    foreach ($f in $files) {
        $rel = $f.Substring($root.Length + 1)
        $ext = [IO.Path]::GetExtension($f).TrimStart('.')
        $lang = @{ cs = 'csharp'; xaml = 'xml'; ps1 = 'powershell' }[$ext]
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("## $rel")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('```' + $lang)
        [void]$sb.AppendLine([IO.File]::ReadAllText($f).TrimEnd())
        [void]$sb.AppendLine('```')
    }
    [IO.File]::WriteAllText((Join-Path $out $dest), $sb.ToString(), $utf8)
}

# testi fissi
Copy-Item (Join-Path $root 'docs\per-claude-ai\00-LEGGIMI-PROGETTO.md') $out -Force
Copy-Item (Join-Path $root 'docs\per-claude-ai\ISTRUZIONI-DA-INCOLLARE.txt') $out -Force
Copy-Item (Join-Path $root 'README.md') $out -Force
Copy-Item (Join-Path $root 'CHANGELOG.md') $out -Force
Copy-Item (Join-Path $root 'GUIDA.md') $out -Force
Copy-Item (Join-Path $root 'data\limiti.json') $out -Force
if (Test-Path (Join-Path $root 'PIANO-v1.2.md')) { Copy-Item (Join-Path $root 'PIANO-v1.2.md') $out -Force }

# codice
$src = Get-ChildItem (Join-Path $root 'src') -File | Where-Object { $_.Extension -in '.cs', '.xaml' } | Sort-Object Name | ForEach-Object FullName
Bundle 'Codice del programma (src)' $src '01-codice-programma.md'
Bundle 'Compilazione e installer' @((Join-Path $root 'build.ps1'), (Join-Path $root 'setup\Setup.cs'), (Join-Path $root 'per-claude-ai.ps1')) '02-codice-build-e-installer.md'

# storia: ultimi commit e versioni
$log = (& git -C $root log --date=short --pretty=format:'%ad  %h  %s' -n 40) -join "`n"
$tags = (& git -C $root tag --sort=-creatordate) -join ', '
[IO.File]::WriteAllText((Join-Path $out '03-storia.md'), "# Storia del progetto`n`nVersioni pubblicate: $tags`n`n## Ultimi lavori (dal più recente)`n`n``````text`n$log`n```````n", $utf8)

"Pronta: $out"
Get-ChildItem $out | Select-Object Name, @{ n = 'KB'; e = { [math]::Round($_.Length / 1KB) } } | Format-Table -AutoSize
"Su claude.ai: incolla ISTRUZIONI-DA-INCOLLARE.txt nelle istruzioni del progetto e carica gli altri file nella conoscenza."
