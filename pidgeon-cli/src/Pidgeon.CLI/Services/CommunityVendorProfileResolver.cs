// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Services;

/// <summary>
/// Community-safe validation resolver for the built-in vendor names. Built-in defaults are
/// generation-oriented and carry no vendor specification, so validation remains the deterministic
/// standards pass while accepting the same named profile lane as the commercial CLI. Saved,
/// shared, and proprietary specification stores remain outside the public composition.
/// </summary>
internal sealed class CommunityVendorProfileResolver : IValidationProfileResolver
{
    private static readonly IReadOnlySet<string> BuiltInVendorNames = new HashSet<string>(
        [
            "epic", "cerner", "meditech", "allscripts", "athenahealth",
            "nextgen", "eclinicalworks", "greenway", "drchrono", "generic"
        ],
        StringComparer.OrdinalIgnoreCase);

    public Task<ValidationProfileResolution?> ResolveAsync(string name)
    {
        var canonical = Normalize(name);
        if (!BuiltInVendorNames.Contains(canonical))
            return Task.FromResult<ValidationProfileResolution?>(null);

        return Task.FromResult<ValidationProfileResolution?>(new ValidationProfileResolution
        {
            CanonicalName = canonical,
            Source = ValidationProfileSource.BuiltInDefaults
        });
    }

    private static string Normalize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^".json".Length].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }
}
