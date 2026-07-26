# Pidgeon CLI

Community healthcare message generation, validation, inspection, and testing toolkit. Generate synthetic HL7, FHIR, and NCPDP messages without using real patient data.

## Install

```bash
dotnet tool install --global Pidgeon.CLI --version 0.1.0-beta.2
```

## Quick Start

```bash
# Generate an HL7 ADT admission message
pidgeon generate ADT^A01

# Generate multiple lab results
pidgeon generate ORU^R01 --count 10 --output labs.hl7

# Generate FHIR Patient resources
pidgeon generate Patient --count 5

# Validate a message file
pidgeon validate --file message.hl7

# De-identify real messages (on-device, HIPAA Safe Harbor)
pidgeon deident --in ./samples --out ./synthetic --date-shift 30d
```

## Capabilities

- Synthetic HL7 v2, FHIR R4, and NCPDP message generation
- Validation with typed findings and explicit capability reporting
- On-device de-identification and deterministic single-artifact comparison
- Standard reference lookup, field discovery, and path inspection
- Local run/session inspection, verification, replay, diff, and fork workflows
- Public baseline-data package management

Run `pidgeon capabilities` for the exact standards, versions, and capability levels present in this build.

## Documentation

- [GitHub Repository](https://github.com/PidgeonHealth/pidgeon-cli)
- [Website](https://pidgeon.health)

## License

MPL-2.0
