$Nugets = Get-ChildItem **.csproj -Name | Foreach-Object { $_.Replace(".csproj", "") }

$localNugetFolder = "C:\LocalNuget"
$localNuget = dotnet nuget update source "Local Nuget" --source $localNugetFolder
if ($localNuget -like "*error*") {
    dotnet nuget add source $localNugetFolder --name "Local Nuget"
}
if ((Test-Path $localNugetFolder) -eq $false) {
    mkdir $localNugetFolder
}
  
  
$nugetCacheFolder = "~\.nuget\packages"
Push-Location $nugetCacheFolder 
foreach ($nuget in $Nugets) {
    if ((Test-Path $nuget) -eq $true) {
        Remove-Item $nuget -Recurse -Force
    }
    if ((Test-Path "${localNugetFolder}\${nuget}") -eq $false) {
        mkdir "${localNugetFolder}\${nuget}"
    }   
}
Pop-Location

foreach ($nuget in $Nugets) {
Push-Location $nuget
    dotnet build
    dotnet pack --output "${localNugetFolder}\${nuget}" --version-suffix $env:COMPUTERNAME --no-build
    Pop-Location
}

Pop-Location
if ($true -eq $Clean) {
    dotnet clean
    dotnet build
}