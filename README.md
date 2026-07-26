# Pidgeon CLI

A command-line toolkit for healthcare integration engineers: generate, validate, and
de-identify HL7 v2, FHIR R4, and NCPDP test messages with synthetic data — no real patient
data required. The CLI is also the surface an AI agent drives over MCP.

## What it does

```bash
pidgeon generate ADT^A01 --count 10 --output admissions.hl7
pidgeon validate --file labs.hl7 --mode compatibility
pidgeon deident --in ./samples --out ./synthetic --date-shift 30d
```

- **Generate** synthetic HL7 v2 and FHIR R4 messages and resources.
- **Validate** against published standard definitions, in strict and compatibility modes.
- **De-identify** message content on-device, preserving referential integrity.
- **Look up** standard reference data (segments, fields, tables).

Validation is derived from the standards' published, machine-readable definitions, so the
generator and validator are checked against the same source rather than against each other.
Run `pidgeon capabilities` for the exact standards, versions, and capability levels your build
reports.

## Install

```bash
dotnet tool install --global Pidgeon.CLI --version 0.1.0-beta.2
pidgeon --version
```

## Build from source

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
dotnet run --project pidgeon-cli/src/Pidgeon.CLI -- --help
```

## License

Pidgeon's community engine and baseline data package are licensed under the Mozilla Public
License 2.0 (see `LICENSE`). Third-party and standards acknowledgments are in `NOTICE`. HL7®
and FHIR® are registered trademarks of Health Level Seven International; their use here is
descriptive and does not constitute endorsement by HL7.

## Contributing & security

Contributions are welcome under the Developer Certificate of Origin — see
[`CONTRIBUTING.md`](CONTRIBUTING.md). To report a vulnerability, see [`SECURITY.md`](SECURITY.md).
