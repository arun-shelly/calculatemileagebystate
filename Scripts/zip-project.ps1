$project = "CalculateMileageByState"
$zip = "$project.zip"

if (Test-Path $zip) { Remove-Item $zip }

Compress-Archive -Path $project -DestinationPath $zip -Force

Write-Host "Created $zip"