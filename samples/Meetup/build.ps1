<#!
.SYNOPSIS
  NUKE-style entry point for the Meetup sample (Windows / PowerShell).
  Thin wrapper over the dotnet CLI — no extra dependencies.
.DESCRIPTION
  Mirrors the kgrzybek-style workflow (build.ps1 / build.sh / build.cmd):
  one command per lifecycle task. Full NUKE (a C# build project) was
  deliberately NOT added: with no test projects, no CI matrix and only a
  handful of steps, a build project would add restore cost and complexity
  without paying back. Revisit when the Roadmap test/CI items land.
.EXAMPLE
  ./build.ps1                 # Build (Debug)
  ./build.ps1 -Target Test
  ./build.ps1 -Target Run -Configuration Release
#>
[CmdletBinding()]
param(
  [ValidateSet("Clean", "Build", "Format", "Test", "Run")]
  [string]$Target = "Build",

  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

function Invoke-Step {
  param([string]$Name, [scriptblock]$Body)
  Write-Host "`n==> $Name" -ForegroundColor Cyan
  & $Body
  if ($LASTEXITCODE -ne 0) { throw "Step '$Name' failed with exit code $LASTEXITCODE." }
}

switch ($Target) {
  "Clean" {
    Invoke-Step "Clean bin/obj" {
      Get-ChildItem -Path $RepoRoot -Recurse -Directory `
        -Include "bin", "obj" | Remove-Item -Recurse -Force
    }
  }
  "Build" {
    Invoke-Step "Build Meetup.slnx ($Configuration)" {
      dotnet build "$RepoRoot/Meetup.slnx" -c $Configuration
    }
  }
  "Format" {
    Invoke-Step "Verify formatting (no writes)" {
      dotnet format "$RepoRoot/Meetup.slnx" --verify-no-changes
    }
  }
  "Test" {
    # No test projects ship with the sample yet (see README §8 Roadmap).
    # Kept as a named target so CI and muscle memory match the kgrzybek flow;
    # fails loudly instead of silently passing with zero tests.
    throw "No test projects yet — see README §8 Roadmap item 2. Add tests, then wire 'dotnet test' here."
  }
  "Run" {
    Invoke-Step "Run Meetup.Web ($Configuration)" {
      dotnet run --project "$RepoRoot/src/Web/Meetup.Web/Meetup.Web.csproj" -c $Configuration --no-build
    }
  }
}
