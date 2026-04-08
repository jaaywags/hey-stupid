param(
    [Parameter(Mandatory)]
    [string]$PublishDir,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$publishDir = (Resolve-Path $PublishDir).Path
$files = Get-ChildItem -Path $publishDir -File -Recurse

$directories = @{}
$components = @()
$componentRefs = @()
$counter = 1

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($publishDir.Length + 1)
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    $componentId = "comp_$counter"
    $fileId = "file_$counter"

    if ([string]::IsNullOrEmpty($relativeDir)) {
        $dirId = "INSTALLFOLDER"
    } else {
        $safeDirId = "dir_" + ($relativeDir -replace '[^a-zA-Z0-9]', '_')
        if (-not $directories.ContainsKey($safeDirId)) {
            $directories[$safeDirId] = $relativeDir
        }
        $dirId = $safeDirId
    }

    $components += @"
    <Component Id="$componentId" Directory="$dirId" Guid="*">
        <File Id="$fileId" Source="$publishDir\$relativePath" KeyPath="yes" />
    </Component>
"@
    $componentRefs += "            <ComponentRef Id=""$componentId"" />"
    $counter++
}

$dirElements = ""
foreach ($entry in $directories.GetEnumerator()) {
    $dirElements += "        <Directory Id=""$($entry.Key)"" Name=""$($entry.Value)"" />`n"
}

$wxs = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Fragment>
        <DirectoryRef Id="INSTALLFOLDER">
$dirElements        </DirectoryRef>
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
Write-Host "Harvested $($files.Count) files in $($directories.Count + 1) directories"