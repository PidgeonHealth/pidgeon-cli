// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Commands.Paths;

/// <summary>
/// Pure lookup helpers shared across the <c>pidgeon path</c> orchestrators.
/// Provides category grouping, path descriptions, field types,
/// and example values for semantic paths. These are placeholder stubs
/// today — once the real field-path plugin system lands, this class is
/// the single seam to replace. Keeping them here avoids duplicating the
/// three switch-expression tables across <c>ListPathOrchestrator</c>,
/// <c>ResolvePathOrchestrator</c>, and <c>ValidatePathOrchestrator</c>.
///
/// Static + pure: no DI dependencies, no state. Allowed under the
/// code-standards exception for pure functions/constants.
/// </summary>
public static class PathMetadataProvider
{
    public static Dictionary<string, Dictionary<string, string>> GroupPathsByCategory(
        IReadOnlyDictionary<string, string> paths,
        string? categoryFilter)
    {
        var groups = new Dictionary<string, Dictionary<string, string>>();

        foreach (var (path, description) in paths)
        {
            var category = GetPathCategory(path);

            if (!string.IsNullOrWhiteSpace(categoryFilter) &&
                !string.Equals(category, categoryFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!groups.ContainsKey(category))
            {
                groups[category] = new Dictionary<string, string>();
            }
            groups[category][path] = description;
        }

        return groups;
    }

    public static string GetPathCategory(string path)
    {
        var parts = path.Split('.');
        if (parts.Length == 0) return "Other";

        return parts[0].ToLowerInvariant() switch
        {
            "patient" => "Patient Demographics",
            "encounter" => "Encounter Information",
            "provider" => "Provider Information",
            "medication" => "Medication Information",
            "message" => "Message Control",
            "facility" => "Facility Information",
            _ => "Other Fields"
        };
    }

    public static string GetPathDescription(string path)
    {
        // TODO: Get actual descriptions from field path plugins
        return path switch
        {
            "patient.mrn" => "Medical Record Number",
            "patient.lastName" => "Patient Last Name",
            "patient.firstName" => "Patient First Name",
            "patient.dateOfBirth" => "Date of Birth",
            "patient.sex" => "Administrative Sex",
            "patient.phoneNumber" => "Primary Phone Number",
            "encounter.location" => "Patient Location",
            "encounter.patientClass" => "Patient Class",
            "message.sendingApplication" => "Sending Application",
            "message.sendingFacility" => "Sending Facility",
            _ => "Healthcare field"
        };
    }

    public static string GetFieldType(string path)
    {
        // TODO: Get actual field types from field path plugins
        return path switch
        {
            "patient.dateOfBirth" => "TS (Timestamp)",
            "patient.sex" => "IS (Coded Value)",
            "patient.phoneNumber" => "XTN (Telephone)",
            _ => "ST (String)"
        };
    }

    public static string GetExampleValue(string path)
    {
        // TODO: Get realistic example values from field path plugins
        return path switch
        {
            "patient.mrn" => "MR123456",
            "patient.lastName" => "Smith",
            "patient.firstName" => "John",
            "patient.dateOfBirth" => "19850315",
            "patient.sex" => "M",
            "patient.phoneNumber" => "(555) 123-4567",
            "encounter.location" => "ER-1",
            "encounter.patientClass" => "I",
            "message.sendingApplication" => "EPIC",
            "message.sendingFacility" => "General Hospital",
            _ => "example_value"
        };
    }
}
