$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "TicketCompletionTimeline.App\TicketCompletionTimeline.App.csproj"
$updaterProject = Join-Path $root "TicketCompletionTimeline.Updater\TicketCompletionTimeline.Updater.csproj"
$output = Join-Path $root "artifacts\portable-win-x64"

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Keep the license and usage documentation with the binary distribution. The
# MIT license requires its notice to travel with copies of the software, and the
# README gives a recipient enough information to run the portable folder.
foreach ($document in @("LICENSE", "README.md")) {
    Copy-Item -LiteralPath (Join-Path $root $document) -Destination (Join-Path $output $document) -Force
}

Write-Host "Portable build written to $output"

# Publish the updater into the same portable folder. It is launched from a
# temporary copy so the updater can replace the running application's folder.
dotnet publish $updaterProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "Updater publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $root "TicketCompletionTimeline.App\update-config.json") -Destination (Join-Path $output "update-config.json") -Force

# Release packages must not ship debug symbols or source-adjacent build
# artifacts. Keep the portable folder focused on the files needed to run.
Get-ChildItem -LiteralPath $output -Filter "*.pdb" -File -Recurse | Remove-Item -Force

Write-Host "Updater included in $output"
