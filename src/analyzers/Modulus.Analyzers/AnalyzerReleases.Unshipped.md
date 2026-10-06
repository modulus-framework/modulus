; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MOD0001 | Security | Warning  | AnonymousEndpointAnalyzer: anonymous endpoint without a reason
MOD0002 | Security | Warning  | ClassifiedLogArgumentAnalyzer: classified value written through an unredacted log call
MOD0003 | Design   | Warning  | IntegrationEventNameAnalyzer: integration event name not declared once and well-formed
