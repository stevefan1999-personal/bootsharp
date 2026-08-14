; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; The CFW060-CFW069 block is allocated to this front end by ADR-0004 and reserved in
; Bootsharp.Cloudflare.Generate.Test/DiagnosticIdTests, which is the family-wide allocation record.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CFW060 | Bootsharp | Error | A Razor construct that needs the MVC runtime, refused by name with the substitution to use.
CFW061 | Bootsharp | Error | The Razor compiler could not compile the page.
CFW062 | Bootsharp | Error | Two declarations would land on the same generated name.
