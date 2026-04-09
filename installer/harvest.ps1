param(
    [Parameter(Mandatory)]
    [string]$PublishDir,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$publishDir = (Resolve-Path $PublishDir).Path
$files = Get-ChildItem -Path $publishDir -File -Recurse

# Build directory tree: map each relative directory path to a WiX directory ID
$dirMap = @{}       # relativeDirPath -> wixDirId
$dirParent = @{}    # wixDirId -> parentWixDirId
$dirName = @{}      # wixDirId -> leafFolderName
$counter = 1

function Get-DirId([string]$relativeDir) {
    if ([string]::IsNullOrEmpty($relativeDir)) {
        return "INSTALLFOLDER"
    }

    if ($dirMap.ContainsKey($relativeDir)) {
        return $dirMap[$relativeDir]
    }

    $parts = $relativeDir -split '[\\\/]'
    $current = ""
    $parentId = "INSTALLFOLDER"

    foreach ($part in $parts) {
        if ($current) {
            $current = "$current\$part"
        } else {
            $current = $part
        }

        if (-not $dirMap.ContainsKey($current)) {
            $safeId = "dir_" + ($current -replace '[^a-zA-Z0-9]', '_')
            $dirMap[$current] = $safeId
            $dirParent[$safeId] = $parentId
            $dirName[$safeId] = $part
        }

        $parentId = $dirMap[$current]
    }

    return $dirMap[$relativeDir]
}

$components = @()
$componentRefs = @()

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($publishDir.Length + 1)
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    $componentId = "comp_$counter"
    $fileId = "file_$counter"
    $dirId = Get-DirId $relativeDir

    $components += @"
    <Component Id="$componentId" Directory="$dirId" Guid="*">
        <File Id="$fileId" Source="$publishDir\$relativePath" KeyPath="yes" />
    </Component>
"@
    $componentRefs += "            <ComponentRef Id=""$componentId"" />"
    $counter++
}

# Build nested directory XML
# Group by parent so we can nest them
function Build-DirXml([string]$parentId, [int]$indent) {
    $xml = ""
    $pad = " " * $indent
    foreach ($entry in $dirParent.GetEnumerator() | Where-Object { $_.Value -eq $parentId }) {
        $id = $entry.Key
        $name = $dirName[$id]
        $children = Build-DirXml $id ($indent + 4)
        if ($children) {
            $xml += "$pad<Directory Id=""$id"" Name=""$name"">`n$children$pad</Directory>`n"
        } else {
            $xml += "$pad<Directory Id=""$id"" Name=""$name"" />`n"
        }
    }
    return $xml
}

$dirXml = Build-DirXml "INSTALLFOLDER" 12

$wxs = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Fragment>
        <DirectoryRef Id="INSTALLFOLDER">
$dirXml        </DirectoryRef>
    </Fragment>
    <Fragment>
        <ComponentGroup Id="PublishOutput">
$($componentRefs -join "`n")
        </ComponentGroup>
$($components -join "`n")
    </Fragment>
</Wix>
"@

$wxs | Out-File -FilePath $OutputPath -Encoding UTF8
Write-Host "Harvested $($files.Count) files in $($dirMap.Count + 1) directories"