param(
    [Parameter(Mandatory = $true)]
    [string]$SkyRipperCharacters,
    [string]$Output
)

$ErrorActionPreference = "Stop"

function Clean([object]$Value) {
    if ($null -eq $Value) { return "" }
    return ([string]$Value).Replace("`t", " ").Replace("`r", " ").Replace("`n", " ").Trim()
}

function Key([string]$Value) {
    $formD = $Value.Normalize([Text.NormalizationForm]::FormD)
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $formD.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark -and
            [char]::IsLetterOrDigit($character)) {
            [void]$builder.Append([char]::ToLowerInvariant($character))
        }
    }
    return $builder.ToString()
}

$rows = foreach ($file in Get-ChildItem -LiteralPath $SkyRipperCharacters -Filter *.json) {
    $character = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $abilities = $character.abilities
    if ($abilities.basic_upgrades.Count -ne 4 -or $abilities.upgrade_paths.Count -ne 2 -or
        $abilities.upgrade_paths[0].abilities.Count -ne 3 -or $abilities.upgrade_paths[1].abilities.Count -ne 3) {
        continue
    }

    $basic = @($abilities.basic_upgrades | ForEach-Object { Clean $_.name })
    $primary = @($abilities.upgrade_paths[0].abilities | ForEach-Object { Clean $_.name })
    $secondary = @($abilities.upgrade_paths[1].abilities | ForEach-Object { Clean $_.name })
    $soulGem = @($abilities.soulgems | ForEach-Object { Clean $_.name }) -join " / "
    $wowPow = @($abilities.wow_pows | ForEach-Object { Clean $_.name }) -join " / "

    [pscustomobject]@{
        Key = Key $character.name
        Columns = @(
            (Key $character.name), (Clean $character.name),
            $basic[0], $basic[1], $basic[2], $basic[3],
            (Clean $abilities.upgrade_paths[0].path_name), $primary[0], $primary[1], $primary[2],
            (Clean $abilities.upgrade_paths[1].path_name), $secondary[0], $secondary[1], $secondary[2],
            $soulGem, $wowPow, (Clean $character.wiki_url)
        )
    }
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("# Upgrade and path names extracted from SkyRipper character JSON; names only, no descriptions or assets.")
$lines.Add("# key`tname`tbase1`tbase2`tbase3`tbase4`tprimary_path`tprimary1`tprimary2`tprimary3`tsecondary_path`tsecondary1`tsecondary2`tsecondary3`tsoul_gem`twow_pow`tsource")
foreach ($row in $rows | Sort-Object Key -Unique) {
    $lines.Add($row.Columns -join "`t")
}

$destination = if ($Output) {
    [IO.Path]::GetFullPath($Output)
} else {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SkyConfig.Core\Data\upgrades.tsv"))
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllLines($destination, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($lines.Count - 2) upgrade profiles to $destination"
